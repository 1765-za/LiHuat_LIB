using Verse;
using LihuatLib.Core;
using LihuatLib.Mechanisms;

namespace LihuatLib.StarBlood
{
    internal sealed class LifeInjectionWatch
    {
        private Pawn pawn;
        private LifeStructureSnapshot baseline;
        private LifeStructureSnapshot spare;
        private int nextDeepCheck;
        private bool spareNeedsRefresh;
        private readonly LifeResourceEvidence evidence;
        public LifeInjectionWatch(LifeResourceEvidence evidence) { this.evidence = evidence; }

        public void Initialize(Pawn target)
        {
            pawn = target;
            spare = null;
            spareNeedsRefresh = false;
            baseline = target == null ? null : new LifeStructureSnapshot(target, evidence, captureValues: false);
            baseline?.ExcludeUndeclaredComponents();
            nextDeepCheck = -1;
        }

        public int Inspect(StarBloodSubject record, bool force, System.Action<string> report, System.Action<Pawn, System.Type> detached)
        {
            if (!StarBloodRules.Enabled(record) || OperationScope.MedicalFor(record.Pawn) || OperationScope.Terminates(record.Pawn)) return 0;
            if (pawn != record.Pawn || baseline == null) Initialize(record.Pawn);
            int now = record.Now;
            if (!force && now < nextDeepCheck && baseline.RootsMatch()) return 0;
            nextDeepCheck = now + 60;
            if (baseline.StructureMatches())
            {
                baseline.RefreshBindings();
                if (spareNeedsRefresh) { spare.Refresh(); spareNeedsRefresh = false; }
                return 0;
            }
            LifeStructureSnapshot after = spare;
            if (after == null) after = new LifeStructureSnapshot(pawn, evidence, captureValues: false);
            else after.Refresh();
            int stripped = 0;
            if (after.HasNewStructures(baseline))
            {
                record.State.InjectionInspections++;
                stripped = after.StripIntroduced(baseline, report, false, true, detached);
                record.State.InjectedStructures += stripped;
            }
            spare = baseline;
            if (stripped == 0) baseline = after;
            else { baseline.Refresh(); spare = after; }
            if (stripped > 0) record.State.StrippedStructures += stripped - baseline.ExcludeRejected(after);
            spareNeedsRefresh = true;
            return stripped;
        }
    }
}
