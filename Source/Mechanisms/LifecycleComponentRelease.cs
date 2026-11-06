using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifecycleComponentRelease
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly Action<Pawn> releaseDespawned = OriginalMethods.Create<Action<Pawn>>(
            AccessTools.Method(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.RemoveComponentsOnDespawned)));
        private static readonly Action<Pawn> releaseKilled = OriginalMethods.Create<Action<Pawn>>(
            AccessTools.Method(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.RemoveComponentsOnKilled)));
        private static readonly Action<MapPawns, Pawn> deregister = OriginalMethods.Create<Action<MapPawns, Pawn>>(
            AccessTools.Method(typeof(MapPawns), nameof(MapPawns.DeRegisterPawn)));
        private static readonly HashSet<FieldInfo> deathFields = DeathFields();

        private static HashSet<FieldInfo> DeathFields()
        {
            var fields = new HashSet<FieldInfo>();
            var code = new LifeIlReader().Read(AccessTools.Method(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.RemoveComponentsOnKilled)));
            for (int i = 1; i < code.Count; i++)
                if (code[i - 1].opcode == OpCodes.Ldnull && code[i].opcode == OpCodes.Stfld
                    && code[i].operand is FieldInfo field && field.DeclaringType == typeof(Pawn)) fields.Add(field);
            return fields;
        }

        internal static bool ReleasesOnDeath(FieldInfo field) => deathFields.Contains(field);

        internal static bool Allowed(Pawn pawn, bool killed) => pawn != null && mapIndex(pawn) < 0
            && (!killed || PawnLife.Dead(pawn) || mapIndex(pawn) < -1);
        internal static void Despawned(Pawn pawn) { if (Allowed(pawn, false)) releaseDespawned(pawn); }
        internal static void Killed(Pawn pawn) { if (Allowed(pawn, true)) releaseKilled(pawn); }
        internal static void Deregister(MapPawns map, Pawn pawn) { if (mapIndex(pawn) < 0) deregister(map, pawn); }
    }
}
