using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureDelegateGate
    {
        private sealed class Wrapped
        {
            internal readonly Delegate Original;
            internal readonly Delegate Value;
            internal Wrapped(Delegate original, DynamicMethod method)
            {
                Original = original;
                Value = method.CreateDelegate(original.GetType(), this);
            }
        }
        private static readonly ConditionalWeakTable<Delegate, Wrapped> wrappers = new ConditionalWeakTable<Delegate, Wrapped>();

        internal static Delegate Wrap(Delegate callback)
        {
            if (callback.Target is Wrapped) return callback;
            MethodInfo invoke = callback.GetType().GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();
            if (invoke.ReturnType.IsByRef || invoke.ReturnType.IsPointer || !parameters.Any(IsPawn)) return callback;
            return wrappers.GetValue(callback, original => Build(original, invoke, parameters)).Value;
        }
        private static bool IsPawn(ParameterInfo parameter) => !parameter.IsOut
            && (parameter.ParameterType == typeof(Pawn) || parameter.ParameterType == typeof(Pawn).MakeByRefType());

        private static Wrapped Build(Delegate original, MethodInfo invoke, ParameterInfo[] parameters)
        {
            Type delegateType = original.GetType();
            var method = new DynamicMethod(Guid.NewGuid().ToString("N"), invoke.ReturnType,
                new[] { typeof(Wrapped) }.Concat(parameters.Select(p => p.ParameterType)).ToArray(), typeof(ErasureDelegateGate).Module, true);
            ILGenerator il = method.GetILGenerator();
            Label denied = il.DefineLabel();
            FieldInfo callback = AccessTools.Field(typeof(Wrapped), nameof(Wrapped.Original));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, callback);
            il.Emit(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Delegate), nameof(Delegate.Target)));
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(ErasureCallbacks), nameof(ErasureCallbacks.BlockOwner)));
            il.Emit(OpCodes.Brtrue, denied);
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!IsPawn(parameters[i])) continue;
                il.Emit(OpCodes.Ldarg, (short)(i + 1));
                if (parameters[i].ParameterType.IsByRef) il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Call, AccessTools.Method(typeof(ErasureCallbacks), nameof(ErasureCallbacks.BlockPawn)));
                il.Emit(OpCodes.Brtrue, denied);
            }
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, callback); il.Emit(OpCodes.Castclass, delegateType);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)(i + 1));
            il.Emit(OpCodes.Callvirt, invoke); il.Emit(OpCodes.Ret);
            il.MarkLabel(denied);
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut)
                {
                    il.Emit(OpCodes.Ldarg, (short)(i + 1));
                    il.Emit(OpCodes.Initobj, parameters[i].ParameterType.GetElementType());
                }
            if (invoke.ReturnType != typeof(void))
            {
                LocalBuilder result = il.DeclareLocal(invoke.ReturnType);
                il.Emit(OpCodes.Ldloca, result); il.Emit(OpCodes.Initobj, invoke.ReturnType); il.Emit(OpCodes.Ldloc, result);
            }
            il.Emit(OpCodes.Ret);
            return new Wrapped(original, method);
        }
    }
}
