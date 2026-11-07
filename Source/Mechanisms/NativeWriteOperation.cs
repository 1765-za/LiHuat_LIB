using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal static class NativeWriteOperation
    {
        private sealed class Invocation
        {
            internal Action Action;
            internal ExceptionDispatchInfo Error;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Callback(IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ExecuteCall(ulong authority, Callback operation, IntPtr state);
        private static readonly Callback callback = Invoke;
        private static readonly ExecuteCall execute = NativeModule.Bind<ExecuteCall>("LihuatNative_WriteOperation");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Execute(Action action)
        {
            if (Assembly.GetCallingAssembly() != typeof(NativeWriteOperation).Assembly)
                throw new InvalidOperationException("Native writes require the library publication gateway.");
            var invocation = new Invocation { Action = action };
            GCHandle handle = GCHandle.Alloc(invocation);
            try
            {
                int result = execute(NativeModule.Authority, callback, GCHandle.ToIntPtr(handle));
                invocation.Error?.Throw();
                if (result != 1) throw new InvalidOperationException("Native publication write failed: " + result);
            }
            finally { handle.Free(); }
        }

        private static int Invoke(IntPtr state)
        {
            var invocation = (Invocation)GCHandle.FromIntPtr(state).Target;
            try { invocation.Action(); return 1; }
            catch (Exception error) { invocation.Error = ExceptionDispatchInfo.Capture(error); return 0; }
        }
    }
}
