using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class SpawnCompletionEmitter
    {
        internal static DynamicMethod CatchRejected(MethodInfo original, MethodInfo continuation)
        {
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder result = il.DeclareLocal(typeof(Thing));
            il.BeginExceptionBlock();
            for (int i = 0; i < IndependentIl.Parameters(original).Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Stloc, result);
            il.BeginCatchBlock(typeof(SpawnRejectedException));
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(SpawnCompletion), nameof(SpawnCompletion.Rejected))));
            il.Emit(OpCodes.Stloc, result);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
            return wrapper;
        }

        internal static DynamicMethod ValidateSetup(MethodInfo original, MethodInfo continuation)
        {
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            for (int i = 0; i < IndependentIl.Parameters(original).Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(SpawnCompletion), nameof(SpawnCompletion.Validate))));
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
