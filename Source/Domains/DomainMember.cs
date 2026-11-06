using System.Collections.Generic;
using RimWorld.Planet;
using RimWorld;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class DomainMember : IExposable
    {
        public Pawn Pawn;
        public Map Source;
        public IntVec3 Position;
        public Rot4 Rotation;
        public Thing Container;
        public Faction Faction;
        public List<int> ContainerPath;
        public string ContainerHolderType;
        public bool Returned;
        public bool Transferred;
        public bool DraftedOnEntry;
        public bool ReturnDrafted;
        public bool ReturnDraftCaptured;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn", saveDestroyedThings: true);
            Scribe_References.Look(ref Source, "sourceMap");
            Scribe_Values.Look(ref Position, "position");
            Scribe_Values.Look(ref Rotation, "rotation");
            Scribe_References.Look(ref Container, "container");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Collections.Look(ref ContainerPath, "containerPath", LookMode.Value);
            Scribe_Values.Look(ref ContainerHolderType, "containerHolderType");
            Scribe_Values.Look(ref Returned, "returned");
            Scribe_Values.Look(ref Transferred, "transferred");
            Scribe_Values.Look(ref DraftedOnEntry, "draftedOnEntry");
            Scribe_Values.Look(ref ReturnDrafted, "returnDrafted");
            Scribe_Values.Look(ref ReturnDraftCaptured, "returnDraftCaptured");
        }
    }

}
