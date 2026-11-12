using System;
using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class PatchProvenance
    {
        private static readonly ConcurrentDictionary<MethodBase, MethodBase> replacements = new();
        private static readonly Type metadataConstructor = typeof(object).GetConstructor(Type.EmptyTypes).GetType();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Record(MethodInfo replacement, MethodBase original)
        {
            CodePublicationAuthority.RequireCaller();
            if (replacement == null || original == null || Equals(replacement, original)) return;
            replacements[replacement] = original;
            if (DynamicWriteCatalogue.Ready && InfrastructureOriginal(original))
                DynamicIlCatalogue.AcceptInfrastructureBody(replacement);
        }
        internal static bool IsReplacement(MethodInfo method) => replacements.ContainsKey(method) || !Equals(Harmony.GetOriginalMethod(method), method);
        internal static bool ReplacesInfrastructure(MethodBase method) => InfrastructureSource(method) != null;
        internal static MethodBase InfrastructureSource(MethodBase method)
        {
            if (method == null || !replacements.TryGetValue(method, out MethodBase original)) return null;
            return original != null && !Equals(original, method) && InfrastructureOriginal(original)
                ? original : null;
        }
        private static bool InfrastructureOriginal(MethodBase original) => original is MethodInfo method
            ? CodePublicationAuthority.Metadata(method)
            : original is ConstructorInfo && original.GetType() == metadataConstructor && original.DeclaringType != null
                && !original.Module.Assembly.IsDynamic && Core.InfrastructureAssemblies.Contains(original.Module.Assembly);
    }
}
