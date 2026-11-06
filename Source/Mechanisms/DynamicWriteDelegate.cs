using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicWriteDelegate
    {
        private sealed class Wrapped
        {
            internal readonly Delegate Value;
            internal Wrapped(Delegate value) { Value = value; }
        }
        private static readonly ConditionalWeakTable<Delegate, Wrapped> wrappers = new ConditionalWeakTable<Delegate, Wrapped>();
        internal static Delegate Publish(DynamicMethod method, Delegate result)
        {
            if (!DynamicWriteCatalogue.Ready || DynamicWriteCatalogue.Building || result == null || result.Target is DynamicWritePolicy
                || !DynamicWriteCatalogue.TryGet(method, out var entry) || entry.Trusted) return result;
            if (!DynamicWriteCatalogue.TryPolicy(method, out FieldInfo[] fields, out bool authority)) return result;
            Delegate guarded = Protect(result, fields, authority);
            FieldReferenceReads.Record(guarded, method, result.Target);
            return guarded;
        }
        internal static Delegate Protect(Delegate result, FieldInfo[] fields, bool authority) =>
            wrappers.GetValue(result, original => new Wrapped(Build(original, fields, authority))).Value;
        private static Delegate Build(Delegate original, FieldInfo[] fields, bool authority)
        {
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try
            {
                Type delegateType = original.GetType();
                MethodInfo invoke = delegateType.GetMethod("Invoke");
                ParameterInfo[] parameters = invoke.GetParameters();
                var signature = new[] { typeof(DynamicWritePolicy) }.Concat(parameters.Select(p => p.ParameterType)).ToArray();
                var method = new DynamicMethod(Guid.NewGuid().ToString("N"), invoke.ReturnType, signature, typeof(DynamicWriteDelegate).Module, true);
                ILGenerator il = method.GetILGenerator();
                Label denied = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.Allows)));
                il.Emit(OpCodes.Brfalse, denied);
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type type = parameters[i].ParameterType;
                    if (parameters[i].IsOut || type.IsPointer || type.IsValueType || type.IsByRef && type.GetElementType().IsValueType) continue;
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg, (short)(i + 1));
                    if (type.IsByRef) il.Emit(OpCodes.Ldind_Ref);
                    il.Emit(OpCodes.Call, AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.AllowsTarget)));
                    il.Emit(OpCodes.Brfalse, denied);
                }
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.Original)));
                il.Emit(OpCodes.Castclass, delegateType);
                for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)(i + 1));
                il.Emit(OpCodes.Callvirt, invoke);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(denied);
                DynamicWriteRejection.Emit(il, invoke.ReturnType, parameters, 1);
                return method.CreateDelegate(delegateType, new DynamicWritePolicy(original, fields, authority));
            }
            finally { DynamicWriteCatalogue.Building = previous; }
        }
    }
}
