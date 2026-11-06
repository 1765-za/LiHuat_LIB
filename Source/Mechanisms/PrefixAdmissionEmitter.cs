using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class PrefixAdmissionEmitter
    {
        internal static MethodInfo Build(MethodInfo prefix, Type subjectType, int id)
        {
            ParameterInfo[] parameters = prefix.GetParameters();
            Type[] signature = parameters.Select(p => p.ParameterType).Concat(new[] { subjectType }).ToArray();
            var call = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(bool), signature, prefix.Module, true);
            ILGenerator il = call.GetILGenerator();
            Label ordinary = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4, id);
            il.Emit(OpCodes.Ldarg, (short)parameters.Length);
            il.Emit(OpCodes.Call, typeof(PrefixAdmission).GetMethod(nameof(PrefixAdmission.Protects), BindingFlags.NonPublic | BindingFlags.Static));
            il.Emit(OpCodes.Brfalse, ordinary);
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut && parameters[i].ParameterType.IsByRef)
                {
                    il.Emit(OpCodes.Ldarg, (short)i);
                    il.Emit(OpCodes.Initobj, parameters[i].ParameterType.GetElementType());
                }
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(ordinary);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, prefix);
            il.Emit(OpCodes.Ret);
            return call;
        }
    }
}
