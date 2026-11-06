using System;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class AvailabilityEntryEmitter
    {
        internal static DynamicMethod Wrap(MethodInfo original, MethodInfo continuation, MethodInfo gate, MethodInfo denied, int id, int target)
        {
            Type[] arguments = IndependentIl.Parameters(original);
            if (target < 0 || target >= arguments.Length || arguments[target].IsValueType
                || original.ReturnType != typeof(void) && original.ReturnType != typeof(bool)
                    && (original.ReturnType.IsValueType || original.ReturnType.IsByRef || original.ReturnType.IsPointer))
                throw new ArgumentException("Unsupported availability entry signature.");
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, id);
            il.Emit(OpCodes.Ldarg, (short)target);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(gate));
            Label execute = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, execute);
            if (original.ReturnType == typeof(bool))
            {
                il.Emit(OpCodes.Ldc_I4, id);
                il.Emit(OpCodes.Call, GeneratedIntegrity.Create(denied));
            }
            else if (original.ReturnType != typeof(void)) il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(execute);
            for (int i = 0; i < arguments.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
