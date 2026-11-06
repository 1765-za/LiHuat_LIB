using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeCallGraphPreparation
    {
        internal static bool Enabled => LibraryCapabilities.NeedsAnalysis && !StarBlood.StarBloodScanPolicy.CacheOnly;
        internal static IEnumerable<Assembly> Sources(IEnumerable<Assembly> assemblies) => assemblies.Where(a => !a.IsDynamic
            && !LifePlatformAssemblies.Contains(a) && !InfrastructureAssemblies.Contains(a)).Distinct();

        internal static IEnumerable Prepare(PreparationProgress progress, LifeLoadingAnalysis loading)
        {
            progress.Next("Lihuat_PreparationLifeCallGraph");
            var watch = Stopwatch.StartNew();
            Assembly[] sources = Sources(LoadedModManager.RunningModsListForReading.SelectMany(m => m.assemblies.loadedAssemblies)).ToArray();
            LifeCallGraphRepository repository = LifeAnalysisCoordinator.Shared.CallGraph;
            progress.Report(0, sources.Length);
            while (loading?.CallGraphsRunning == true) yield return null;
            int completed = 0;
            foreach (Assembly assembly in sources)
            {
                if (!repository.Analyzed(assembly))
                {
                    var builder = new LifeCallGraphBuilder(assembly);
                    foreach (object step in builder.Build(() => false)) yield return step;
                    repository.Publish(builder.Result);
                }
                progress.Report(++completed, sources.Length);
                yield return null;
            }
            Log.Message("[LihuatLib.Analysis] Life call graph ready: assemblies=" + repository.AssemblyCount
                + " methods=" + repository.MethodCount + " edges=" + repository.EdgeCount + " limited=" + repository.LimitedCount
                + " indirect=" + repository.IndirectCount + " earlyMs=" + (loading?.CallGraphMilliseconds ?? 0).ToString("F1")
                + " stageMs=" + watch.Elapsed.TotalMilliseconds.ToString("F1")
                + " methodLimit=" + LifeCallGraphRepository.MethodLimit + " edgeLimit=" + LifeCallGraphRepository.EdgeLimit
                + " budgetLimited=" + repository.BudgetLimitedCount + " scanner=" + LibraryMod.ActiveScanner);
        }
    }
}
