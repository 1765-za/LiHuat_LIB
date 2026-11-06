using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyWorkflowCalls
    {
        private static readonly System.Type creator = typeof(Harmony).Assembly.GetType("HarmonyLib.MethodCreator", true);
        private static readonly FieldInfo config = AccessTools.Field(creator, "config");
        private static readonly FieldInfo original = AccessTools.Field(config.FieldType, "original");

        internal static void Install()
        {
            MethodInfo emit = AccessTools.Method(typeof(Harmony).Assembly.GetType("HarmonyLib.MethodCreatorTools", true), "EmitCodes");
            NativeIlGuards.RegisterLayer(emit, typeof(HarmonyWorkflowCalls).FullName, NativeIlGuards.TransformOrder,
                body => Wrap(emit, body), false);
            foreach (MethodInfo definition in LifecycleWorkflowCalls.Definitions) PatchIntegrity.RefreshExisting(definition);
        }

        private static DynamicMethod Wrap(MethodInfo definition, MethodInfo continuation)
        {
            DynamicMethod wrapper = IndependentIl.New(definition);
            ILGenerator il = wrapper.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, config);
            il.Emit(OpCodes.Ldfld, original);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(LifecycleWorkflowCalls), nameof(LifecycleWorkflowCalls.Prepare)));
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
