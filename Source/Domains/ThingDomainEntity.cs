using LihuatLib.Core;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class ThingDomainEntity : DomainEntity
    {
        public Thing Target;
        public string Id;
        public override string Key => "thing:" + Id;
        public override IntVec3 Position => Target?.PositionHeld ?? Origin;
        public override Faction Faction => Target?.Faction;
        public override void Move(Map destination, IntVec3 cell)
        {
            if (!OperationScope.Transferring) throw new System.InvalidOperationException("Combat entity transfer requires a domain operation.");
            if (Target is { Destroyed: false } && Target.Map != destination)
                LihuatGame.Current.Domains.TransferTo(Target, destination, cell);
            Map = destination;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Id, "id");
            Scribe_References.Look(ref Target, "target", saveDestroyedThings: true);
        }
    }
}
