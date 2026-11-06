using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class UnsafeFieldMetadata
    {
        internal static FieldInfo Resolve(MethodInfo method)
        {
            if (method == null || !method.IsStatic || !method.ReturnType.IsByRef) return null;
            var access = method.GetCustomAttribute<UnsafeAccessorAttribute>();
            if (access == null || access.Kind != UnsafeAccessorKind.Field && access.Kind != UnsafeAccessorKind.StaticField) return null;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1) return null;
            Type owner = parameters[0].ParameterType;
            if (owner.IsByRef) owner = owner.GetElementType();
            FieldInfo field = owner.GetField(access.Name ?? method.Name, BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly | (access.Kind == UnsafeAccessorKind.Field ? BindingFlags.Instance : BindingFlags.Static));
            return field?.FieldType == method.ReturnType.GetElementType() ? field : null;
        }
    }
}
