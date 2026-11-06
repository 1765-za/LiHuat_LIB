using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.IO;
using LihuatLib.Mechanisms;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.StarBlood
{
    public static class LifeAnalysisPreparation
    {
        private static readonly HashSet<ThingDef> races = new HashSet<ThingDef>();
        private static bool ready;
        private static LifeLoadingAnalysis loading;
        public static bool Ready => !LibraryCapabilities.NeedsAnalysis || ready;
        public static double ElapsedMilliseconds { get; private set; }

        internal static void BeginLoading()
        {
            if (ready || loading != null || StarBloodScanPolicy.StartupCacheOnly
                || !LibraryCapabilities.Has(LibraryCapability.StarBlood)) return;
            ConsumerAssemblyRegistration.Initialize(LoadedModManager.RunningModsListForReading);
            loading = new LifeLoadingAnalysis(Path.Combine(GenFilePaths.ConfigFolderPath, "LihuatLib", "StarBloodAnalysis"),
                LifeCacheOwner.FromMods(LoadedModManager.RunningModsListForReading).ToArray(),
                LoadedModManager.RunningModsListForReading.SelectMany(mod => mod.assemblies.loadedAssemblies).Distinct().ToArray());
            loading.Start();
            Log.Message("[LihuatLib.Analysis] Loading analysis started before XML loading.");
        }

        public static void RegisterRace(ThingDef race)
        {
            LibraryCapabilities.Require(LibraryCapability.StarBlood);
            if (race != null) races.Add(race);
        }

        internal static IEnumerable Prepare(PreparationProgress progress)
        {
            if (!LibraryCapabilities.NeedsAnalysis) yield break;
            var watch = Stopwatch.StartNew();
            loading?.Stop();
            LifeAnalysisCoordinator.Initialize(Path.Combine(GenFilePaths.ConfigFolderPath, "LihuatLib", "StarBloodAnalysis"),
                LifeCacheOwner.FromMods(LoadedModManager.RunningModsListForReading), loading);
            if (LifeCallGraphPreparation.Enabled)
                foreach (object step in LifeCallGraphPreparation.Prepare(progress, loading)) yield return step;
            if (LibraryCapabilities.Has(LibraryCapability.StarBlood))
                foreach (object step in PrepareStarBlood(progress)) yield return step;
            if (LibraryCapabilities.Has(LibraryCapability.Regeneration))
            {
                progress.Next("Lihuat_PreparationRegeneration");
                RegenerationGuard.Discover();
                int total = RegenerationGuard.Pending;
                progress.Report(0, total);
                while (RegenerationGuard.Pending > 0)
                {
                    RegenerationGuard.AnalyzeNext(); LifeAnalysisCoordinator.Shared.Pump(true);
                    progress.Report(total - RegenerationGuard.Pending, total); yield return null;
                }
            }
            progress.Next("Lihuat_PreparationPublish");
            int published = LifeAnalysisCoordinator.Shared.Published + LifeAnalysisDiagnostics.CacheWrites;
            progress.Report(0, LifeAnalysisCoordinator.Shared.Pending);
            while (LifeAnalysisCoordinator.Shared.Pending > 0)
            {
                LifeAnalysisCoordinator.Shared.Pump(true);
                int completed = LifeAnalysisCoordinator.Shared.Published + LifeAnalysisDiagnostics.CacheWrites - published;
                progress.Report(completed, completed + LifeAnalysisCoordinator.Shared.Pending); yield return null;
            }
            LifeAnalysisCoordinator.Shared.ReleaseSnapshots();
            if (loading != null)
            {
                loading.Release();
                Log.Message("[LihuatLib.Analysis] Loading overlap: " + loading.Summary);
                loading = null;
            }
            LifeAnalysisCoordinator.Shared.FinishLoading();
            LifeAnalysisDiskCache cache = LifeAnalysisCoordinator.Shared.Cache;
            var maintenance = cache.MaintainAsync();
            while (!maintenance.IsCompleted) yield return null;
            maintenance.GetAwaiter().GetResult();
            watch.Stop(); ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds; ready = true;
            Log.Message("[LihuatLib.Analysis] Startup analysis complete: capabilities=" + LibraryCapabilities.Enabled
                + " types=" + LifeResourceEvidence.PreparedTypes
                + " counters=" + LifeResourceEvidence.PreparedCounters + " retirement=" + LifeRetirementGuard.Installed
                + " availability=" + LifeAvailabilityGuard.Installed
                + " regeneration=" + RegenerationGuard.Installed
                + " preparation=" + RegenerationSupplyGuard.Installed
                + " milliseconds=" + ElapsedMilliseconds.ToString("F1") + " analyzed=" + LifeAnalysisDiagnostics.Analyzed
                + " cacheHits=" + LifeAnalysisDiagnostics.CacheHits + " cacheInvalid=" + LifeAnalysisDiagnostics.CacheInvalid
                + " cacheOnly=" + StarBloodScanPolicy.CacheOnly + " skipped=" + LifeAnalysisDiagnostics.CacheOnlySkipped
                + " workers=" + LifeAnalysisDiagnostics.PeakWorkers
                + " metadataUnavailable=" + LifeDiscoveryMetadata.Unavailable
                + " snapshotHits=" + LifeAnalysisDiagnostics.SnapshotHits + " retainedSnapshots=" + LifeAnalysisDiagnostics.SnapshotCount
                + " cacheWrites=" + LifeAnalysisDiagnostics.CacheWrites + " skippedWrites=" + LifeAnalysisDiagnostics.SkippedWrites
                + " cacheFailures=" + LifeAnalysisDiagnostics.CacheFailures
                + " writeBatches=" + LifeAnalysisDiagnostics.WriteBatches + " maxWriteBatch=" + LifeAnalysisDiagnostics.MaximumWriteBatch
                + " removedCacheFiles=" + cache.RemovedFiles + " removedCacheBytes=" + cache.RemovedBytes
                + " cleanupFailures=" + cache.CleanupFailures);
        }

        private static IEnumerable PrepareStarBlood(PreparationProgress progress)
        {
            progress.Next("Lihuat_PreparationTypes");
            var types = new HashSet<Type>();
            foreach (ThingDef race in races)
            {
                AddType(race.thingClass, types);
                if (race.comps != null) foreach (CompProperties comp in race.comps) AddType(comp.compClass, types);
            }
            foreach (Type type in typeof(ThingComp).AllSubclassesNonAbstract())
                AddType(type, types);
            foreach (Type type in typeof(Hediff).AllSubclassesNonAbstract()) AddType(type, types);
            foreach (Type type in typeof(HediffComp).AllSubclassesNonAbstract()) AddType(type, types);
            int completed = 0;
            progress.Report(0, types.Count);
            foreach (Type type in types)
            {
                LifeResourceEvidence.PrepareType(type);
                LifeAnalysisCoordinator.Shared.Pump(true);
                progress.Report(++completed, types.Count);
                yield return null;
            }
            var counters = new LifeCounterWatch(new LifeResourceEvidence(), () => 0);
            progress.Next("Lihuat_PreparationCounters");
            counters.DiscoverLoadedTypes();
            int total = counters.Pending;
            progress.Report(0, total);
            while (counters.Pending > 0)
            {
                counters.AnalyzeNext(); LifeAnalysisCoordinator.Shared.Pump(true);
                progress.Report(total - counters.Pending, total); yield return null;
            }
            progress.Next("Lihuat_PreparationLifecycle");
            LifeRetirementGuard.Discover();
            LifeAvailabilityGuard.Discover();
            total = LifeRetirementGuard.Pending;
            progress.Report(0, total);
            while (LifeRetirementGuard.Pending > 0)
            {
                LifeRetirementGuard.AnalyzeNext(); LifeAnalysisCoordinator.Shared.Pump(true);
                progress.Report(total - LifeRetirementGuard.Pending, total); yield return null;
            }
        }

        private static void AddType(Type type, HashSet<Type> types)
        {
            if (type?.IsArray == true)
            {
                AddType(type.GetElementType(), types);
                return;
            }
            if (type == null || type.ContainsGenericParameters || !LifeResourceEvidence.IsCustom(type)
                || !types.Add(type)) return;
            for (Type cursor = type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
                foreach (FieldInfo field in LifeDiscoveryMetadata.Fields(cursor, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    Type child = field.FieldType;
                    if (child.IsValueType || typeof(Thing).IsAssignableFrom(child) || typeof(Def).IsAssignableFrom(child)) continue;
                    if (typeof(IList).IsAssignableFrom(child) && child.IsGenericType)
                        foreach (Type item in child.GetGenericArguments()) AddType(item, types);
                    else AddType(child, types);
                }
        }
    }
}
