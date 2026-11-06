using System.Collections.Generic;
using LihuatLib.Domains;
using RimWorld;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralSpellTargeting
    {
        internal static string Failure(AstralEntry entry, AstralSpellDef spell, LocalTargetInfo target)
        {
            Pawn caster = entry.Pawn;
            AstralSpellRank rank = spell.Learned(entry);
            if (spell.target == AstralSpellTarget.Self)
                return spell.kind == AstralSpellKind.Mend && !AstralSpellHealing.Injured(caster) ? "Lihuat_AstralSpellNoInjury" : null;
            if (!target.IsValid || !target.Cell.InBounds(caster.Map) || target.Cell.Fogged(caster.Map)
                || caster.Position.DistanceToSquared(target.Cell) > rank.range * rank.range) return "Lihuat_AstralSpellOutOfRange";
            if (target.HasThing && (!target.Thing.Spawned || target.Thing.Map != caster.Map || target.Thing.Destroyed))
                return "Lihuat_AstralSpellBadTarget";
            if (!GenSight.LineOfSight(caster.Position, target.Cell, caster.Map)) return "Lihuat_AstralSpellNoSight";
            if (spell.kind == AstralSpellKind.Lance && target.Cell == caster.Position) return "Lihuat_AstralSpellBadTarget";
            if (spell.target == AstralSpellTarget.Enemy && (!target.HasThing || target.Thing == caster
                || !Combat.CombatProxyRegistry.HostileTo(target.Thing, caster) || target.Pawn?.Dead == true)) return "Lihuat_AstralSpellNeedEnemy";
            if (spell.target == AstralSpellTarget.Ally && (target.Pawn == null || target.Pawn.Dead || target.Pawn.HostileTo(caster)))
                return "Lihuat_AstralSpellNeedAlly";
            if (spell.kind == AstralSpellKind.Rejuvenate && !AstralSpellHealing.Injured(target.Pawn)) return "Lihuat_AstralSpellNoInjury";
            if (spell.kind == AstralSpellKind.Warp)
            {
                if (target.Cell == caster.Position || !target.Cell.Standable(caster.Map) || !DomainShellRules.AllowsMove(caster, target.Cell))
                    return "Lihuat_AstralSpellLanding";
                foreach (Thing thing in target.Cell.GetThingList(caster.Map)) if (thing is Pawn) return "Lihuat_AstralSpellLanding";
            }
            return null;
        }
        internal static IEnumerable<Pawn> Pawns(Pawn caster, IntVec3 center, float radius, bool enemies)
        {
            Map map = caster.Map;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || !GenSight.LineOfSight(center, cell, map)) continue;
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Pawn pawn && !pawn.Dead && pawn.HostileTo(caster) == enemies) yield return pawn;
            }
        }
    }
}
