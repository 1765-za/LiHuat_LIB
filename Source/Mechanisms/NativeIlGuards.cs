using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    public static class NativeIlGuards
    {
        private sealed class Entry
        {
            internal readonly MethodBase Original;
            internal readonly ProtectionMethodEntry Publisher;
            internal readonly IlBoundaryPipeline Pipeline;
            internal readonly List<Delegate> Roots = new List<Delegate>();
            internal DynamicMethod Current;
            internal Entry(MethodBase original, IlBoundaryPipeline pipeline)
            { Original = original; Publisher = new ProtectionMethodEntry(original); Pipeline = pipeline; }
            internal void Publish(DynamicMethod method)
            {
                Roots.Add(IndependentIl.Compile(method));
                Publisher.Publish(method);
                Current = method;
                ExecutableWriteProtection.Track(RuntimeMethodAddress.Get(Original));
                ExecutableWriteProtection.Track(RuntimeMethodAddress.Get(method));
            }
        }

        private static readonly Dictionary<MethodBase, Entry> entries = new Dictionary<MethodBase, Entry>();
        public const int BodyOrder = 0;
        public const int ScopeOrder = 1000;
        public const int GateOrder = 2000;
        public const int TransformOrder = 3000;
        public const int ResultOrder = 4000;
        public static MethodInfo Definition(MethodInfo method) => method == null ? null : GenericMethodIdentity.Applies(method)
            ? GenericMethodIdentity.Definition(method) : method.DeclaringType == null || method.ReflectedType == method.DeclaringType
                ? method : (MethodInfo)MethodBase.GetMethodFromHandle(method.MethodHandle, method.DeclaringType.TypeHandle);
        private static MethodBase Key(MethodBase method) => method is MethodInfo info ? Definition(info) : method;
        private static string Identity(MethodInfo callback) => callback.Module.Assembly.FullName + ":" + callback.DeclaringType?.FullName + ":" + callback.MetadataToken;
        public static int Count => entries.Count + Dispatching.NativeDispatcher.GenericCount;
        public static int NativeCount => entries.Values.Count(e => e.Publisher.Route == ProtectionRoute.Native) + Dispatching.NativeDispatcher.GenericCount;
        public static int ManagedCount => entries.Values.Count(e => e.Publisher.Route == ProtectionRoute.Managed);
        public static string LastFailure => entries.Values.Select(e => e.Publisher.Failure).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        public static string NativeModule => NativeRuntime.Path;
        public static NativeIlStatus Status(MethodInfo method) => NativeRuntime.Status(method);
        public static bool Contains(MethodBase method) => method != null && (entries.ContainsKey(Key(method)) || Dispatching.NativeDispatcher.GenericControl(method) != null);
        public static bool Intact(MethodBase method) => GenericMethodIdentity.Applies(method) ? GenericBodyPublication.Status((MethodInfo)method).Intact
            : method != null && entries.TryGetValue(Key(method), out Entry entry) && entry.Publisher.NativeIntact;
        public static bool Protected(MethodBase method) => GenericMethodIdentity.Applies(method) ? Intact(method)
            : method != null && entries.TryGetValue(Key(method), out Entry entry) && entry.Publisher.Intact;
        public static int HandlerCount(MethodBase method) => method != null && entries.TryGetValue(Key(method), out Entry entry) ? entry.Pipeline?.Count ?? 1 : 0;
        public static ProtectionRoute? RouteFor(MethodBase method) => method != null && entries.TryGetValue(Key(method), out Entry entry)
            ? (ProtectionRoute?)entry.Publisher.Route : null;
        internal static MethodInfo Control(MethodBase method) => Dispatching.NativeDispatcher.GenericControl(method) ?? (method != null && entries.TryGetValue(Key(method), out Entry entry)
            && entry.Pipeline == null ? entry.Current : null);

        internal static MethodInfo WrapCopy(MethodInfo replacement, MethodBase original)
        {
            if (replacement == null || original == null || !entries.TryGetValue(Key(original), out Entry entry)) return null;
            if (entry.Pipeline == null) return entry.Current;
            DynamicMethod copy = entry.Pipeline.Compose(replacement);
            entry.Roots.Add(IndependentIl.Compile(copy));
            return copy;
        }

        internal static void InstallControls(MethodInfo[] originals, DynamicMethod[] replacements)
        {
            LibraryCapabilities.Require(LibraryCapability.IlDispatch);
            LibraryCapabilities.RequirePublicationThread();
            var prepared = new Entry[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                if (entries.ContainsKey(originals[i])) throw new InvalidOperationException("Independent entry already installed: " + originals[i]);
                prepared[i] = new Entry(originals[i], null);
                prepared[i].Roots.Add(IndependentIl.Compile(replacements[i]));
            }
            int published = 0;
            try
            {
                for (; published < originals.Length; published++)
                {
                    prepared[published].Publish(replacements[published]);
                    entries.Add(originals[published], prepared[published]);
                }
            }
            catch
            {
                for (int i = published - 1; i >= 0; i--) { entries.Remove(originals[i]); prepared[i].Publisher.Rollback(); }
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Guard(MethodInfo original, MethodInfo gate, params int[] arguments)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(gate)) throw new ArgumentException("Gate owner is not registered.", nameof(gate));
            int[] inputs = (int[])arguments.Clone();
            RegisterTransform(original, Identity(gate), GateOrder, body => IndependentIl.Guard(original, body, gate, inputs));
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UseOriginalWhen(MethodInfo original, MethodInfo applies, params int[] arguments)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(applies)) throw new ArgumentException("Body policy owner is not registered.", nameof(applies));
            DynamicMethod source = IndependentIl.Clone(original);
            int[] inputs = (int[])arguments.Clone();
            RegisterTransform(original, Identity(applies), BodyOrder,
                body => IlBoundaryEmitter.Conditional(original, body, source, applies, inputs));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Filter(MethodInfo original, MethodInfo filter)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(filter)) throw new ArgumentException("Filter owner is not registered.", nameof(filter));
            RegisterTransform(original, Identity(filter), ResultOrder, body => IndependentIl.Filter(original, body, filter));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RequireNonNullResult(MethodInfo original)
        {
            CodePublicationAuthority.RequireCaller();
            if (original == null || original.ReturnType == typeof(void) || original.ReturnType.IsValueType
                || original.ReturnType.IsByRef || original.ReturnType.IsPointer)
                throw new ArgumentException("A result contract requires a reference return type.", nameof(original));
            DynamicMethod metadataBody = IndependentIl.Clone(original);
            RegisterTransform(original, typeof(IlResultContract).FullName, ResultOrder,
                body => IlResultContract.NonNull(original, body, metadataBody));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void FilterArguments(MethodInfo original, MethodInfo filter, params int[] arguments)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(filter)) throw new ArgumentException("Filter owner is not registered.", nameof(filter));
            int[] inputs = (int[])arguments.Clone();
            RegisterTransform(original, Identity(filter), ResultOrder, body => IlBoundaryEmitter.Filter(original, body, filter, inputs));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Scoped(MethodInfo original, MethodInfo begin, int[] beforeArguments, MethodInfo end, int[] afterArguments)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(begin) || !GeneratedIntegrity.Owns(end)) throw new ArgumentException("Scope owner is not registered.");
            int[] before = (int[])beforeArguments.Clone(), after = (int[])afterArguments.Clone();
            RegisterTransform(original, Identity(begin), ScopeOrder, body => IlBoundaryEmitter.Scope(original, body, begin, before, end, after));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void GuardReturningArgument(MethodInfo original, MethodInfo gate, int deniedArgument, params int[] arguments)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(gate)) throw new ArgumentException("Gate owner is not registered.", nameof(gate));
            int[] inputs = (int[])arguments.Clone();
            RegisterTransform(original, Identity(gate), GateOrder, body => IndependentIl.Guard(original, body, gate, inputs, deniedArgument: deniedArgument));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ScopedWhen(MethodInfo original, MethodInfo applies, int[] eligibilityArguments,
            MethodInfo begin, int[] beforeArguments, MethodInfo end, int[] afterArguments)
        {
            CodePublicationAuthority.RequireCaller();
            if (!GeneratedIntegrity.Owns(applies) || !GeneratedIntegrity.Owns(begin) || !GeneratedIntegrity.Owns(end))
                throw new ArgumentException("Scope owner is not registered.");
            int[] eligibility = (int[])eligibilityArguments.Clone();
            int[] before = (int[])beforeArguments.Clone(), after = (int[])afterArguments.Clone();
            RegisterTransform(original, Identity(begin), ScopeOrder, body => IlBoundaryEmitter.Conditional(original, body,
                IlBoundaryEmitter.Scope(original, body, begin, before, end, after), applies, eligibility));
        }

        internal static void GuardTagged(MethodInfo original, MethodInfo gate, int tag, params int[] arguments)
        {
            if (!GeneratedIntegrity.Owns(gate)) throw new ArgumentException("Gate owner is not registered.", nameof(gate));
            int[] inputs = (int[])arguments.Clone();
            RegisterTransform(original, Identity(gate), GateOrder, body => IndependentIl.Guard(original, body, gate, inputs, tag));
        }

        private static void Install(MethodInfo original, Func<MethodInfo, DynamicMethod> decorate)
            => RegisterTransform(original, Identity(decorate.Method), TransformOrder, decorate);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RegisterTransform(MethodInfo original, string id, int order, Func<MethodInfo, DynamicMethod> decorate)
        {
            CodePublicationAuthority.RequireCaller();
            RegisterLayer(original, id, order, decorate, true);
        }

        internal static int RetainEntries(IEnumerable<MethodInfo> methods)
        {
            LibraryCapabilities.Require(LibraryCapability.IlDispatch);
            LibraryCapabilities.RequirePublicationThread();
            var snapshots = methods.Select(Definition).Distinct().Where(m => !entries.ContainsKey(m)).Select(m =>
            {
                IntPtr address = CoreClrEntryAddress.Code(m);
                return (Method: m, Address: address, Expected: Marshal.ReadInt64(address));
            }).ToArray();
            foreach (var snapshot in snapshots) RetainEntry(snapshot.Method);
            foreach (var snapshot in snapshots) NativeRuntime.Retain(snapshot.Method, snapshot.Address, snapshot.Expected);
            return snapshots.Length;
        }

        private static void RetainEntry(MethodInfo original)
        {
            LibraryCapabilities.Require(LibraryCapability.IlDispatch);
            LibraryCapabilities.RequirePublicationThread();
            original = Definition(original);
            if (entries.ContainsKey(original)) return;
            var pipeline = new IlBoundaryPipeline(IndependentIl.Clone(original));
            var entry = new Entry(original, pipeline);
            try { entry.Publish(pipeline.Compose()); entries.Add(original, entry); PatchIntegrity.RefreshExisting(original); }
            catch { entries.Remove(original); entry.Publisher.Rollback(); throw; }
        }

        internal static void RegisterLayer(MethodBase original, string id, int order, Func<MethodInfo, DynamicMethod> decorate, bool bypassDeveloper)
        {
            LibraryCapabilities.Require(LibraryCapability.IlDispatch);
            LibraryCapabilities.RequirePublicationThread();
            if (decorate == null || !CodePublicationAuthority.Callback(decorate.Method)) throw new ArgumentException("Transform owner is not registered.");
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A transform needs an explicit identity.", nameof(id));
            original = Key(original);
            if (entries.TryGetValue(original, out Entry existing))
            {
                if (existing.Pipeline == null) throw new InvalidOperationException("An exclusive control cannot accept gameplay transforms: " + original);
                DynamicMethod previous = existing.Current;
                existing.Pipeline.Add(id, order, decorate, bypassDeveloper);
                try { existing.Publish(existing.Pipeline.Compose()); }
                catch
                {
                    existing.Pipeline.Remove(id);
                    existing.Publish(previous);
                    throw;
                }
                return;
            }
            var pipeline = new IlBoundaryPipeline(Dispatching.NativeDispatcher.Source(original));
            pipeline.Add(id, order, decorate, bypassDeveloper);
            var entry = new Entry(original, pipeline);
            try { entry.Publish(pipeline.Compose()); entries.Add(original, entry); PatchIntegrity.RefreshExisting(original); }
            catch { entries.Remove(original); entry.Publisher.Rollback(); throw; }
        }

        internal static void RefreshBody(MethodBase original)
        {
            LibraryCapabilities.RequirePublicationThread();
            original = Key(original);
            if (!entries.TryGetValue(original, out Entry entry) || entry.Pipeline == null)
                throw new InvalidOperationException("No composable native entry: " + original);
            if (HarmonyLib.Harmony.GetPatchInfo(original) != null) { PatchIntegrity.RefreshExisting(original); return; }
            MethodInfo previous = entry.Pipeline.Source;
            entry.Pipeline.Source = Dispatching.NativeDispatcher.Source(original);
            try { entry.Publish(entry.Pipeline.Compose()); }
            catch { entry.Pipeline.Source = previous; throw; }
        }

        internal static bool RemoveTransform(MethodBase original, string id)
        {
            LibraryCapabilities.RequirePublicationThread();
            original = Key(original);
            if (!entries.TryGetValue(original, out Entry entry) || entry.Pipeline == null) return false;
            IlBoundaryPipeline.Layer layer = entry.Pipeline.Find(id);
            if (layer == null) return false;
            DynamicMethod previous = entry.Current;
            entry.Pipeline.Remove(id);
            try { entry.Publish(entry.Pipeline.Compose()); }
            catch
            {
                entry.Pipeline.Add(layer.Id, layer.Order, layer.Build, layer.BypassDeveloper);
                entry.Publish(previous);
                throw;
            }
            return true;
        }

        internal static void ReleaseRetained(MethodInfo original)
        {
            LibraryCapabilities.RequirePublicationThread();
            original = Definition(original);
            if (!entries.TryGetValue(original, out Entry entry)) return;
            if (entry.Pipeline == null || entry.Pipeline.Count != 0)
                throw new InvalidOperationException("Cannot release an entry with active protection layers.");
            entry.Publisher.Rollback();
            entries.Remove(original);
        }

        internal static MethodInfo Wrap(MethodInfo replacement, MethodBase original)
        {
            PatchProvenance.Record(replacement, original);
            if (replacement == null || original == null || !entries.TryGetValue(Key(original), out Entry entry)) return replacement;
            if (entry.Pipeline == null) return entry.Current;
            MethodInfo previous = entry.Pipeline.Source;
            entry.Pipeline.Source = replacement;
            try { return entry.Pipeline.Compose(); }
            catch { entry.Pipeline.Source = previous; throw; }
        }

        internal static bool Commit(MethodBase original, MethodBase replacement)
        {
            if (Dispatching.NativeDispatcher.GenericControl(original) != null) return true;
            if (!entries.TryGetValue(Key(original), out Entry entry)) return false;
            if (entry.Pipeline != null) entry.Publish((DynamicMethod)replacement);
            return true;
        }
    }
}
