using System;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal static class NativeStartupProof
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SlotStatus
        {
            internal IntPtr Slot, Current, Control, Continuation, Displaced;
            internal ulong Publications, Contentions, Failures;
            internal uint Active, Reserved;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ReadSlot(out SlotStatus status, uint bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OwnsMemory(IntPtr address, uint bytes);
        internal static void Verify()
        {
            var read = NativeModule.Bind<ReadSlot>("LihuatNative_JitSlotStatus");
            if (read(out SlotStatus status, (uint)Marshal.SizeOf<SlotStatus>()) != 1
                || status.Active != 1 || status.Control == IntPtr.Zero || status.Continuation == IntPtr.Zero
                || status.Current != status.Control
                || !NativePlatform.TryReadPointer(status.Slot, out IntPtr current) || current != status.Control)
                throw new InvalidOperationException("Native JIT ownership did not match the live compilation slot.");
            var owns = NativeModule.Bind<OwnsMemory>("LihuatNative_Overlaps");
            // dlopen returns an opaque handle, not an ELF image base. Verify a known image address instead.
            IntPtr image = OperatingSystem.IsWindows() ? NativeModule.Handle
                : NativeModule.Export(NativeModule.Handle, "LihuatNative_Initialize");
            if (owns(image, 1) != 1 || owns(status.Control, 1) != 1 || owns(status.Slot, (uint)IntPtr.Size) != 1)
                throw new InvalidOperationException("Native startup did not retain its image and compilation slot ownership.");
        }
    }
}
