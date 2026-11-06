using RimWorld;
using RimWorld.Planet;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Domains
{
    public sealed class DomainRequest
    {
        public string ProviderId;
        public Pawn Owner;
        public int Size;
        public float CaptureRadius;
        public WorldObjectDef ParentDef;
        public MapGeneratorDef GeneratorDef;
        public DomainPressure Pressure = DomainPressure.All;
        public bool AffectsOwner;
        public bool CollapseOnOwnerDeath = true;
        public bool TerminalDeaths;
        public DomainKind Kind = DomainKind.Closed;
        public float ExternalBarrierRadius;
        public DomainProfileDef Profile;
        public ThingDef ShellDef;
        public int ShellCapacity;
        public float? Stability;
        public System.Func<Pawn, bool> CaptureFilter;
        public System.Func<DomainEntity, bool> EntityCaptureFilter;
    }

    public static class DomainAPI
    {
        public static DomainArea Open(DomainRequest request) => LihuatGame.Current?.Domains.Open(request);
        public static DomainArea Find(string id) => LihuatGame.Current?.Domains.GetArea(id);
        public static DomainArea OwnedBy(Pawn pawn) => LihuatGame.Current?.Domains.OwnedBy(pawn);
        public static bool Collapse(string id, string reason = "broken") => LihuatGame.Current?.Domains.Collapse(id, reason) == true;
        public static bool DamageStability(string id, float amount) => LihuatGame.Current?.Domains.DamageStability(id, amount) == true;
        public static bool ShouldSuppress(string id, Pawn pawn) => LihuatGame.Current?.Domains.ShouldSuppress(id, pawn) == true;
        public static bool IsImmune(Pawn pawn) => LihuatGame.Current?.Domains.IsImmune(pawn) == true;
        public static int PressureCount(Pawn pawn) => LihuatGame.Current?.Domains.PressureCount(pawn) ?? 0;
        public static CellRect Footprint(string id) => Find(id)?.Footprint ?? CellRect.Empty;
        public static Map Arena(string id) => Find(id)?.Map;
        public static System.Collections.Generic.IEnumerable<DomainArea> Overlapping(string id) => LihuatGame.Current?.Domains.Overlapping(Find(id)) ?? System.Linq.Enumerable.Empty<DomainArea>();
        public static void NotifyMoved(Pawn pawn) => LihuatGame.Current?.Domains.Moved(pawn);
    }
}
