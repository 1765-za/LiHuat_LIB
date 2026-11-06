using System.Collections.Generic;
using Verse;

namespace LihuatLib.Combat
{
    public sealed class RenderedTargetMap : MapComponent
    {
        private readonly Dictionary<(string, string), RenderedCombatTarget> targets = new();
        public RenderedTargetMap(Map map) : base(map) { }
        internal void Register(RenderedCombatTarget target) => targets[(target.SourceId, target.EntityId)] = target;
        internal void Remove(RenderedCombatTarget target) => targets.Remove((target.SourceId, target.EntityId));
        internal void Prepare(RenderedCombatSource source, RenderedCombatEntity entity, Pawn caster)
        {
            if (entity.NativeTarget?.Spawned == true || !entity.Alive || entity.Map != map) return;
            if (!targets.TryGetValue((source.Id, entity.Id), out RenderedCombatTarget target))
            {
                target = (RenderedCombatTarget)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Lihuat_RenderedCombatTarget"));
                target.SourceId = source.Id; target.EntityId = entity.Id;
                GenSpawn.Spawn(target, entity.Position.ToIntVec3(), map);
            }
            target.Retain(caster);
        }
    }
}
