using System.Collections.Generic;
using System.Linq;
using LihuatLib.AstralArts;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class AstralHealTargeting
    {
        internal static void Begin(AstralEntry entry, int budget)
        {
            Pawn caster = entry.Pawn;
            IntVec3 last = IntVec3.Invalid;
            int revision = -1;
            List<IntVec3> selection = null, cast = null;
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetBuildings = false }, target =>
            {
                if (!AstralHealing.Order(entry, target.Pawn, budget))
                    Messages.Message("Lihuat_AstralHealFailed".Translate(), MessageTypeDefOf.RejectInput, false);
            }, target =>
            {
                if (!caster.Spawned || caster.Map != Find.CurrentMap) return;
                if (last != caster.Position || revision != entry.SkillRevision)
                {
                    last = caster.Position; revision = entry.SkillRevision;
                    selection = Cells(caster, AstralHealing.SelectionRadius(entry));
                    cast = Cells(caster, AstralHealing.CastRadius(entry));
                }
                GenDraw.DrawFieldEdges(selection, Color.cyan);
                GenDraw.DrawFieldEdges(cast, Color.green);
            }, target => AstralHealing.CanSelect(entry, target.Pawn), caster);
        }

        private static List<IntVec3> Cells(Pawn caster, int range)
            => CellRect.CenteredOn(caster.Position, range * 2 + 1, range * 2 + 1).Where(cell => cell.InBounds(caster.Map)).ToList();
    }
}
