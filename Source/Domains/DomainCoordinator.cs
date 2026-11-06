using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using LihuatLib.Domains;
using LihuatLib.Mechanisms;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator : IExposable
    {
        internal sealed class Encounter
        {
            public readonly List<DomainArea> Areas = new List<DomainArea>();
            public readonly HashSet<Pawn> Members = new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance);
            public Map Map;
            public bool Contested => Areas.Count(a => !a.Ending && !a.Closed) > 1;
        }
        private List<DomainArea> external = new List<DomainArea>();
        private DomainRollback rollback;
        private bool pendingClosure;
        private float nextFinishAt;
        private readonly Dictionary<string, DomainArea> byId = new Dictionary<string, DomainArea>();
        private readonly Dictionary<string, Encounter> encounters = new Dictionary<string, Encounter>();
        private readonly Dictionary<Pawn, Encounter> byPawn = new Dictionary<Pawn, Encounter>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<Map, Encounter> byMap = new Dictionary<Map, Encounter>();
        private readonly Dictionary<Pawn, DomainArea> byOwner = new Dictionary<Pawn, DomainArea>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<Pawn, DomainPressure> observedPressure = new Dictionary<Pawn, DomainPressure>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<Pawn, float> observedStrength = new Dictionary<Pawn, float>(Core.ReferenceIdentity<Pawn>.Instance);
        public IEnumerable<DomainArea> Areas => external;
        public readonly DomainBarrierService Barriers = new DomainBarrierService();
        internal bool HasAreas => byId.Count != 0;
        public event Action<Pawn> PressureChanged;
        public event Action<DomainArea> Opened;
        public event Action<DomainArea> Closed;
        public event Action<Pawn> Captured;
        public Func<Pawn, bool> DenyLivingTransfer;
        public DomainArea GetArea(string id) => id != null && byId.TryGetValue(id, out DomainArea area) ? area : null;
        public DomainArea OwnedBy(Pawn pawn) => pawn != null && byOwner.TryGetValue(pawn, out DomainArea area) && !area.Ending ? area : null;
        public bool Contains(Pawn pawn) => pawn != null && byPawn.ContainsKey(pawn);
        public Map SourceFor(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out Encounter group) ? group.Areas[0].Source : null;
        public DomainMember OriginFor(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out Encounter group)
            ? group.Areas.SelectMany(a => a.Members).FirstOrDefault(m => m.Pawn == pawn) : null;
        public bool SameEncounter(Pawn first, Pawn second) => byPawn.TryGetValue(first, out Encounter a)
            ? byPawn.TryGetValue(second, out Encounter b) && a == b : !byPawn.ContainsKey(second);
        public bool CanContain(Pawn pawn, Map map) => byPawn.TryGetValue(pawn, out Encounter group) ? group.Map == map : map == null || !byMap.ContainsKey(map);
        public bool Protects(Map map) => map != null && (byMap.ContainsKey(map) || Areas.Any(a => a.Source == map)
            || rollback?.Area.HomeMap == map || rollback?.Positions.Any(p => p.Map == map) == true);

        public void Initialize()
        {
            foreach (DomainArea area in Areas)
            {
                if (area.Id == null) area.Id = Guid.NewGuid().ToString("N");
                if (area.EncounterId == null) area.EncounterId = area.Id;
                if (area.Profile == null) area.Profile = DomainProfileDef.For(DomainKind.Closed);
                if (area.Enclosed && area.HomeMap == null) area.HomeMap = area.Map;
                if (area.Size == 0) { area.Size = area.HomeMap.Size.x; area.Stability = area.Profile.stability; }
                if (area.ShellCapacity == 0) area.ShellCapacity = area.Profile.ShellCapacity(area.Size);
                area.ReturnPath ??= DomainReturnPath.Capture(area.Source);
                foreach (DomainMember member in area.Members) if (member.Source == null) member.Source = area.Source;
            }
            Rebuild(); RefreshAll();
        }

        private void Rebuild()
        {
            byId.Clear(); encounters.Clear(); byPawn.Clear(); byMap.Clear(); byOwner.Clear(); shells.Clear(); spatial.Clear();
            ResetGeometry();
            foreach (DomainArea area in Areas)
            {
                if (area.Closed) continue;
                byId.Add(area.Id, area);
                ObserveGeometry(area);
                if (!encounters.TryGetValue(area.EncounterId, out Encounter group)) encounters.Add(area.EncounterId, group = new Encounter { Map = area.Map });
                group.Areas.Add(area);
                if (!area.Ending && area.Owner != null) byOwner[area.Owner] = area;
                IndexShell(area);
                if (!area.Enclosed && area.Map != null)
                {
                    if (!spatial.TryGetValue(area.Map, out List<DomainArea> list)) spatial.Add(area.Map, list = new List<DomainArea>());
                    list.Add(area);
                }
                foreach (DomainMember member in area.Members) if (!member.Returned && member.Pawn != null) group.Members.Add(member.Pawn);
            }
            foreach (Encounter group in encounters.Values)
            {
                foreach (DomainArea area in group.Areas)
                {
                    area.MemberSet.Clear(); area.MemberSet.UnionWith(group.Members);
                    if (area.Enclosed && area.Map != null) byMap[area.Map] = group;
                    if (area.Enclosed && area.HomeMap != null) byMap[area.HomeMap] = group;
                }
                if (group.Areas.Any(a => a.Enclosed)) foreach (Pawn pawn in group.Members) byPawn[pawn] = group;
            }
            pendingClosure = byId.Values.Any(a => a.Ending);
            RebuildNestedSources();
            RebuildConfinement();
            Barriers.Rebuild(external);
        }

        public DomainArea Open(DomainRequest request) => Open(request, new DomainArea());
        public DomainArea Open(DomainRequest request, DomainArea area)
        {
            LibraryCapabilities.Require(LibraryCapability.Domains);
            Pawn owner = request?.Owner;
            DomainProfileDef profile = request?.Profile ?? (request == null ? null : DomainProfileDef.For(request.Kind));
            if (request != null && profile?.HasShell == true)
            {
                request.ParentDef = request.ParentDef ?? DefDatabase<WorldObjectDef>.GetNamed("Lihuat_DomainParent");
                request.GeneratorDef = request.GeneratorDef ?? DefDatabase<MapGeneratorDef>.GetNamed("Lihuat_DomainGenerator");
                request.ShellDef = request.ShellDef ?? DefDatabase<ThingDef>.GetNamedSilentFail(request.Size == 25 ? "Lihuat_DomainShell" : "Lihuat_DomainShell_" + request.Size + "Cells");
                if (request.ShellDef == null || request.ShellDef.size.x != request.Size || request.ShellDef.size.z != request.Size) return null;
            }
            if (rollback != null || owner?.Spawned != true || PawnLife.Dead(owner) || string.IsNullOrWhiteSpace(request.ProviderId)
                || request.Size < (profile?.Movable == true ? 3 : 5) || request.Size > 255 || profile?.Movable != true && request.Size % 2 == 0
                || request.CaptureRadius < 0f || float.IsNaN(request.CaptureRadius) || float.IsInfinity(request.CaptureRadius)
                || request.Stability.HasValue && (!(request.Stability.Value > 0f) || float.IsInfinity(request.Stability.Value))
                || request.ShellCapacity < 0
                || request.ExternalBarrierRadius < 0f || float.IsNaN(request.ExternalBarrierRadius) || float.IsInfinity(request.ExternalBarrierRadius)
                || request.ExternalBarrierRadius > 0f && (profile?.HasShell == true || profile?.Movable == true || request.ExternalBarrierRadius * 2f > request.Size)
                || OwnedBy(owner) != null || profile == null) return null;
            if (profile.HasShell && (request.ParentDef?.worldObjectClass == null || !typeof(PocketMapParent).IsAssignableFrom(request.ParentDef.worldObjectClass)
                || request.GeneratorDef == null)) return null;
            byMap.TryGetValue(owner.Map, out Encounter existing);
            if (existing?.Areas.Any(a => a.Ending) == true) return null;
            if (request.ExternalBarrierRadius > 0f && existing != null) return null;
            Map entryMap = owner.Map;
            Map shellMap = existing == null ? entryMap : existing.Areas[0].Source;
            if (profile.HasShell && (shellMap == null || shellMap.Size.x < request.Size || shellMap.Size.z < request.Size)) return null;
            PlanetTile generationTile = PlanetTile.Invalid;
            if (profile.HasShell && !DomainLocation.TryResolve(entryMap, out generationTile)) return null;
            IntVec3 entryAnchor = owner.Position;
            area.Owner = owner; area.ProviderId = request.ProviderId; area.Pressure = request.Pressure;
            area.Profile = profile; area.Size = request.Size; area.Stability = request.Stability ?? profile.stability; area.Center = owner.Position;
            area.ShellCapacity = request.ShellCapacity;
            area.ExternalBarrierRadius = request.ExternalBarrierRadius;
            area.AffectsOwner = request.AffectsOwner; area.TerminalDeaths = request.TerminalDeaths;
            area.CollapseOnOwnerDeath = request.CollapseOnOwnerDeath;
            area.Source = existing == null ? entryMap : existing.Areas[0].Source;
            area.ReturnPath = existing?.Areas[0].ReturnPath ?? DomainReturnPath.Capture(area.Source);
            area.Anchor = existing == null ? entryAnchor : DomainCoordinates.OnSource(existing.Areas, entryMap, entryMap, entryAnchor);
            area.EncounterId = existing == null ? area.Id : existing.Areas[0].EncounterId;
            area.CreatedAt = Find.TickManager.TicksGame;
            area.EntryCommitted = false;
            if (!area.Enclosed) return OpenSurface(area, existing, entryMap);
            CellRect captureCells = CellRect.CenteredOn(entryAnchor, (int)Math.Ceiling(request.CaptureRadius) + 1).ClipInsideMap(entryMap);
            MapCellRegistration.Reconcile(entryMap, captureCells);
            HashSet<Pawn> captured = DomainTransit.CaptureArea(entryMap, entryAnchor, request.CaptureRadius);
            var excluded = new List<Pawn>();
            if (request.CaptureFilter != null)
                captured.RemoveWhere(pawn =>
                {
                    if (pawn == owner || request.CaptureFilter(pawn)) return false;
                    excluded.Add(pawn);
                    return true;
                });
            captured.Add(owner);
            foreach (Pawn pawn in captured.OrderBy(p => p == owner ? 0 : 1).ThenBy(p => p.thingIDNumber))
            {
                if (existing?.Members.Contains(pawn) == true) continue;
                DomainMember member = DomainTransit.Capture(pawn);
                if (existing != null)
                {
                    member.Position = DomainCoordinates.OnSource(existing.Areas, entryMap, member.Source, member.Position);
                    member.Source = area.Source; member.Container = null; member.ContainerPath = null;
                }
                area.Members.Add(member);
            }
            var transaction = new DomainRollback { Area = area };
            DomainCoordinates.Placement[] placements = existing?.Areas.Select(a => new DomainCoordinates.Placement(a)).ToArray();
            bool published = false;
            try
            {
                area.Parent = (PocketMapParent)WorldObjectMaker.MakeWorldObject(request.ParentDef);
                area.Parent.sourceMap = area.Source; area.Parent.Tile = generationTile; area.Parent.mapGenerator = request.GeneratorDef;
                area.HomeMap = MapGenerator.GenerateMap(new IntVec3(request.Size, 1, request.Size), area.Parent, request.GeneratorDef,
                    extraInitBeforeContentGen: map => area.HomeMap = map, isPocketMap: true);
                Find.World.pocketMaps.Add(area.Parent);
                Map host = existing != null && existing.Map.Size.x >= request.Size ? existing.Map : area.HomeMap;
                IntVec3 translation = host == entryMap ? IntVec3.Zero : host.Center - entryAnchor;
                List<Thing> things = existing == null ? captured.Cast<Thing>().ToList() : DomainTransit.Contents(existing.Areas);
                foreach (Pawn pawn in captured) if (!things.Any(thing => ReferenceEquals(thing, pawn))) things.Add(pawn);
                using (new OperationScope(OperationKind.Transfer))
                {
                    DomainEntityTransit.Enter(area, existing?.Areas, entryMap, entryAnchor, request.CaptureRadius, host, translation, transaction, request.EntityCaptureFilter);
                    foreach (Pawn pawn in excluded)
                    {
                        if (existing?.Members.Contains(pawn) == true || !DomainTransit.HeldBy(pawn, captured)) continue;
                        DomainMove saved = DomainTransit.Snapshot(pawn);
                        transaction.Positions.Add(saved);
                        TransferTo(pawn, saved.Map, saved.Position);
                    }
                    foreach (Thing thing in things)
                    {
                        if (thing.MapHeld == host) continue;
                        transaction.Positions.Add(DomainTransit.Snapshot(thing));
                        IntVec3 destination = existing == null ? DomainTransit.EntryPosition(owner, thing, host, entryAnchor)
                            : DomainCoordinates.InArena(existing.Areas, entryMap, thing.MapHeld, thing.PositionHeld) + translation;
                        TransferTo(thing, host, destination);
                        if (thing != owner) (thing as Pawn)?.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    }
                }
                area.Map = host;
                area.CenterOffset = entryAnchor + translation - host.Center;
                foreach (DomainMember member in area.Members) member.Transferred = true;
                if (existing != null) foreach (DomainArea previous in existing.Areas) DomainCoordinates.Move(previous, host, translation);
                external.Add(area);
                published = true;
                Rebuild(); RefreshAll();
                if (!DomainShellRules.Create(area, area.Source, area.Anchor, request.ShellDef ?? DefDatabase<ThingDef>.GetNamed("Lihuat_DomainShell")))
                    throw new InvalidOperationException("Domain shell creation failed.");
                foreach (Pawn pawn in excluded)
                {
                    if (!pawn.Spawned || pawn.Map != area.Source || !DomainShellRules.Blocks(pawn.Map, pawn.Position)) continue;
                    transaction.Positions.Add(DomainTransit.Snapshot(pawn));
                    DomainShellRules.Repel(pawn);
                }
                Opened?.Invoke(area);
                foreach (Pawn pawn in captured) Captured?.Invoke(pawn);
                if (area.Ending || area.Closed) throw new InvalidOperationException("Domain opening was cancelled before completion.");
                MapCellRegistration.Reconcile(entryMap, captureCells);
                Current.Game.CurrentMap = host;
                host.mapDrawer.RegenerateEverythingNow();
                Find.CameraDriver.SetRootPosAndSize(owner.Position.ToVector3Shifted(), host.Size.x / 2f + .5f);
                area.EntryCommitted = true;
                return area;
            }
            catch (Exception ex)
            {
                Log.Error("[LihuatLib.Domain] Expansion rollback: " + ex);
                if (published)
                {
                    external.Remove(area);
                    if (placements != null) foreach (DomainCoordinates.Placement placement in placements) placement.Restore();
                    Rebuild();
                }
                area.EndReason = "entry-failed";
                area.Ending = true;
                DomainShellRules.Remove(area);
                NotifyClosed(area);
                rollback = transaction;
                RestoreEntry();
                return null;
            }
        }

        public bool Collapse(string id, string reason)
        {
            DomainArea area = GetArea(id);
            if (area == null || area.Ending) return false;
            area.Ending = true; area.EndReason = reason;
            pendingClosure = true;
            nextFinishAt = 0f;
            RefreshAll();
            return true;
        }

        public void Tick(int now)
        {
            if (now % 60 != 0 || !HasAreas && rollback == null) return;
            if (rollback != null) RestoreEntry();
            ReconcileConfinement();
            foreach (DomainArea area in Areas.ToArray())
            {
                if (!area.Ending && area.CollapseOnOwnerDeath && (area.Owner == null || PawnLife.Dead(area.Owner) || area.Owner.Destroyed)) Collapse(area.Id, "owner-dead");
                if (!area.Ending && area.Profile.Movable) MoveMobile(area);
                DomainShellRules.Tick(area, now);
                ObserveGeometry(area);
            }
            Update();
        }

        public void Update()
        {
            if (resolvingOverlaps) return;
            ResolvePendingOverlaps();
            if (pendingClosure && UnityEngine.Time.realtimeSinceStartup >= nextFinishAt) Flush();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref external, "foreignDomains", LookMode.Deep);
            if (external == null) external = new List<DomainArea>();
            Scribe_Deep.Look(ref rollback, "domainRollback");
        }
    }
}
