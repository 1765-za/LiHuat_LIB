using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;

namespace LihuatLib.Integrations
{
    internal static class BackstoryAssignmentCompatibility
    {
        internal static void Install() => NativeDispatcher.Register(
            AccessTools.PropertySetter(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.Childhood)),
            transpiler: new DispatchMethod(typeof(BackstoryAssignmentCompatibility), nameof(Assign)));

        private static IEnumerable<CodeInstruction> Assign(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var categories = AccessTools.Field(typeof(BackstoryDef), nameof(BackstoryDef.spawnCategories));
            var contains = AccessTools.Method(typeof(List<string>), nameof(List<string>.Contains));
            int index = code.FindIndex(line => line.LoadsField(categories));
            if (index < 0 || index + 2 >= code.Count || code[index + 1].opcode != OpCodes.Ldstr
                || !Equals(code[index + 1].operand, "Child") || !code[index + 2].Calls(contains))
                throw new InvalidOperationException("Childhood category check has changed.");
            var call = new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(BackstoryAssignmentCompatibility), nameof(IsChildBackstory)));
            for (int i = index; i <= index + 2; i++)
            { call.labels.AddRange(code[i].labels); call.blocks.AddRange(code[i].blocks); }
            code.RemoveRange(index, 3);
            code.Insert(index, call);
            return code;
        }

        private static bool IsChildBackstory(BackstoryDef backstory) => backstory?.spawnCategories?.Contains("Child") == true;
    }
}
