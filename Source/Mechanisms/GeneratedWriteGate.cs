using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class GeneratedWriteGate
    {
        internal static DynamicMethod Build(MethodInfo original, bool ownsFields)
        {
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try
            {
                Type[] signature = IndependentIl.Parameters(original);
                var gate = new DynamicMethod(Guid.NewGuid().ToString("N"), original.ReturnType, signature, typeof(GeneratedWriteGate).Module, true);
                ILGenerator il = gate.GetILGenerator();
                Label denied = il.DefineLabel();
                il.Emit(ownsFields ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.AllowsNativeFields)));
                il.Emit(OpCodes.Brfalse, denied);
                ParameterInfo[] parameters = original.GetParameters();
                int offset = original.IsStatic ? 0 : 1;
                for (int i = 0; i < signature.Length; i++)
                {
                    Type type = signature[i];
                    if (i >= offset && parameters[i - offset].IsOut || type.IsPointer || type.IsFunctionPointer || type.IsValueType
                        || type.IsByRef && (type.GetElementType().IsValueType || type.GetElementType().IsPointer)) continue;
                    il.Emit(OpCodes.Ldarg, (short)i);
                    if (type.IsByRef) il.Emit(OpCodes.Ldind_Ref);
                    il.Emit(OpCodes.Call, AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.AllowsNativeTarget)));
                    il.Emit(OpCodes.Brfalse, denied);
                }
                for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
                il.Emit(OpCodes.Call, original);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(denied);
                DynamicWriteRejection.Emit(il, original.ReturnType, parameters, offset);
                FieldReferenceReads.RecordCopy(gate, original);
                return gate;
            }
            finally { DynamicWriteCatalogue.Building = previous; }
        }
    }
}
