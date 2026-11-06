using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Dispatching
{
    public static class NativeDispatcher
    {
        private sealed class Entry
        {
            internal readonly MethodBase Target;
            internal List<DispatchHandler> Handlers = new();
            internal GenericDispatchEntry Generic;
            internal Entry(MethodBase target) { Target = target; }
            internal DynamicMethod Build(MethodInfo body) => DispatchCompiler.Build(Target, body, Handlers);
        }
        private static readonly Dictionary<MethodBase, Entry> entries = new();
        private const string LayerId = "LihuatLib.Dispatching.Gameplay";
        private const int LayerOrder = 500;
        public static int Count => entries.Count;
        public static int HandlerCount => entries.Values.Sum(e => e.Handlers.Count);
        internal static int GenericCount => entries.Values.Count(e => e.Generic != null);
        internal static MethodInfo GenericControl(MethodBase target) => target is MethodInfo info
            && entries.TryGetValue(GenericMethodIdentity.Definition(info), out Entry entry) ? entry.Generic?.Control : null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void InstallAssembly(Assembly assembly)
        {
            CodePublicationAuthority.RequireCaller();
            InstallTypes(assembly.GetTypes().Where(t => t.IsDefined(typeof(DispatchTargetAttribute))).ToArray());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Install(params Type[] adapters)
        {
            CodePublicationAuthority.RequireCaller();
            InstallTypes(adapters);
        }

        private static void InstallTypes(Type[] types)
        {
            foreach (var group in types.SelectMany(DispatchMetadata.Read).GroupBy(item => item.Target))
                Add(group.Key, group.Select(item => item.Handler).ToArray());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(MethodBase target, DispatchMethod prefix = null, DispatchMethod postfix = null,
            DispatchMethod transpiler = null, DispatchMethod finalizer = null)
        {
            CodePublicationAuthority.RequireCaller();
            var handlers = new List<DispatchHandler>();
            Collect(prefix, DispatchPhase.Prefix); Collect(postfix, DispatchPhase.Postfix);
            Collect(transpiler, DispatchPhase.Transpiler); Collect(finalizer, DispatchPhase.Finalizer);
            Add(target ?? throw new ArgumentNullException(nameof(target)), handlers);
            void Collect(DispatchMethod callback, DispatchPhase phase)
            {
                if (callback != null) handlers.Add(new DispatchHandler { Method = callback.Method, Phase = phase,
                    Priority = callback.priority, Order = callback.order });
            }
        }

        private static void Add(MethodBase target, IEnumerable<DispatchHandler> added)
        {
            Core.LibraryCapabilities.RequirePublicationThread();
            if (target is MethodInfo info) target = NativeIlGuards.Definition(info);
            bool exists = entries.TryGetValue(target, out Entry entry);
            entry ??= new Entry(target);
            List<DispatchHandler> previous = entry.Handlers;
            var next = new List<DispatchHandler>(previous);
            foreach (DispatchHandler handler in added)
            {
                if (!handler.Method.IsStatic || handler.Method.ContainsGenericParameters || !CodePublicationAuthority.Metadata(handler.Method))
                    throw new ArgumentException("Dispatcher callback must be registered static metadata: " + handler.Method);
                if (next.Any(h => h.Identity == handler.Identity)) throw new InvalidOperationException("Duplicate dispatcher callback: " + handler.Method);
                if (next.Any(h => h.Phase == handler.Phase && h.Priority == handler.Priority && h.Order == handler.Order))
                    throw new InvalidOperationException("Dispatcher callback order is ambiguous: " + target + " / " + handler.Method);
                next.Add(handler);
            }
            next.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Order.CompareTo(b.Order));
            entry.Handlers = next;
            if (!exists) entries.Add(target, entry);
            bool layerAdded = false;
            try
            {
                if (GenericMethodIdentity.Applies(target))
                {
                    entry.Generic ??= new GenericDispatchEntry((MethodInfo)target);
                    entry.Generic.Publish(next);
                    return;
                }
                if (exists) NativeIlGuards.RefreshBody(target);
                else
                {
                    bool retained = NativeIlGuards.Contains(target);
                    NativeIlGuards.RegisterLayer(target, LayerId, LayerOrder, entry.Build, false);
                    layerAdded = true;
                    if (retained) NativeIlGuards.RefreshBody(target);
                }
            }
            catch
            {
                entry.Handlers = previous;
                if (GenericMethodIdentity.Applies(target))
                {
                    if (!exists) entries.Remove(target);
                    throw;
                }
                if (!exists)
                {
                    entries.Remove(target);
                    if (layerAdded)
                    {
                        NativeIlGuards.RemoveTransform(target, LayerId);
                        NativeIlGuards.RefreshBody(target);
                    }
                }
                else NativeIlGuards.RefreshBody(target);
                throw;
            }
        }

        internal static void Refresh(MethodBase target)
        {
            if (target is MethodInfo info) target = NativeIlGuards.Definition(info);
            if (!entries.ContainsKey(target)) throw new InvalidOperationException("Dispatcher target is not registered: " + target);
            if (entries[target].Generic is GenericDispatchEntry generic) { generic.Publish(entries[target].Handlers); return; }
            NativeIlGuards.RefreshBody(target);
        }

        internal static DynamicMethod Source(MethodBase target)
        {
            if (!entries.TryGetValue(target, out Entry entry) || !entry.Handlers.Any(h => h.Phase == DispatchPhase.Transpiler))
                return IndependentIl.Clone(target);
            DynamicMethod body = IndependentIl.New(target);
            body.InitLocals = NativeRuntime.SourceBody(target).InitLocals;
            ILGenerator il = body.GetILGenerator();
            EmitSource(target, il, entry.Handlers);
            return body;
        }

        internal static void EmitSource(MethodBase target, ILGenerator il, IReadOnlyList<DispatchHandler> handlers,
            Func<OpCode, MethodBase, bool> runtimeCall = null)
        {
            IEnumerable<CodeInstruction> code = PortableIlEmitter.ReadInstructions(target, il);
            foreach (DispatchHandler handler in handlers)
            {
                if (handler.Phase != DispatchPhase.Transpiler) continue;
                object[] arguments = handler.Method.GetParameters().Select(p => p.ParameterType == typeof(ILGenerator) ? (object)il
                    : p.ParameterType == typeof(MethodBase) || p.ParameterType == typeof(MethodInfo) ? target
                    : typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(p.ParameterType) ? code
                    : throw new ArgumentException("Unsupported instruction transformer argument: " + p)).ToArray();
                code = ((IEnumerable<CodeInstruction>)DispatchMetadata.Invoke(handler.Method, arguments)).ToList();
            }
            PortableIlEmitter.EmitInstructions(il, code, runtimeCall);
        }

        // Foreign Harmony wrappers consume the same transformation plan without owning its registration.
        internal static void IncludeTransforms(MethodBase target, ref List<MethodInfo> transformers)
        {
            if (!entries.TryGetValue(target, out Entry entry)) return;
            DispatchHandler[] own = entry.Handlers.Where(h => h.Phase == DispatchPhase.Transpiler).ToArray();
            if (own.Length == 0) return;
            var priorities = Harmony.GetPatchInfo(target)?.Transpilers.GroupBy(p => p.PatchMethod).ToDictionary(g => g.Key, g => g.Max(p => p.priority))
                ?? new Dictionary<MethodInfo, int>();
            var merged = transformers.Select((m, i) => (Method: m, Priority: priorities.TryGetValue(m, out int p) ? p : 400, Order: i, Own: false)).ToList();
            foreach (DispatchHandler handler in own)
                if (!merged.Any(m => m.Method == handler.Method)) merged.Add((handler.Method, handler.Priority, handler.Order, true));
            transformers = merged.OrderByDescending(m => m.Priority).ThenBy(m => m.Own).ThenBy(m => m.Order).Select(m => m.Method).ToList();
        }
    }
}
