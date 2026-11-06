using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class PatchProvenance
    {
        private static readonly Dictionary<MethodBase, MethodBase> replacements = new();
        internal static void Record(MethodInfo replacement, MethodBase original)
        {
            if (replacement != null && original != null && !Equals(replacement, original)) replacements[replacement] = original;
        }
        internal static bool IsReplacement(MethodInfo method) => replacements.ContainsKey(method) || !Equals(Harmony.GetOriginalMethod(method), method);
        internal static bool ReplacesInfrastructure(MethodBase method)
        {
            if (method == null) return false;
            if (!replacements.TryGetValue(method, out MethodBase original)) return false;
            return original != null && !Equals(original, method) && Core.InfrastructureAssemblies.Contains(original.Module.Assembly);
        }
    }
}
