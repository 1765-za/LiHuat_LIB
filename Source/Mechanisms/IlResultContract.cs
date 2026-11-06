using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class IlResultContract
    {
        internal static DynamicMethod NonNull(MethodInfo original, MethodInfo continuation, MethodInfo metadataBody)
        {
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            int count = IndependentIl.Parameters(original).Length;
            Label done = il.DefineLabel();
            for (int i = 0; i < count; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Pop);
            for (int i = 0; i < count; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, metadataBody);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
