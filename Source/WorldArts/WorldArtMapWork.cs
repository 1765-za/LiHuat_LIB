using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    internal sealed class WorldArtMapWork : IExposable
    {
        private Map map;
        private Pawn caster;
        private bool impact;
        private float damage;
        private List<Thing> targets = new();
        private List<IntVec3> fillCells = new();
        private List<IntVec3> craterCells = new();
        private int targetIndex, cellIndex, craterIndex;
        public WorldArtMapWork() { }

        internal WorldArtMapWork(Map map, Pawn caster, bool impact, WorldArtProfileDef profile, WorldArtRange range)
        {
            this.map = map; this.caster = caster; this.impact = impact; damage = profile.impactDamage;
            float halfLength = profile.mapClearingRadius * range.Length / (float)profile.clearingLength;
            float halfWidth = profile.mapClearingRadius * range.Width / (float)profile.clearingLength;
            int radius = impact ? Mathf.CeilToInt(profile.mapImpactRadius * (range.Radius + 1f) / (profile.impactRadius + 1f))
                : Mathf.CeilToInt(Mathf.Sqrt(halfLength * halfLength + halfWidth * halfWidth));
            float sin = Mathf.Sin(range.Heading * Mathf.Deg2Rad), cos = Mathf.Cos(range.Heading * Mathf.Deg2Rad);
            var seen = new HashSet<Thing>();
            CellRect area = CellRect.CenteredOn(map.Center, radius).ClipInsideMap(map);
            foreach (IntVec3 cell in area)
            {
                float x = cell.x - map.Center.x, z = cell.z - map.Center.z;
                float distance = x * x + z * z;
                if (impact ? distance > radius * radius : Mathf.Abs(x * cos - z * sin) > halfWidth || Mathf.Abs(x * sin + z * cos) > halfLength) continue;
                if (impact)
                {
                    if (distance <= radius * radius * .25f) craterCells.Add(cell);
                    else fillCells.Add(cell);
                }
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing == caster || thing is Mote || thing is Filth) continue;
                    if (thing is Building || thing is Plant || impact && thing is Pawn)
                        if (seen.Add(thing)) targets.Add(thing);
                }
            }
            FleckMaker.Static(map.Center.ToVector3Shifted(), map, impact ? FleckDefOf.ExplosionFlash : FleckDefOf.PsycastAreaEffect,
                Mathf.Min(radius, 12));
            if (impact)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16f;
                    Vector3 point = map.Center.ToVector3Shifted() + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * .55f;
                    FleckMaker.Static(point, map, FleckDefOf.Smoke, 3f);
                }
        }

        internal bool Advance(ref int budget)
        {
            if (map == null || !Find.Maps.Contains(map)) return true;
            while (targetIndex < targets.Count && budget-- > 0)
            {
                Thing thing = targets[targetIndex++];
                if (thing == null || thing.Destroyed || thing.Map != map) continue;
                if (impact) thing.TakeDamage(new DamageInfo(DamageDefOf.Crush, damage, 10f, instigator: caster));
                else thing.Destroy(DestroyMode.Vanish);
            }
            while (cellIndex < fillCells.Count && budget-- > 0)
                map.terrainGrid.SetTerrain(fillCells[cellIndex++], TerrainDefOf.Soil);
            while (craterIndex < craterCells.Count && budget-- > 0)
                map.terrainGrid.SetTerrain(craterCells[craterIndex++], TerrainDefOf.Gravel);
            return targetIndex >= targets.Count && cellIndex >= fillCells.Count && craterIndex >= craterCells.Count;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                targets = targets.Skip(targetIndex).Where(t => t != null && !t.Destroyed).ToList(); targetIndex = 0;
                fillCells = fillCells.Skip(cellIndex).ToList(); cellIndex = 0;
                craterCells = craterCells.Skip(craterIndex).ToList(); craterIndex = 0;
            }
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Values.Look(ref impact, "impact");
            Scribe_Values.Look(ref damage, "damage");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
            Scribe_Collections.Look(ref fillCells, "fillCells", LookMode.Value);
            Scribe_Collections.Look(ref craterCells, "craterCells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) craterCells ??= new();
        }
    }
}
