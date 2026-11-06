using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class RatkinWatchCompatibility
    {
        internal static void Install()
        {
            Type tracker = AccessTools.TypeByName("RatkinAnomaly.RAComponent");
            if (tracker == null) return;
            NativeDispatcher.Register(AccessTools.DeclaredMethod(tracker, "WatchTracker"),
                transpiler: new DispatchMethod(typeof(RatkinWatchCompatibility), nameof(ReplaceAdmission)));
        }

        private static IEnumerable<CodeInstruction> ReplaceAdmission(IEnumerable<CodeInstruction> instructions)
        {
            var spawned = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Spawned));
            var allowed = AccessTools.Method(typeof(RatkinWatchCompatibility), nameof(CanReplace));
            foreach (CodeInstruction instruction in instructions)
                yield return instruction.Calls(spawned)
                    ? new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = allowed }
                    : instruction;
        }

        private static bool CanReplace(Thing thing) => thing.Spawned
            && (thing is not Pawn pawn || !PawnContinuity.Protects(pawn));
    }
}
