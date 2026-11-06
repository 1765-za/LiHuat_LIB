using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureCallbackIl
    {
        private static readonly MethodInfo ownerGate = AccessTools.Method(typeof(ErasureCallbacks), nameof(ErasureCallbacks.BlockOwner));
        internal static bool TargetArgument(ParameterInfo parameter)
        {
            if (parameter.IsOut) return false;
            Type type = parameter.ParameterType;
            if (type.IsByRef) type = type.GetElementType();
            return typeof(Pawn).IsAssignableFrom(type) || type == typeof(Thing)
                || typeof(Hediff).IsAssignableFrom(type) || typeof(HediffComp).IsAssignableFrom(type)
                || typeof(ThingComp).IsAssignableFrom(type);
        }

        internal static DynamicMethod Build(MethodInfo original, MethodInfo continuation)
            => Build(original, continuation, ownerGate, TargetArgument);

        internal static DynamicMethod Build(MethodInfo original, MethodInfo continuation, MethodInfo gate, Func<ParameterInfo, bool> targetArgument)
        {
            DynamicMethod method = IndependentIl.New(original);
            ILGenerator il = method.GetILGenerator();
            Label denied = il.DefineLabel();
            int offset = original.IsStatic ? 0 : 1;
            if (!original.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, gate); il.Emit(OpCodes.Brtrue, denied);
            }
            ParameterInfo[] parameters = original.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!targetArgument(parameters[i])) continue;
                il.Emit(OpCodes.Ldarg, (short)(i + offset));
                if (parameters[i].ParameterType.IsByRef) il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Call, gate); il.Emit(OpCodes.Brtrue, denied);
            }
            for (int i = 0; i < parameters.Length + offset; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation); il.Emit(OpCodes.Ret);
            il.MarkLabel(denied);
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut)
                {
                    il.Emit(OpCodes.Ldarg, (short)(i + offset));
                    il.Emit(OpCodes.Initobj, parameters[i].ParameterType.GetElementType());
                }
            if (original.ReturnType != typeof(void))
            {
                LocalBuilder value = il.DeclareLocal(original.ReturnType);
                il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Initobj, original.ReturnType); il.Emit(OpCodes.Ldloc, value);
            }
            il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
