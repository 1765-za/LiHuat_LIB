using System;
using HarmonyLib;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.StarBlood
{
    public static class PawnHealthExchange
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> owner =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        public static void Replace(Pawn pawn, Pawn_HealthTracker health)
        {
            if (!OperationScope.MedicalFor(pawn)) throw new InvalidOperationException("Health exchange requires a medical operation.");
            if (health?.hediffSet?.hediffs == null || owner(health) != pawn || health.hediffSet.pawn != pawn)
                throw new ArgumentException("Health tracker must be initialized for its recipient.", nameof(health));
            if (ReferenceEquals(pawn.health, health)) return;
            StarBloodRules rules = StarBloodRegistry.For(pawn);
            rules?.PrepareTick();
            PinnedHediffs.BindHealth(pawn, health.hediffSet);
            pawn.health = health;
            rules?.AcceptHealthExchange();
        }
    }
}
