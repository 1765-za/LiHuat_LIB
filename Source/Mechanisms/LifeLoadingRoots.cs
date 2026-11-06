using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeLoadingRoots
    {
        private const BindingFlags Declared = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static IEnumerable<LifeAnalysisKey> Discover(IEnumerable<Assembly> assemblies, Func<bool> stopped)
        {
            var seen = new HashSet<LifeAnalysisKey>();
            foreach (Assembly assembly in assemblies)
            {
                if (stopped()) yield break;
                if (assembly.IsDynamic || assembly == typeof(Pawn).Assembly || InfrastructureAssemblies.Contains(assembly)) continue;
                foreach (Type type in LifeDiscoveryMetadata.Types(assembly))
                {
                    if (stopped()) yield break;
                    if (type == null || type.ContainsGenericParameters || type.IsValueType || !LifeResourceEvidence.IsCustom(type)) continue;
                    if (!type.IsAbstract && (typeof(ThingComp).IsAssignableFrom(type) || typeof(Hediff).IsAssignableFrom(type)
                        || typeof(HediffComp).IsAssignableFrom(type)))
                    {
                        var key = new LifeAnalysisKey(LifeAnalysisKind.Type, type);
                        if (seen.Add(key)) yield return key;
                    }
                    bool component = typeof(GameComponent).IsAssignableFrom(type) || typeof(MapComponent).IsAssignableFrom(type)
                        || typeof(RimWorld.Planet.WorldComponent).IsAssignableFrom(type);
                    foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type, Declared))
                    {
                        if (stopped()) yield break;
                        if (!LifeIlReader.HasMetadataBody(method)) continue;
                        LifeAnalysisKind kind;
                        if (component && method.IsVirtual && (method.Name.EndsWith("ComponentTick", StringComparison.Ordinal)
                            || method.Name.EndsWith("ComponentUpdate", StringComparison.Ordinal))) kind = LifeAnalysisKind.Counter;
                        else if (LifeRetirementRecipient.Candidate(method)) kind = LifeAnalysisKind.Retirement;
                        else continue;
                        var key = new LifeAnalysisKey(kind, method);
                        if (seen.Add(key)) yield return key;
                    }
                }
            }
        }

    }
}
