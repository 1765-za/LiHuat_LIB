using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyJitSetup
    {
        private static Delegate installed;
        private static Delegate originalRoot;
        private static IntPtr slot, address;
        private static readonly object sync = new object();
        internal static IntPtr Address => address;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static unsafe void Install()
        {
            lock (sync) InstallLocked();
        }

        private static unsafe void InstallLocked()
        {
            if (installed != null)
            {
                if (NativeEntryBackend.Initialized) NativeEntryBackend.EnsureCompilationObserver();
                else Publish();
                return;
            }
            Assembly harmony = typeof(Harmony).Assembly;
            Type platform = harmony.GetType("MonoMod.Core.Platforms.PlatformTriple", true);
            object runtime = platform.GetProperty("Runtime").GetValue(platform.GetProperty("Current").GetValue(null));
            Type runtime21 = harmony.GetType("MonoMod.Core.Platforms.Runtimes.Core21Runtime", true);
            Delegate original = (Delegate)runtime21.GetField("ourCompileMethod", Instance).GetValue(runtime);
            if (original == null) return;
            var body = HarmonyJitStackWrapper.Create(original.Method);
            Type runtimeBase = harmony.GetType("MonoMod.Core.Platforms.Runtimes.CoreBaseRuntime", true);
            IntPtr jit = (IntPtr)runtimeBase.GetProperty("JitObject", Instance).GetValue(runtime);
            int index = (int)runtime21.GetProperty("VtableIndexICorJitCompilerCompileMethod", Instance).GetValue(runtime);
            if (!NativePlatform.TryReadPointer(jit, out IntPtr table) || table == IntPtr.Zero || index < 0)
                throw new InvalidOperationException("Cannot read the CoreCLR JIT table.");
            slot = table + checked(index * IntPtr.Size);
            Delegate replacement = body?.CreateDelegate(original.GetType(), original.Target) ?? original;
            address = Marshal.GetFunctionPointerForDelegate(replacement);
            byte* entry; uint size;
            ((delegate* unmanaged<IntPtr, IntPtr, void*, uint, byte**, uint*, int>)address)(IntPtr.Zero, IntPtr.Zero, null, 0, &entry, &size);
            // A callback already loaded by the compiler can outlive its slot publication.
            originalRoot = original;
            installed = replacement;
            Publish();
        }

        private static unsafe void Publish()
        {
            ref IntPtr location = ref *(IntPtr*)slot;
            if (!NativePlatform.TryReadPointer(slot, out IntPtr current))
                throw new InvalidOperationException("Cannot read the JIT callback slot.");
            if (current == address) return;
            NativePlatform.SlotProtection protection = NativePlatform.MakeSlotWritable(slot);
            bool changed = false;
            IntPtr previous = IntPtr.Zero;
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    previous = Volatile.Read(ref location);
                    if (previous == address) return;
                    if (Interlocked.CompareExchange(ref location, address, previous) == previous) { changed = true; break; }
                }
            }
            finally
            {
                if (!protection.Restore())
                {
                    if (changed) Interlocked.CompareExchange(ref location, previous, address);
                    protection.Restore();
                    throw new InvalidOperationException("Cannot restore JIT table protection.");
                }
            }
            if (Volatile.Read(ref location) != address) throw new InvalidOperationException("JIT callback changed during publication.");
        }
    }
}
