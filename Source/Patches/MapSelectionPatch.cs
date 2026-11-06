using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Presentation;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(GenUI), nameof(GenUI.ThingsUnderMouse))]
    [DispatchOrder(520)]
    internal static class MapSelectionPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var add = AccessTools.Method(typeof(List<Thing>), nameof(List<Thing>.Add));
            var range = AccessTools.Method(typeof(List<Thing>), nameof(List<Thing>.AddRange));
            var admit = AccessTools.Method(typeof(MapSelectionCandidates), nameof(MapSelectionCandidates.Add));
            var admitRange = AccessTools.Method(typeof(MapSelectionCandidates), nameof(MapSelectionCandidates.AddRange));
            int singles = 0, ranges = 0;
            foreach (CodeInstruction line in instructions)
            {
                if (line.Calls(add)) { line.opcode = OpCodes.Call; line.operand = admit; singles++; }
                else if (line.Calls(range)) { line.opcode = OpCodes.Call; line.operand = admitRange; ranges++; }
                yield return line;
            }
            if (singles != 6 || ranges != 4)
                throw new InvalidOperationException("Map selection candidate call sites differ: " + singles + "/" + ranges);
        }
    }
}
