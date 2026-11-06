using LihuatLib.Dispatching;
using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Patches
{
    internal static class FixedPawnGenerationPatch
    {
        internal static void Install()
        {
            Type utility = AccessTools.TypeByName("FixedPawnGenerate.FixedPawnUtility");
            if (utility == null) return;
            MethodInfo target = AccessTools.DeclaredMethod(utility, "GetCallerMethodName", new[] { typeof(int) });
            NativeDispatcher.Register(target, prefix: new DispatchMethod(typeof(FixedPawnGenerationPatch), nameof(Prefix)));
        }

        private static bool Prefix(ref string __result)
        {
            __result = PawnGenerationCaller.Read(new StackTrace(false));
            return false;
        }
    }
}
