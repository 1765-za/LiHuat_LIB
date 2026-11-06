using System.Collections;
using LihuatLib.Mechanisms;

namespace LihuatLib.Core
{
    internal static class LibraryPreparation
    {
        internal static bool Required => LibraryCapabilities.NeedsAnalysis
            || LibraryCapabilities.Has(LibraryCapability.HediffProtection);
        internal static PreparationProgress Progress { get; } = new PreparationProgress();

        internal static IEnumerable Prepare()
        {
            using var diagnostics = LifeDiscoveryMetadata.Batch();
            Progress.Begin((LibraryCapabilities.Has(LibraryCapability.HediffProtection) ? 1 : 0)
                + (LibraryCapabilities.Has(LibraryCapability.StarBlood) ? 3 : 0)
                + (LibraryCapabilities.Has(LibraryCapability.Regeneration) ? 1 : 0)
                + (LifeCallGraphPreparation.Enabled ? 1 : 0)
                + (LibraryCapabilities.NeedsAnalysis ? 1 : 0));
            try
            {
                if (LibraryCapabilities.Has(LibraryCapability.HediffProtection))
                {
                    Progress.Next("Lihuat_PreparationHealth");
                    HediffMutationGuard.Discover();
                    int completed = 0;
                    Progress.Report(0, HediffMutationGuard.Pending);
                    while (HediffMutationGuard.Pending > 0)
                    {
                        int pending = HediffMutationGuard.Pending;
                        HediffMutationGuard.Update();
                        completed += System.Math.Max(0, pending - HediffMutationGuard.Pending);
                        Progress.Report(completed, completed + HediffMutationGuard.Pending);
                        yield return null;
                    }
                }
                if (LibraryCapabilities.NeedsAnalysis)
                    foreach (object step in StarBlood.LifeAnalysisPreparation.Prepare(Progress)) yield return step;
                HediffMutationGuard.DiscoverCallers();
                while (HediffMutationGuard.Pending > 0) { HediffMutationGuard.Update(); yield return null; }
                Progress.Finish();
            }
            finally { Progress.Close(); }
        }
    }
}
