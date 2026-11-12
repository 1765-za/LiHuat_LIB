using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal static class MethodIdentity
    {
        internal static MethodInfo Declaration(MethodInfo method) => method == null || method.DeclaringType == null
            || method.ReflectedType == method.DeclaringType ? method
            : (MethodInfo)MethodBase.GetMethodFromHandle(method.MethodHandle, method.DeclaringType.TypeHandle);

        internal static MethodBase Declaration(MethodBase method) => method is MethodInfo info ? Declaration(info) : method;
    }
}
