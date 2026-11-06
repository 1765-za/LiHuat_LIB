using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    public enum NativeIlState { Missing, Pending, Applied, SourceMismatch, WriteFailed, EntryFailed, JitFailed }

    public readonly struct NativeIlStatus
    {
        public NativeIlState State { get; }
        public uint Publications { get; }
        public uint CodeSites { get; }
        public bool Intact { get; }
        internal NativeIlStatus(NativeRuntime.Snapshot value)
        {
            State = (NativeIlState)value.State;
            Publications = value.Publications; CodeSites = value.Sites; Intact = value.Intact != 0;
        }
    }

    internal static class NativeRuntime
    {
        internal struct Snapshot { internal uint State, Publications, Sites, Intact; }
        private sealed class Entry
        {
            internal RuntimeDelegateBody Target;
            internal MethodBody Source;
            internal byte[] Body;
            internal bool Applied;
        }
        private static readonly Dictionary<MethodBase, Entry> entries = new Dictionary<MethodBase, Entry>();
        private static volatile bool initialized;
        private static ModuleBuilder publications;
        internal static string Path => NativeEntryBackend.Path;
        internal static string Failure { get; private set; }
        internal static bool Available => initialized;
        internal static ModuleBuilder PublicationModule(MethodBase method)
        {
            if (!Available && !TryInitialize()) throw new InvalidOperationException(Failure);
            return RuntimePublicationModules.For(method, publications);
        }

        internal static bool TryInitialize()
        {
            LibraryCapabilities.Require(LibraryCapability.RuntimeIl);
            if (initialized) return true;
            if (Failure != null) return false;
            LibraryCapabilities.RequirePublicationThread();
            try
            {
                if (!RuntimeMethodBodies.Supported) throw new PlatformNotSupportedException("Waylith method body updates are unavailable.");
                NativeEntryBackend.Initialize();
                publications = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LihuatPublications"), AssemblyBuilderAccess.Run).DefineDynamicModule("Entries");
                initialized = true; Failure = null;
                return true;
            }
            catch (Exception error) { Failure = error.ToString(); return false; }
        }
        internal static void Validate(MethodBase method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (method.ContainsGenericParameters || method.IsGenericMethod || method.DeclaringType?.IsGenericType == true
                || method.Module.Assembly.IsDynamic || method.IsAbstract
                || (method.GetMethodImplementationFlags() & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL)
                throw new NotSupportedException("Method body updates require a non-generic metadata method: " + method);
        }
        internal static void Register(MethodBase method, byte[] expected, byte[] replacement, MethodInfo destination)
        {
            if (!Available && !TryInitialize()) throw new InvalidOperationException(Failure);
            Validate(method);
            if (entries.ContainsKey(method) || !expected.SequenceEqual(RuntimeMethodBodies.Read(method)))
                throw new InvalidOperationException("Method identity or metadata body changed: " + method);
            var entry = new Entry { Source = method.GetMethodBody() };
            if (destination != null) entry.Target = new RuntimeDelegateBody(method, RuntimePublicationModules.For(method, publications));
            else
            {
                MethodBody body = method.GetMethodBody();
                entry.Body = MethodBodyBlob.Create(replacement, body.MaxStackSize, body, method);
            }
            entries.Add(method, entry);
            RuntimeBodyBoundary.Track(method);
            if (destination == null)
            {
                try { Publish(method, null); }
                catch { RuntimeBodyBoundary.Release(method); entries.Remove(method); throw; }
            }
        }
        internal static void Publish(MethodBase method, MethodInfo destination)
        {
            Entry entry = entries[method];
            entry.Target?.Publish(destination);
            Apply(method, entry);
        }
        internal static MethodBody SourceBody(MethodBase method) => (GenericMethodIdentity.Applies(method) ? GenericBodyPublication.Source((MethodInfo)method) : null)
            ?? (entries.TryGetValue(method, out Entry entry)
            && entry.Target != null ? entry.Source : method.GetMethodBody());
        internal static void Retain(MethodInfo method, IntPtr address, long expected)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (!entries.TryGetValue(method, out Entry entry) || !entry.Applied) throw new InvalidOperationException("Method entry has not been published.");
            using (new ExecutableWriteProtection.Scope(true))
            {
                _ = CoreClrEntryAddress.Code(method);
                NativeEntryBackend.Flush(method.MethodHandle.Value);
                try { NativeEntryBackend.Retain(method.MethodHandle.Value, address, expected); }
                catch (InvalidOperationException error) { throw new InvalidOperationException("Snapshot retention failed for " + method, error); }
            }
        }
        private static void Apply(MethodBase method, Entry entry)
        {
            NativeEntryBackend.Flush(method.MethodHandle.Value);
            if (entry.Applied && NativeEntryBackend.Status(method.MethodHandle.Value).Intact != 0) return;
            using var publication = new ClrCompilationScope(method);
            Integrations.HarmonyPatchOwnership.Replace(method, entry.Target?.Body ?? entry.Body, out IntPtr previous, out long expected);
            IntPtr control = CoreClrEntryAddress.Code(method);
            try
            {
                if (entry.Applied) NativeEntryBackend.Publish(method.MethodHandle.Value, previous, expected, control);
                else NativeEntryBackend.Register(method.MethodHandle.Value, method.MethodHandle.GetFunctionPointer(), previous, expected, control);
                entry.Applied = true;
            }
            catch
            {
                if (!entry.Applied) RuntimeMethodBodies.Revert(method);
                throw;
            }
        }
        internal static NativeIlStatus Status(MethodBase method)
        {
            if (GenericMethodIdentity.Applies(method)) return GenericBodyPublication.Status((MethodInfo)method);
            if (method == null || !Available || !entries.TryGetValue(method, out Entry entry)) return default;
            if (!entry.Applied) return new NativeIlStatus(new Snapshot { State = (uint)NativeIlState.Pending });
            NativeEntryBackend.Snapshot status = NativeEntryBackend.Status(method.MethodHandle.Value);
            return new NativeIlStatus(new Snapshot { State = status.State, Publications = status.Publications, Sites = status.Sites,
                Intact = status.Intact != 0 && (entry.Target == null || entry.Target.Intact) ? 1u : 0u });
        }
        internal static bool Remove(MethodBase method)
        {
            if (!entries.TryGetValue(method, out Entry entry)) return false;
            if (entry.Applied)
            {
                if (!NativeEntryBackend.Remove(method.MethodHandle.Value)) return false;
                RuntimeMethodBodies.Revert(method);
            }
            RuntimeBodyBoundary.Release(method);
            return entries.Remove(method);
        }
        internal static void FlushCompiled() => NativeEntryBackend.Flush(IntPtr.Zero);
    }
}
