using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Domains;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(DamageWorker), nameof(DamageWorker.Apply))]
    [DispatchOrder(370)]
    internal static class DomainShellDamagePatch
    {
        [DispatchPriority(Priority.Last)]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var rounding = AccessTools.Method(typeof(GenMath), nameof(GenMath.RoundRandom), new[] { typeof(float) });
            int index = code.FindIndex(instruction => instruction.Calls(rounding));
            if (index < 0 || code.Skip(index + 1).Any(instruction => instruction.Calls(rounding)))
                throw new InvalidOperationException("DamageWorker.Apply damage rounding site differs.");
            var target = new CodeInstruction(OpCodes.Ldarg_2);
            target.labels.AddRange(code[index].labels);
            target.blocks.AddRange(code[index].blocks);
            code[index].labels.Clear();
            code[index].blocks.Clear();
            code.InsertRange(index, new[] { target, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(DomainShellDamageRules), nameof(DomainShellDamageRules.Limit))) });
            return code;
        }
    }
}
