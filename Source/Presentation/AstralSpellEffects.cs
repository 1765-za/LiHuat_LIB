using LihuatLib.AstralArts;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    [DefOf]
    public static class AstralSpellFlecks
    {
        public static FleckDef Lihuat_AstralAura;
        public static FleckDef Lihuat_AstralRing;
        public static FleckDef Lihuat_AstralBeam;
        static AstralSpellFlecks() { DefOfHelper.EnsureInitializedInCtor(typeof(AstralSpellFlecks)); }
    }

    internal static class AstralSpellEffects
    {
        private static readonly Color cyan = new(.35f, .9f, 1f), gold = new(1f, .76f, .25f), mint = new(.3f, 1f, .65f), violet = new(.8f, .5f, 1f);
        internal static void Cast(Pawn caster, AstralSpellDef spell, AstralSpellRank rank, LocalTargetInfo target)
        {
            if (!caster.Spawned) return;
            Map map = caster.Map;
            Vector3 center = target.Cell.ToVector3Shifted();
            switch (spell.kind)
            {
                case AstralSpellKind.Warp:
                    Stamp(caster.DrawPos, map, FleckDefOf.PsycastSkipInnerExit, cyan, 1.5f);
                    Ring(caster.DrawPos, map, 2f, cyan); break;
                case AstralSpellKind.Bolt:
                    Line(caster.DrawPos, center, map, .25f, gold);
                    Stamp(center, map, FleckDefOf.MicroSparksFast, gold, 1.4f); Ring(center, map, 1.6f, gold); break;
                case AstralSpellKind.Lance:
                    Line(caster.DrawPos, center, map, rank.radius * 2f, gold);
                    Ring(center, map, 2f, gold); break;
                case AstralSpellKind.Nova:
                    Ring(center, map, rank.radius * 2f, gold);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        Vector3 end = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rank.radius;
                        Line(center, end, map, .13f, gold);
                    }
                    break;
                case AstralSpellKind.Stasis: Ring(center, map, rank.radius * 2f, violet); break;
                case AstralSpellKind.Rain: Ring(center, map, rank.radius * 2f, mint); break;
                case AstralSpellKind.Rally: Ring(caster.DrawPos, map, rank.radius * 2f, gold); break;
                case AstralSpellKind.Mend: Heal(caster); Ring(caster.DrawPos, map, 2f, mint); break;
                case AstralSpellKind.Barrier: Ring(center, map, 2.5f, cyan); break;
                case AstralSpellKind.Rejuvenate: Bless(target.Pawn, spell.kind, rank.duration); Heal(target.Pawn); break;
                default: Bless(caster, spell.kind, rank.duration); Ring(caster.DrawPos, map, rank.radius > 0f ? rank.radius * 2f : 2f, cyan); break;
            }
        }
        internal static void Depart(Pawn pawn)
        {
            Stamp(pawn.DrawPos, pawn.Map, FleckDefOf.PsycastSkipFlashEntry, cyan, 1.2f);
            Ring(pawn.DrawPos, pawn.Map, 2f, cyan);
        }
        internal static void Bless(Pawn pawn, AstralSpellKind kind, float duration)
        {
            if (!pawn.Spawned) return;
            var data = FleckMaker.GetDataAttachedOverlay(pawn, AstralSpellFlecks.Lihuat_AstralAura, Vector3.zero, 1.8f, duration);
            data.instanceColor = kind == AstralSpellKind.Rejuvenate ? mint : kind == AstralSpellKind.Rally ? gold : cyan;
            pawn.Map.flecks.CreateFleck(data);
        }
        internal static void Heal(Pawn pawn)
        { if (pawn.Spawned) Stamp(pawn.DrawPos, pawn.Map, FleckDefOf.HealingCross, mint, 1.2f); }
        internal static void Absorb(Pawn pawn, bool broken)
        {
            if (!pawn.Spawned) return;
            Ring(pawn.DrawPos, pawn.Map, broken ? 3f : 1.8f, cyan);
            if (broken) Stamp(pawn.DrawPos, pawn.Map, FleckDefOf.MicroSparksFast, cyan, 2f);
        }
        private static void Ring(Vector3 center, Map map, float diameter, Color color)
            => Stamp(center, map, AstralSpellFlecks.Lihuat_AstralRing, color, diameter);
        private static void Line(Vector3 start, Vector3 end, Map map, float width, Color color)
        {
            Vector3 delta = end - start;
            var data = FleckMaker.GetDataStatic(start + delta * .5f, map, AstralSpellFlecks.Lihuat_AstralBeam);
            data.exactScale = new Vector3(delta.MagnitudeHorizontal(), 1f, width);
            data.rotation = Mathf.Atan2(-delta.z, delta.x) * Mathf.Rad2Deg; data.instanceColor = color;
            map.flecks.CreateFleck(data);
        }
        private static void Stamp(Vector3 center, Map map, FleckDef def, Color color, float scale)
        {
            var data = FleckMaker.GetDataStatic(center, map, def, scale); data.instanceColor = color;
            map.flecks.CreateFleck(data);
        }
    }
}
