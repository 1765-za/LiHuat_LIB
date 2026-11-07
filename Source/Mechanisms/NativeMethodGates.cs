using System;
using System.Runtime.InteropServices;
using LihuatLib.Configuration;

namespace LihuatLib.Mechanisms
{
    internal static class NativeMethodGates
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Snapshot
        {
            internal uint State, Count, Mask, Failure;
            internal ulong Allowed, Rejected;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InstallCall(ulong authority);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatusCall(out Snapshot status, uint bytes);

        internal static void Install()
        {
            if (!HighMechanismPolicy.NativeProtection) return;
            int result = NativeModule.Bind<InstallCall>("LihuatNative_InstallMethodGates")(NativeModule.Authority);
            var read = NativeModule.Bind<StatusCall>("LihuatNative_MethodGateStatus");
            if (read(out Snapshot status, (uint)Marshal.SizeOf<Snapshot>()) != 1 || result != 1
                || status.State != 1 || status.Count != 17 || status.Mask != (1u << 17) - 1)
                throw new InvalidOperationException("Native memory method installation failed: result=" + result
                    + " mask=" + status.Mask + " entry=" + status.Failure);
        }
    }
}
