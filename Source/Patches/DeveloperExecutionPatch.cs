using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(DebugActionNode), nameof(DebugActionNode.Enter))]
    [DispatchOrder(190)]
    internal static class DeveloperActionPatch
    {
        static void Prefix(DebugActionNode __instance) => __instance.action = DeveloperExecution.Wrap(__instance.action);
    }

    [DispatchTarget]
    [DispatchOrder(200)]
    internal static class DeveloperToolPatch
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredConstructors(typeof(DebugTool));
        static void Prefix(MethodBase __originalMethod, object[] __args)
        {
            ParameterInfo[] parameters = __originalMethod.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].Name == "clickAction" && __args[i] is Action action) __args[i] = DeveloperExecution.Wrap(action);
        }
    }

    [DispatchTarget(typeof(WindowStack), nameof(WindowStack.Add))]
    [DispatchOrder(210)]
    internal static class DeveloperWindowCreationPatch
    {
        static void Prefix(Window window) => DeveloperExecution.Track(window);
    }

    [DispatchTarget(typeof(Window), "InnerWindowOnGUI")]
    [DispatchOrder(220)]
    internal static class DeveloperWindowActionPatch
    {
        static void Prefix(Window __instance, out DeveloperExecution.Scope __state) => __state = DeveloperExecution.EnterWindow(__instance);
        static Exception Finalizer(Exception __exception, DeveloperExecution.Scope __state)
        { __state?.Dispose(); return __exception; }
    }

    [DispatchTarget]
    [DispatchOrder(230)]
    internal static class DeveloperQueuedActionPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(LongEventHandler)))
                if (method.Name == nameof(LongEventHandler.QueueLongEvent) || method.Name == nameof(LongEventHandler.ExecuteWhenFinished)) yield return method;
        }
        static void Prefix(object[] __args)
        {
            if (!DeveloperExecution.Active) return;
            for (int i = 0; i < __args.Length; i++) if (__args[i] is Action action) __args[i] = DeveloperExecution.Wrap(action);
        }
    }
}
