using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    internal static class RegenerationEmitter
    {
        internal static DynamicMethod Wrap(MethodInfo original, MethodInfo continuation, int id, int source, int map)
        {
            Type[] signature = IndependentIl.Parameters(original);
            DynamicMethod result = IndependentIl.New(original);
            result.InitLocals = true;
            ILGenerator il = result.GetILGenerator();
            LocalBuilder scope = il.DeclareLocal(typeof(RegenerationFlow.Scope));
            LocalBuilder value = original.ReturnType == typeof(void) ? null : il.DeclareLocal(original.ReturnType);
            Label done = il.DefineLabel(), denied = il.DefineLabel();
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldc_I4, id);
            LoadArgument(il, signature, source);
            LoadArgument(il, signature, map);
            il.Emit(OpCodes.Ldloca, scope);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(RegenerationGuard), nameof(RegenerationGuard.Begin))));
            il.Emit(OpCodes.Brfalse, denied);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            if (value != null) il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(denied);
            ClearOutputs(il, original);
            il.MarkLabel(done);
            il.BeginCatchBlock(typeof(RegenerationDeniedException));
            il.Emit(OpCodes.Pop);
            ClearOutputs(il, original);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloc, scope);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(RegenerationGuard), nameof(RegenerationGuard.End))));
            il.EndExceptionBlock();
            if (value != null) il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            return result;
        }
        private static void LoadArgument(ILGenerator il, Type[] signature, int index)
        {
            if (index < 0) { il.Emit(OpCodes.Ldnull); return; }
            il.Emit(OpCodes.Ldarg, (short)index);
            if (signature[index].IsByRef) il.Emit(OpCodes.Ldind_Ref);
        }
        private static void ClearOutputs(ILGenerator il, MethodInfo original)
        {
            ParameterInfo[] parameters = original.GetParameters();
            int offset = original.IsStatic ? 0 : 1;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut && parameters[i].ParameterType.IsByRef)
                {
                    il.Emit(OpCodes.Ldarg, (short)(i + offset));
                    il.Emit(OpCodes.Initobj, parameters[i].ParameterType.GetElementType());
                }
        }
    }
}
