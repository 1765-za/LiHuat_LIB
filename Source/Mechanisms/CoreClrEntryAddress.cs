using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal static class CoreClrEntryAddress
    {
        internal static IntPtr Code(MethodBase method)
        {
            IntPtr precode = method.MethodHandle.GetFunctionPointer();
            IntPtr result = IntPtr.Zero;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                precode = method.MethodHandle.GetFunctionPointer();
                result = NativeEntryBackend.Code(precode);
                // Tier transitions can reset a prepared entry to its prestub before the snapshot is read.
                if (result != precode) break;
            }
            if (result == IntPtr.Zero || result == precode || (result.ToInt64() & 7) != 0)
                throw new NotSupportedException("CoreCLR code entry is unavailable or unaligned: " + method
                    + " precode=" + precode.ToString("X") + " code=" + result.ToString("X"));
            return result;
        }
    }
}
