using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class ReflectionInvocationBoundary
    {
        internal static void Install()
        {
            Type runtime = typeof(MethodInfo).Assembly.GetType("System.Reflection.RuntimeMethodInfo", true);
            MethodInfo invoke = AccessTools.DeclaredMethod(runtime, nameof(MethodInfo.Invoke));
            NativeIlGuards.RegisterTransform(invoke, typeof(ReflectionInvocationBoundary).FullName, NativeIlGuards.BodyOrder, body => Rewrite(invoke, body));
            NativeIlGuards.Guard(invoke,
                AccessTools.Method(typeof(ReflectionInvocationBoundary), nameof(Before)), 0, 1, 4);
        }
        private static DynamicMethod Rewrite(MethodInfo invoke, MethodInfo continuation)
        {
            DynamicMethod wrapper = IndependentIl.New(invoke);
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder body = il.DeclareLocal(typeof(DynamicMethod));
            Label original = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(GeneratedMethodBoundary), nameof(GeneratedMethodBoundary.InvocationBody)));
            il.Emit(OpCodes.Stloc, body);
            il.Emit(OpCodes.Ldloc, body);
            il.Emit(OpCodes.Brfalse, original);
            il.Emit(OpCodes.Ldloc, body);
            for (short i = 0; i < 6; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(GeneratedMethodBoundary), nameof(GeneratedMethodBoundary.Invoke)));
            il.Emit(OpCodes.Ret);
            il.MarkLabel(original);
            for (short i = 0; i < 6; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
        private static void Before(MethodInfo method, object instance, object[] arguments)
        {
            GeneratedMethodBoundary.RequireInvoke(method, instance, arguments);
            if (LibraryCapabilities.Has(LibraryCapability.StarBlood)) LifeRetirementGuard.ObserveInvocation(method, instance, arguments);
        }
    }
}
