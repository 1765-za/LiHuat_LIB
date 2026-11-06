using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace LihuatLib.AstralArts
{
    internal static class AstralSpellOffense
    {
        internal static void Apply(AstralArtsService service, Pawn caster, AstralSpellKind kind, AstralSpellRank rank, LocalTargetInfo target)
        {
            float amount = rank.power * service.SpellRuntime.DamageFactor(caster);
            if (kind == AstralSpellKind.Bolt) { Hit(caster, target.Thing, rank, amount); return; }
            if (kind == AstralSpellKind.Nova)
            {
                foreach (Pawn pawn in AstralSpellTargeting.Pawns(caster, target.Cell, rank.radius, true).ToArray()) Hit(caster, pawn, rank, amount);
                var cells = GenRadial.RadialCellsAround(target.Cell, rank.radius, true)
                    .Where(c => c.InBounds(caster.Map) && GenSight.LineOfSight(target.Cell, c, caster.Map)).ToList();
                Combat.CombatProxyRegistry.PrepareTargets(caster, CellRect.CenteredOn(target.Cell, Mathf.CeilToInt(rank.radius)).ClipInsideMap(caster.Map));
                var seen = new HashSet<object>();
                foreach (IntVec3 cell in cells)
                    foreach (Thing thing in cell.GetThingList(caster.Map).ToArray())
                        if (Combat.CombatProxyRegistry.Alive(thing) && Combat.CombatProxyRegistry.HostileTo(thing, caster)
                            && seen.Add(Combat.CombatProxyRegistry.Identity(thing))) Hit(caster, thing, rank, amount);
                return;
            }
            foreach (Thing thing in AlongLine(caster, target.Cell, rank.radius).ToArray()) Hit(caster, thing, rank, amount);
        }
        private static void Hit(Pawn caster, Thing target, AstralSpellRank rank, float amount)
        {
            if (target.Destroyed || target is Pawn pawn && pawn.Dead) return;
            if (Combat.CombatProxyRegistry.Hit(target,
                new DamageInfo(DamageDefOf.Cut, amount, rank.armorPenetration, instigator: caster, intendedTarget: target), false)) return;
            target.TakeDamage(new DamageInfo(DamageDefOf.Cut, amount, rank.armorPenetration, instigator: caster, intendedTarget: target));
        }
        private static IEnumerable<Thing> AlongLine(Pawn caster, IntVec3 destination, float width)
        {
            Vector3 from = caster.Position.ToVector3Shifted(), to = destination.ToVector3Shifted(), direction = to - from;
            Combat.CombatProxyRegistry.PrepareTargets(caster, CellRect.FromLimits(Math.Min(caster.Position.x, destination.x),
                Math.Min(caster.Position.z, destination.z), Math.Max(caster.Position.x, destination.x), Math.Max(caster.Position.z, destination.z))
                .ExpandedBy(Mathf.CeilToInt(width + 1f)).ClipInsideMap(caster.Map));
            int steps = Math.Max(1, (int)Math.Ceiling(direction.magnitude * 2f));
            var seen = new HashSet<object>();
            for (int i = 1; i <= steps; i++)
            {
                Vector3 point = from + direction * i / steps;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(point.ToIntVec3(), width + .71f, true))
                {
                    if (!cell.InBounds(caster.Map) || !GenSight.LineOfSight(caster.Position, cell, caster.Map)) continue;
                    Vector3 relative = cell.ToVector3Shifted() - from;
                    float projection = Vector3.Dot(relative, direction) / direction.sqrMagnitude;
                    if (projection < 0f || projection > 1f || (relative - direction * projection).sqrMagnitude > width * width) continue;
                    foreach (Thing thing in cell.GetThingList(caster.Map))
                        if ((thing is Pawn || thing is Building || Combat.CombatProxyRegistry.Alive(thing))
                            && thing != caster && Combat.CombatProxyRegistry.HostileTo(thing, caster) && seen.Add(Combat.CombatProxyRegistry.Identity(thing))) yield return thing;
                }
            }
        }
    }
}
