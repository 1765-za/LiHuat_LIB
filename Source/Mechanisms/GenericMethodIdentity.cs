using System;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal static class GenericMethodIdentity
    {
        internal static bool Applies(MethodBase method) => method is MethodInfo
            && (method.IsGenericMethod || method.DeclaringType?.IsGenericType == true);

        internal static MethodInfo Definition(MethodInfo method)
        {
            method = MethodIdentity.Declaration(method);
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition) method = method.GetGenericMethodDefinition();
            Type owner = method.DeclaringType;
            if (owner?.IsConstructedGenericType == true)
                method = (MethodInfo)method.Module.ResolveMethod(method.MetadataToken);
            return method;
        }
    }
}
