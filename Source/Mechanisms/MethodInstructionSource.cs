using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class MethodInstructionSource
    {
        private static readonly MethodInfo bake = AccessTools.Method(typeof(ILGenerator).Assembly.GetType("System.Reflection.Emit.RuntimeILGenerator", true), "BakeByteArray");
        private static readonly FieldInfo generator = AccessTools.Field(typeof(DynamicMethod), "_ilGenerator");
        private static readonly FieldInfo scope = AccessTools.Field(DynamicWriteCatalogue.GeneratorType, "m_scope");
        private static readonly MethodInfo item = AccessTools.PropertyGetter(scope.FieldType, "Item");

        internal static List<CodeInstruction> Read(MethodInfo source)
        {
            if (source is DynamicMethod dynamic)
            {
                if (generator.GetValue(dynamic) is ILGenerator il)
                {
                    object tokens = scope.GetValue(il);
                    return LifeIlReader.Decode((byte[])bake.Invoke(il, null), (_, key) => ResolveToken(tokens, key));
                }
                return DynamicIlCatalogue.Read(dynamic);
            }
            if (source.DeclaringType == null) return null;
            MethodBody body = NativeRuntime.SourceBody(source);
            return body == null ? null : LifeIlReader.Decode(source, CilRewrite.ReadSource(source, body));
        }

        internal static object ResolveToken(object tokens, int key) => item.Invoke(tokens, new object[] { key }) switch
        {
            DynamicMethod method => method,
            RuntimeFieldHandle field => FieldInfo.GetFieldFromHandle(field),
            RuntimeMethodHandle method => MethodBase.GetMethodFromHandle(method),
            RuntimeTypeHandle type => Type.GetTypeFromHandle(type),
            _ => null
        };
    }
}
