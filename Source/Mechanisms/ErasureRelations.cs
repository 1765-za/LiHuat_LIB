using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureRelations
    {
        private static readonly AccessTools.FieldRef<Pawn_RelationsTracker, HashSet<Pawn>> incoming =
            AccessTools.FieldRefAccess<Pawn_RelationsTracker, HashSet<Pawn>>("pawnsWithDirectRelationsWithMe");

        public static void Remove(Pawn pawn)
        {
            if (pawn.relations == null) return;
            foreach (Pawn other in incoming(pawn.relations).ToArray())
            {
                if (other.relations != null)
                    foreach (DirectPawnRelation relation in other.relations.DirectRelations.ToArray())
                        if (ReferenceEquals(relation.otherPawn, pawn)) ReferenceListStorage<DirectPawnRelation>.Remove(other.relations.DirectRelations, relation);
                ReferenceSetStorage<Pawn>.Remove(incoming(pawn.relations), other);
            }
            foreach (DirectPawnRelation relation in pawn.relations.DirectRelations.ToArray())
            {
                if (relation.otherPawn?.relations != null) ReferenceSetStorage<Pawn>.Remove(incoming(relation.otherPawn.relations), pawn);
                ReferenceListStorage<DirectPawnRelation>.Remove(pawn.relations.DirectRelations, relation);
            }
        }
    }
}
