using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using LihuatLib.Mechanisms;

namespace LihuatLib.Core
{
    public static class InfrastructureAssemblies
    {
        private static readonly HashSet<Assembly> assemblies = new HashSet<Assembly> { typeof(InfrastructureAssemblies).Assembly };
        private static string policyKey = ComputePolicyKey();
        internal static string PolicyKey { get { lock (assemblies) return policyKey; } }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(Assembly assembly)
        {
            if (assembly == null) throw new System.ArgumentNullException(nameof(assembly));
            MethodBase caller = new StackFrame(1, false).GetMethod();
            if (caller is MethodInfo replacement) caller = HarmonyLib.Harmony.GetOriginalMethod(replacement);
            Assembly callerAssembly = caller?.DeclaringType?.Assembly;
            lock (assemblies)
            {
                if (assemblies.Contains(assembly)) return;
                bool trusted = caller is MethodInfo method && CodePublicationAuthority.Callback(method)
                    || caller is ConstructorInfo && callerAssembly != null && assemblies.Contains(callerAssembly)
                    || caller != null && PatchProvenance.ReplacesInfrastructure(caller);
                if (!trusted && (LibraryCapabilities.Frozen || callerAssembly != assembly || assembly.IsDynamic))
                    throw new System.InvalidOperationException("Infrastructure registration requires a startup consumer declaration.");
                assemblies.Add(assembly);
                policyKey = ComputePolicyKey();
            }
        }
        public static bool Contains(Assembly assembly) { lock (assemblies) return assemblies.Contains(assembly); }

        private static string ComputePolicyKey()
        {
            // Trust membership affects discovery; referenced module content is validated by each analysis artifact.
            string value = string.Join("\n", assemblies.Select(assembly => assembly.FullName).OrderBy(identity => identity, System.StringComparer.Ordinal));
            using (var hash = SHA256.Create())
                return System.BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
        }
    }
}
