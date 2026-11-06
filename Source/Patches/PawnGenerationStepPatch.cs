using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    [DispatchOrder(530)]
    internal static class PawnGenerationStepPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var generate = AccessTools.Method(typeof(GenStep), nameof(GenStep.Generate));
            var boundary = AccessTools.Method(typeof(PawnGenerationStep), nameof(PawnGenerationStep.Run));
            int replaced = 0;
            foreach (CodeInstruction line in instructions)
            {
                if (line.Calls(generate))
                {
                    line.opcode = OpCodes.Call;
                    line.operand = boundary;
                    replaced++;
                }
                yield return line;
            }
            if (replaced != 1) throw new InvalidOperationException("Map generation step dispatch changed.");
        }
    }
}
