using System;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal readonly struct ClrCompilationScope : IDisposable
    {
        private readonly IntPtr previous;
        internal ClrCompilationScope(MethodBase method) => previous = NativeEntryBackend.Publication(method.MethodHandle.Value);
        public void Dispose() => NativeEntryBackend.Publication(previous);
    }
}
