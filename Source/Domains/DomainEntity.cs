using Verse;

namespace LihuatLib.Domains
{
    public abstract class DomainEntity : IExposable
    {
        public Map Source;
        public IntVec3 Origin;
        public Map Map;
        public abstract string Key { get; }
        public abstract RimWorld.Faction Faction { get; }
        public abstract IntVec3 Position { get; }
        public abstract void Move(Map destination, IntVec3 cell);
        public virtual void ExposeData()
        {
            Scribe_References.Look(ref Source, "source");
            Scribe_Values.Look(ref Origin, "origin");
            Scribe_References.Look(ref Map, "map");
        }
    }

    public sealed class DomainEntityMove : IExposable
    {
        public DomainEntity Entity;
        public Map Map;
        public IntVec3 Position;
        public void ExposeData()
        {
            Scribe_Deep.Look(ref Entity, "entity");
            Scribe_References.Look(ref Map, "map");
            Scribe_Values.Look(ref Position, "position");
        }
    }
}
