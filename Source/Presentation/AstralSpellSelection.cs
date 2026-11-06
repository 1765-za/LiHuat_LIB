using System.Collections.Generic;
using LihuatLib.AstralArts;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class AstralSpellSelection
    {
        internal static void Begin(AstralEntry entry, AstralSpellDef spell)
        {
            Pawn caster = entry.Pawn;
            Map map = caster.Map;
            AstralSpellRank rank = spell.Learned(entry);
            Combat.CombatProxyRegistry.PrepareTargets(caster, CellRect.CenteredOn(caster.Position, Mathf.CeilToInt(rank.range)).ClipInsideMap(map));
            IntVec3 previous = IntVec3.Invalid, previousArea = IntVec3.Invalid;
            List<IntVec3> range = null, area = null;
            bool Valid(LocalTargetInfo target) => caster.Spawned && caster.Map == map && Find.CurrentMap == map
                && LihuatGame.Current.AstralArts.SpellCastFailure(entry, spell, target) == null;
            Find.Targeter.BeginTargeting(new TargetingParameters {
                canTargetLocations = spell.target == AstralSpellTarget.Cell, canTargetPawns = spell.target != AstralSpellTarget.Cell,
                canTargetBuildings = spell.target == AstralSpellTarget.Enemy,
                canTargetItems = spell.target == AstralSpellTarget.Enemy,
                mustBeSelectable = false, mapObjectTargetsMustBeAutoAttackable = false,
                validator = info => info.Thing == null || info.Thing is Pawn || info.Thing is Building || Combat.CombatProxyRegistry.Alive(info.Thing)
            }, target => { if (Valid(target)) AstralSpellGizmos.Cast(entry, spell, target); }, target =>
            {
                if (!caster.Spawned || caster.Map != map || Find.CurrentMap != map) return;
                if (previous != caster.Position) { previous = caster.Position; range = Cells(previous, rank.range, map); }
                GenDraw.DrawFieldEdges(range, new Color(.35f, .8f, 1f));
                if (target.IsValid && target.Cell.InBounds(map))
                {
                    if (spell.kind == AstralSpellKind.Lance) GenDraw.DrawLineBetween(caster.DrawPos, target.Cell.ToVector3Shifted());
                    else if (rank.radius > 0f)
                    {
                        if (previousArea != target.Cell) { previousArea = target.Cell; area = Cells(target.Cell, rank.radius, map); }
                        GenDraw.DrawFieldEdges(area, new Color(1f, .8f, .35f));
                    }
                }
            }, Valid, caster);
        }
        private static List<IntVec3> Cells(IntVec3 center, float radius, Map map)
        {
            var result = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true)) if (cell.InBounds(map)) result.Add(cell);
            return result;
        }
    }
}
