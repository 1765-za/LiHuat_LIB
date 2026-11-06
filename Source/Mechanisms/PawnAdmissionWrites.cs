using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class PawnAdmissionWrites
    {
        private static readonly FieldInfo mapState = AccessTools.Field(typeof(Thing), "mapIndexOrState");
        private static readonly FieldInfo discarded = AccessTools.Field(typeof(Thing), "markedForDiscard");
        private static readonly FieldInfo healthState = AccessTools.Field(typeof(Pawn_HealthTracker), "healthState");
        private static readonly FieldInfo healthTracker = AccessTools.Field(typeof(Pawn), nameof(Pawn.health));
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthOwner =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        internal static bool Allows(FieldInfo field, object target, object value)
        {
            if (field != mapState && field != discarded && field != healthState && field != healthTracker) return true;
            LihuatGame game = LihuatGame.Current;
            if (game == null || game.Lifecycle.AllowsGeneration()) return true;
            Pawn pawn = target as Pawn ?? (target is Pawn_HealthTracker health ? healthOwner(health) : null);
            if (pawn == null) return true;
            if (field == mapState && value != null && Convert.ToInt32(value) >= 0)
                return game.Lifecycle.AllowsRegistration(pawn);
            if ((field == healthState && value != null && Convert.ToInt32(value) != (int)PawnHealthState.Dead
                    || field == healthTracker) && PawnLife.Dead(pawn)
                || field == discarded && value is false && pawn.Discarded)
                return game.Lifecycle.AllowsRestore(pawn);
            return true;
        }
    }
}
