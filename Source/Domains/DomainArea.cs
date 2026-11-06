using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Domains
{
    [Flags]
    public enum DomainPressure { None = 0, Benefits = 1, Resistance = 2, SpecialDamage = 4, Abilities = 8, All = 15 }

    public class DomainArea : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string EncounterId;
        public string ProviderId;
        public Pawn Owner;
        public Map Map;
        public Map HomeMap;
        public Map Source;
        public DomainReturnPath ReturnPath;
        public PocketMapParent Parent;
        public IntVec3 Anchor;
        public DomainPressure Pressure = DomainPressure.All;
        public bool AffectsOwner;
        public bool CollapseOnOwnerDeath = true;
        public bool TerminalDeaths;
        public DomainProfileDef Profile;
        public int Size;
        public float ExternalBarrierRadius;
        public IntVec3 Center;
        public IntVec3 CenterOffset;
        public float Stability;
        public Thing Shell;
        public bool ShellBound;
        public int LastShellRecoveryAt;
        public int ShellCapacity;
        public List<DomainMember> Members = new List<DomainMember>();
        public List<DomainEntity> Entities = new();
        public readonly HashSet<Pawn> MemberSet = new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance);
        public int CreatedAt;
        public bool EntryCommitted;
        public bool Ending;
        public string EndReason;
        public bool ClosurePrepared;
        public bool Closed;

        public bool Enclosed => (Profile?.kind ?? DomainKind.Closed) == DomainKind.Closed;
        public IntVec3 MapCenter => Enclosed && Map != null ? Map.Center + CenterOffset : Center;
        public CellRect Footprint => Map == null ? CellRect.Empty : CellRect.CenteredOn(MapCenter, Size, Size);
        public CellRect SourceFootprint => CellRect.CenteredOn(Anchor, Size, Size);
        public bool Covers(Pawn pawn) => pawn != null && pawn.MapHeld == Map && CoversCell(pawn.PositionHeld);
        public bool CoversCell(IntVec3 cell) => ExternalBarrierRadius > 0f
            ? cell.IsValid && cell.DistanceToSquared(MapCenter) <= ExternalBarrierRadius * ExternalBarrierRadius
            : Footprint.Contains(cell);
        public bool Contains(Pawn pawn) => pawn != null && MemberSet.Contains(pawn);
        public void RebuildIndex()
        {
            MemberSet.Clear();
            foreach (DomainMember member in Members) if (!member.Returned && member.Pawn != null) MemberSet.Add(member.Pawn);
        }
        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref Id, "domainId");
            Scribe_Values.Look(ref EncounterId, "encounterId");
            Scribe_Values.Look(ref ProviderId, "providerId");
            Scribe_References.Look(ref Owner, "owner", saveDestroyedThings: true);
            Scribe_References.Look(ref Map, "map");
            Scribe_References.Look(ref HomeMap, "homeMap");
            Scribe_References.Look(ref Source, "source");
            Scribe_Deep.Look(ref ReturnPath, "returnPath");
            Scribe_References.Look(ref Parent, "parent");
            Scribe_Values.Look(ref Anchor, "anchor");
            Scribe_Values.Look(ref Pressure, "pressure", DomainPressure.All);
            Scribe_Values.Look(ref AffectsOwner, "affectsOwner");
            Scribe_Values.Look(ref CollapseOnOwnerDeath, "collapseOnOwnerDeath", true);
            Scribe_Values.Look(ref TerminalDeaths, "terminalDeaths");
            Scribe_Defs.Look(ref Profile, "profile");
            Scribe_Values.Look(ref Size, "size");
            Scribe_Values.Look(ref ExternalBarrierRadius, "externalBarrierRadius");
            Scribe_Values.Look(ref Center, "center");
            Scribe_Values.Look(ref CenterOffset, "centerOffset");
            Scribe_Values.Look(ref Stability, "stability");
            Scribe_References.Look(ref Shell, "shell");
            Scribe_Values.Look(ref ShellBound, "shellBound");
            Scribe_Values.Look(ref LastShellRecoveryAt, "lastShellRecoveryAt");
            Scribe_Values.Look(ref ShellCapacity, "shellCapacity");
            Scribe_Collections.Look(ref Members, "members", LookMode.Deep);
            Scribe_Collections.Look(ref Entities, "entities", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && Entities == null) Entities = new();
            Scribe_Values.Look(ref CreatedAt, "createdAt");
            Scribe_Values.Look(ref EntryCommitted, "entryCommitted", true);
            Scribe_Values.Look(ref Ending, "ending");
            Scribe_Values.Look(ref EndReason, "endReason");
            Scribe_Values.Look(ref ClosurePrepared, "closurePrepared");
            Scribe_Values.Look(ref Closed, "closed");
        }
    }
}
