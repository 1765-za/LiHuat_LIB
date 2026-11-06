using UnityEngine;
using Verse;

namespace LihuatLib.Combat
{
    public sealed class RenderedCombatTarget : ThingWithComps
    {
        public string SourceId, EntityId;
        private Pawn caster;
        private int expiresAt;
        private RenderedCombatEntity Entity => (CombatProxyRegistry.Provider(this) as RenderedCombatSource)?.Find(Map, EntityId);
        public override string LabelNoCount => Entity?.Label ?? base.LabelNoCount;
        public override Vector3 DrawPos => Entity?.Position ?? base.DrawPos;

        internal void Retain(Pawn owner)
        {
            caster = owner;
            expiresAt = Find.TickManager.TicksGame + 180;
        }
        protected override void Tick()
        {
            base.Tick();
            RenderedCombatEntity entity = Entity;
            if (entity?.Alive != true || entity.Map != Map) { Destroy(); return; }
            bool selected = caster?.Map == Map && (caster.CurJob?.targetA.Thing == this
                || Find.CurrentMap == Map && Find.Targeter.IsTargeting);
            if (selected) expiresAt = Find.TickManager.TicksGame + 180;
            else if (Find.TickManager.TicksGame >= expiresAt) { Destroy(); return; }
            IntVec3 cell = entity.Position.ToIntVec3();
            if (!cell.InBounds(Map)) { Destroy(); return; }
            if (cell != Position) Position = cell;
        }
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = true;
            Entity?.Hit(dinfo, false);
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            map.GetComponent<RenderedTargetMap>().Register(this);
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map.GetComponent<RenderedTargetMap>().Remove(this);
            base.DeSpawn(mode);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref SourceId, "sourceId");
            Scribe_Values.Look(ref EntityId, "entityId");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Values.Look(ref expiresAt, "expiresAt");
        }
    }
}
