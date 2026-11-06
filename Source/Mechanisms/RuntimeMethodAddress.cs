using System;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class RuntimeMethodAddress
    {
        private static readonly Func<DynamicMethod, RuntimeMethodHandle> descriptor = typeof(DynamicMethod)
            .GetMethod("GetMethodDescriptor", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate<Func<DynamicMethod, RuntimeMethodHandle>>();

        internal static IntPtr Get(MethodBase method) => (method is DynamicMethod dynamicMethod
            ? descriptor(dynamicMethod) : method.MethodHandle).GetFunctionPointer();
    }
}
