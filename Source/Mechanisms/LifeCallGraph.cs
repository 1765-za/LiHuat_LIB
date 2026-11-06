using System;
using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    [Flags]
    public enum LifeCallKind { Direct = 1, Virtual = 2, Delegate = 4 }

    public readonly record struct LifeCallEdge(MethodInfo Caller, MethodInfo Target, LifeCallKind Kind);
    public readonly record struct LifeCallAnalysis(MemberInfo Root, string Kind);
    public readonly record struct LifeCallGraphCoverage(string Assembly, int Methods, int Edges, bool Limited, int IndirectCalls);

    public static class LifeCallGraph
    {
        private static LifeCallGraphRepository Repository => LifeAnalysisCoordinator.Shared?.CallGraph;
        public static int Assemblies => Repository?.AssemblyCount ?? 0;
        public static int Methods => Repository?.MethodCount ?? 0;
        public static int Edges => Repository?.EdgeCount ?? 0;
        public static int LimitedAssemblies => Repository?.LimitedCount ?? 0;
        public static int IndirectCalls => Repository?.IndirectCount ?? 0;
        public static int DemandBuilds => Repository?.DemandBuilds ?? 0;
        public static LifeCallGraphCoverage[] Coverage => Repository?.Coverage() ?? Array.Empty<LifeCallGraphCoverage>();
        public static LifeCallEdge[] Callers(MethodInfo method) => Repository?.Callers(method) ?? Array.Empty<LifeCallEdge>();
        public static LifeCallEdge[] Callees(MethodInfo method) => Repository?.Callees(method) ?? Array.Empty<LifeCallEdge>();
        public static LifeCallAnalysis[] Analyses(MethodInfo method) => LifeAnalysisCoordinator.Shared?.CallAnalyses(method) ?? Array.Empty<LifeCallAnalysis>();
        public static MethodInfo[] FindPath(MethodInfo source, MethodInfo target, int maximumDepth = 16)
            => Repository?.FindPath(source, target, Math.Clamp(maximumDepth, 0, 32)) ?? Array.Empty<MethodInfo>();
    }

}
