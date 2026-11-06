using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Characters
{
    public sealed class CharacterWorldStore : WorldComponent, IThingHolder
    {
        private CharacterManager manager;
        public CharacterManager Manager => manager;
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => manager.Contents;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, manager.Contents);
        public CharacterWorldStore(World world) : base(world) { manager = new CharacterManager(this); }
        public override void FinalizeInit(bool fromLoad)
        { if (world == Current.Game?.World) LihuatGame.Current?.BindCharacters(this); }
        public override void ExposeData()
        {
            // World components load after factions and ideologies, which Pawn need initialization reads.
            Scribe_Deep.Look(ref manager, "characters", this);
            if (manager == null) manager = new CharacterManager(this);
        }
    }
}
