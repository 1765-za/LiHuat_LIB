using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class OberoniaIncidentCompatibility
    {
        private const string LetterKey = "OA_OptionalDiplomaticOffices";
        private static Func<Faction> faction;
        private static FieldInfo letter;

        internal static void Install()
        {
            Type worker = AccessTools.TypeByName("OberoniaAurea.IncidentWorker_OptionalDiplomaticOffices");
            if (worker == null) return;
            Type utility = worker.Assembly.GetType("OberoniaAurea.ModUtility", true);
            faction = AccessTools.PropertyGetter(utility, "OAFaction").CreateDelegate<Func<Faction>>();
            letter = AccessTools.Field(worker, "letterText");
            NativeDispatcher.Register(AccessTools.Constructor(worker),
                transpiler: new DispatchMethod(typeof(OberoniaIncidentCompatibility), nameof(DeferLetter)));
            NativeDispatcher.Register(AccessTools.DeclaredMethod(worker, "TryExecuteWorker"),
                prefix: new DispatchMethod(typeof(OberoniaIncidentCompatibility), nameof(PrepareLetter)));
        }

        private static IEnumerable<CodeInstruction> DeferLetter(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int start = code.FindIndex(line => line.opcode == OpCodes.Ldstr && Equals(line.operand, LetterKey));
            int end = start < 0 ? -1 : code.FindIndex(start, line => line.opcode == OpCodes.Stfld && Equals(line.operand, letter));
            if (start < 1 || end <= start || code[start - 1].opcode != OpCodes.Ldarg_0)
                throw new InvalidOperationException("Oberonia diplomatic letter initializer has changed.");
            var empty = new CodeInstruction(OpCodes.Ldstr, string.Empty);
            for (int i = start; i < end; i++)
            { empty.labels.AddRange(code[i].labels); empty.blocks.AddRange(code[i].blocks); }
            code.RemoveRange(start, end - start); code.Insert(start, empty);
            return code;
        }

        private static bool PrepareLetter(object __instance, ref bool __result)
        {
            Faction current = faction();
            if (current == null) { __result = false; return false; }
            letter.SetValue(__instance, LetterKey.Translate(current.NameColored, current.leader).ToString());
            return true;
        }
    }
}
