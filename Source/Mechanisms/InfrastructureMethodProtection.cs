using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal static class InfrastructureMethodProtection
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MetadataBody
        {
            internal IntPtr Owner, Method, Code, Precode;
            internal uint Length;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ProtectCall(ulong authority, [In] MetadataBody[] methods, uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EntryCall(ulong authority, IntPtr method, IntPtr precode);
        private static readonly ConcurrentDictionary<Assembly, byte> protectedAssemblies = new();
        private static readonly ConcurrentDictionary<Module, Dictionary<int, IntPtr>> definitionsByModule = new();
        private static ProtectCall protect;
        private static EntryCall protectEntry;
        private static bool installed;
        private static bool entryTracking;
        internal static int Methods { get; private set; }

        internal static void Install()
        {
            if (installed || !Configuration.HighMechanismPolicy.NativeProtection) return;
            protect = NativeModule.Bind<ProtectCall>("LihuatNative_ProtectMetadata");
            protectEntry = NativeModule.Bind<EntryCall>("LihuatNative_ProtectEntry");
            installed = true;
            foreach (Assembly assembly in InfrastructureAssemblies.Snapshot()) Register(assembly);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Assembly assembly)
        {
            if (!installed || protectedAssemblies.ContainsKey(assembly)) return;
            if (!InfrastructureAssemblies.Contains(assembly) || assembly.IsDynamic || assembly.IsCollectible)
                throw new InvalidOperationException("Method protection requires a retained infrastructure assembly.");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var definitions = assembly.GetTypes().SelectMany(type => type.GetMethods(flags).Cast<MethodBase>()
                .Concat(type.GetConstructors(flags))).Where(method => !method.IsAbstract
                    && (method.GetMethodImplementationFlags() & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.IL)
                .Select(method => (Method: method, Body: method.GetMethodBody())).Where(entry => entry.Body != null).ToArray();
            var pins = new List<GCHandle>(definitions.Length);
            try
            {
                var metadata = new MetadataBody[definitions.Length];
                for (int i = 0; i < metadata.Length; ++i)
                {
                    MethodBase method = definitions[i].Method;
                    byte[] code = definitions[i].Body.GetILAsByteArray();
                    GCHandle pin = GCHandle.Alloc(code, GCHandleType.Pinned);
                    pins.Add(pin);
                    metadata[i] = new MetadataBody
                    {
                        Owner = method.DeclaringType.TypeHandle.Value,
                        Method = method.MethodHandle.Value,
                        Code = pin.AddrOfPinnedObject(),
                        Precode = method.ContainsGenericParameters ? IntPtr.Zero : method.MethodHandle.GetFunctionPointer(),
                        Length = (uint)code.Length
                    };
                }
                int result = protect(NativeModule.Authority, metadata, (uint)metadata.Length);
                if (result != 1) throw new InvalidOperationException("Native infrastructure method protection failed: "
                    + assembly.GetName().Name + " result=" + result.ToString("X8"));
                foreach (var module in definitions.GroupBy(entry => entry.Method.Module))
                    definitionsByModule.TryAdd(module.Key, module.ToDictionary(entry => entry.Method.MetadataToken,
                        entry => entry.Method.MethodHandle.Value));
                protectedAssemblies.TryAdd(assembly, 0);
                Methods += metadata.Length;
            }
            finally { foreach (GCHandle pin in pins) pin.Free(); }
        }

        internal static bool Contains(MethodBase method) => installed && method?.DeclaringType != null
            && !method.Module.Assembly.IsDynamic && protectedAssemblies.ContainsKey(method.Module.Assembly);

        internal static void InstallEntryBoundary()
        {
            if (!installed || entryTracking) return;
            MethodInfo entry = typeof(RuntimeMethodHandle).GetMethod(nameof(RuntimeMethodHandle.GetFunctionPointer), Type.EmptyTypes);
            MethodInfo filter = typeof(InfrastructureMethodProtection).GetMethod(nameof(Published), BindingFlags.NonPublic | BindingFlags.Static);
            NativeIlGuards.Filter(entry, filter);
            entryTracking = true;
        }

        private static IntPtr Published(ref RuntimeMethodHandle handle, IntPtr result)
        {
            if (!entryTracking || result == IntPtr.Zero) return result;
            MethodBase method = RuntimeBodyBoundary.Resolve(handle);
            if (!Contains(method) || !definitionsByModule.TryGetValue(method.Module, out var definitions)
                || !definitions.TryGetValue(method.MetadataToken, out IntPtr definition)) return result;
            int accepted = protectEntry(NativeModule.Authority, definition, result);
            if (accepted != 1) throw new InvalidOperationException("Native infrastructure entry protection failed: "
                + method + " result=" + accepted);
            return result;
        }
    }
}
