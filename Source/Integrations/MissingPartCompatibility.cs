using LihuatLib.Dispatching;
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class MissingPartCompatibility
    {
        internal static void Install()
        {
            Type adapter = AccessTools.TypeByName("RiftPiercer.MissingPartCompatibilityPatch");
            if (adapter == null) return;
            string[] names = { "AddHediffHediff_Prefix", "AddHediffDef_Prefix", "HediffSetAddDirect_Prefix", "HediffMissingPartPostAdd_Prefix" };
            string[] prefixes = { nameof(HediffPrefix), nameof(DefinitionPrefix), nameof(DirectPrefix), nameof(AddedPrefix) };
            var targets = new MethodInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
                targets[i] = AccessTools.DeclaredMethod(adapter, names[i]) ?? throw new MissingMethodException(adapter.FullName, names[i]);
            for (int i = 0; i < targets.Length; i++)
                NativeDispatcher.Register(targets[i], prefix: new DispatchMethod(typeof(MissingPartCompatibility), prefixes[i]));
            Log.Message("[LihuatLib] Missing-part compatibility installed (4 entries).");
        }

        private static bool HediffPrefix(Pawn_HealthTracker __0, Hediff __1, BodyPartRecord __2, ref bool __result)
        {
            BodyPartRecord part = __2 ?? __1?.Part;
            if (!MissingPartWriteRules.CanAdd(__0?.hediffSet?.pawn, __1?.def, part)) return true;
            if (__1.Part == null) __1.Part = part;
            __result = true;
            return false;
        }

        private static bool DefinitionPrefix(Pawn_HealthTracker __0, HediffDef __1, BodyPartRecord __2, ref bool __result)
        {
            if (!MissingPartWriteRules.CanAdd(__0?.hediffSet?.pawn, __1, __2)) return true;
            __result = true;
            return false;
        }

        private static bool DirectPrefix(HediffSet __0, Hediff __1, ref bool __result)
        {
            if (!MissingPartWriteRules.CanAdd(__0?.pawn, __1?.def, __1?.Part)) return true;
            __result = true;
            return false;
        }

        private static bool AddedPrefix(Hediff_MissingPart __0, ref bool __result)
        {
            if (!MissingPartWriteRules.IsRegistered(__0)) return true;
            __result = true;
            return false;
        }
    }
}
