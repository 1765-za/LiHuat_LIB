using System;
using System.Runtime.InteropServices;
using LihuatLib.Mechanisms;

namespace LihuatLib.Experimental.VirtualEntities
{
    internal sealed unsafe class VmNativeMemory : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SampleCall(IntPtr address, uint length, byte* previous, byte* current,
            uint* offsets, uint capacity, out uint changed);
        private IntPtr module;
        private readonly SampleCall sample;
        internal VmNativeMemory()
        {
            module = NativeLibrary.Load(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(NativeEntryBackend.Path), "LihuatVmExperimental.dll"));
            try { sample = Marshal.GetDelegateForFunctionPointer<SampleCall>(NativeLibrary.GetExport(module, "LihuatVm_Sample")); }
            catch { NativeLibrary.Free(module); module = IntPtr.Zero; throw; }
        }

        internal bool Sample(IntPtr address, byte[] baseline, byte[] current, int offset, int length,
            uint[] changes, out uint changed)
        {
            fixed (byte* before = baseline)
            fixed (byte* after = current)
            fixed (uint* offsets = changes)
                return sample(address, (uint)length, baseline == null ? null : before + offset,
                    after + offset, offsets, (uint)changes.Length, out changed) == 1;
        }

        public void Dispose()
        { if (module == IntPtr.Zero) return; NativeLibrary.Free(module); module = IntPtr.Zero; }
    }
}
