using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class FieldValueReader
    {
        private static readonly MethodInfo bake = AccessTools.Method(typeof(ILGenerator).Assembly.GetType("System.Reflection.Emit.RuntimeILGenerator", true), "BakeByteArray");
        private static readonly FieldInfo scope = AccessTools.Field(DynamicWriteCatalogue.GeneratorType, "m_scope");
        private static readonly MethodInfo item = AccessTools.PropertyGetter(scope.FieldType, "Item");

        internal static DynamicMethod Build(MethodInfo source)
        {
            if (!source.ReturnType.IsByRef) return null;
            List<CodeInstruction> lines;
            if (source is DynamicMethod dynamic)
            {
                if (DynamicWriteCatalogue.TryGet(dynamic, out var entry) && entry.Generator != null)
                {
                    object tokens = scope.GetValue(entry.Generator);
                    lines = LifeIlReader.Decode((byte[])bake.Invoke(entry.Generator, null), (_, key) => Resolve(item.Invoke(tokens, new object[] { key })));
                }
                else lines = DynamicIlCatalogue.Read(dynamic);
            }
            else
            {
                if (source.DeclaringType == null) return null;
                MethodBody body = NativeRuntime.SourceBody(source);
                lines = body == null ? null : LifeIlReader.Decode(source, CilRewrite.ReadSource(source, body));
            }
            if (lines == null) return null;
            var code = new List<CodeInstruction>();
            foreach (CodeInstruction line in lines)
            {
                if (line.opcode == OpCodes.Nop) continue;
                if (line.opcode == OpCodes.Call && line.operand is MethodInfo call)
                {
                    FieldInfo field = GeneratedFieldWrites.ReferenceField(call);
                    if (field != null) { code.Add(new CodeInstruction(field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, field)); continue; }
                    if (call.DeclaringType == typeof(Unsafe) && call.Name == nameof(Unsafe.AsRef) && call.GetParameters().Length == 1
                        && call.GetParameters()[0].ParameterType.IsByRef && call.ReturnType == call.GetParameters()[0].ParameterType) continue;
                    return null;
                }
                if (line.opcode != OpCodes.Ret && line.opcode != OpCodes.Ldarg && line.opcode != OpCodes.Ldarg_S
                    && (line.opcode.Value < OpCodes.Ldarg_0.Value || line.opcode.Value > OpCodes.Ldarg_3.Value)
                    && line.opcode != OpCodes.Ldfld && line.opcode != OpCodes.Ldsfld
                    && line.opcode != OpCodes.Ldflda && line.opcode != OpCodes.Ldsflda
                    && line.opcode != OpCodes.Castclass && line.opcode != OpCodes.Unbox) return null;
                if (line.labels.Count != 0 || line.blocks.Count != 0) return null;
                if ((line.opcode == OpCodes.Castclass || line.opcode == OpCodes.Unbox) && line.operand is not Type) return null;
                code.Add(new CodeInstruction(line));
            }
            if (code.Count < 2 || code[^1].opcode != OpCodes.Ret || code.Take(code.Count - 1).Any(line => line.opcode == OpCodes.Ret)
                || code[^2].operand is not FieldInfo final || final.FieldType != source.ReturnType.GetElementType()
                || code[^2].opcode != OpCodes.Ldflda && code[^2].opcode != OpCodes.Ldsflda) return null;
            code[^2].opcode = final.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;
            var reader = new DynamicMethod(Guid.NewGuid().ToString("N"), final.FieldType, IndependentIl.Parameters(source), typeof(FieldValueReader).Module, true);
            ILGenerator il = reader.GetILGenerator();
            foreach (CodeInstruction line in code)
            {
                if (line.operand == null) il.Emit(line.opcode);
                else if (line.operand is FieldInfo field) il.Emit(line.opcode, field);
                else if (line.operand is Type type) il.Emit(line.opcode, type);
                else if (line.opcode == OpCodes.Ldarg || line.opcode == OpCodes.Ldarg_S) il.Emit(OpCodes.Ldarg, Convert.ToInt16(line.operand));
                else return null;
            }
            return reader;
        }

        internal static object ResolveToken(object tokens, int key) => Resolve(item.Invoke(tokens, new object[] { key }));
        private static object Resolve(object token) => token switch
        {
            DynamicMethod method => method,
            RuntimeFieldHandle field => FieldInfo.GetFieldFromHandle(field),
            RuntimeMethodHandle method => MethodBase.GetMethodFromHandle(method),
            RuntimeTypeHandle type => Type.GetTypeFromHandle(type),
            _ => null
        };
    }
}
