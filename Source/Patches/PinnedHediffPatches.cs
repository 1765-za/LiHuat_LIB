using LihuatLib.Dispatching;
using System.Collections.Generic;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(HediffSet), nameof(HediffSet.Clear))]
    [DispatchOrder(540)]
    internal static class PinnedHediffClearPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase __originalMethod)
            => HediffMutationGuard.Transpile(instructions, __originalMethod);
    }
}
