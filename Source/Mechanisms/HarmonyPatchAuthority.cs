using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class HarmonyPatchAuthority
    {
        private sealed class Request
        {
            internal readonly Request Previous;
            internal readonly Assembly Issuer;
            internal Request(Assembly issuer) { Previous = current; Issuer = issuer; }
        }

        [ThreadStatic] private static Request current;
        internal static readonly MethodInfo ProcessorEntry = AccessTools.Method(typeof(PatchProcessor), nameof(PatchProcessor.Patch));
        internal static readonly MethodInfo ClassEntry = AccessTools.Method(typeof(PatchClassProcessor), nameof(PatchClassProcessor.Patch));

        internal static void Install()
        {
            MethodInfo end = AccessTools.Method(typeof(HarmonyPatchAuthority), nameof(End));
            NativeIlGuards.Scoped(ProcessorEntry, AccessTools.Method(typeof(HarmonyPatchAuthority), nameof(BeginProcessor)),
                Array.Empty<int>(), end, Array.Empty<int>());
            NativeIlGuards.Scoped(ClassEntry, AccessTools.Method(typeof(HarmonyPatchAuthority), nameof(BeginClass)),
                Array.Empty<int>(), end, Array.Empty<int>());
        }

        internal static bool Owns(MethodInfo method) => method?.DeclaringType != null
            && !method.Module.Assembly.IsDynamic && GeneratedIntegrity.Owns(method);

        internal static bool Allows(MethodBase original, MethodInfo patch)
        {
            Assembly issuer = current?.Issuer;
            if (issuer == null || !Owns(patch) || patch.Module.Assembly != issuer) return false;
            return !GeneratedIntegrity.Owns(original) || original.Module.Assembly == issuer
                || issuer == typeof(HarmonyPatchAuthority).Assembly;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Assembly FindIssuer()
        {
            bool publication = false;
            foreach (StackFrame frame in new StackTrace(false).GetFrames())
            {
                MethodBase method = frame.GetMethod();
                if (method == null) continue;
                Assembly owner = method.Module.Assembly;
                if (owner == typeof(Harmony).Assembly) { publication = true; continue; }
                if (!publication || owner == typeof(object).Assembly) continue;
                return method.DeclaringType != null && !owner.IsDynamic && InfrastructureAssemblies.Contains(owner) ? owner : null;
            }
            return null;
        }

        private static bool BeginProcessor(out Request state, out MethodInfo result)
        {
            state = new Request(FindIssuer());
            current = state;
            result = null;
            return true;
        }

        private static bool BeginClass(out Request state, out List<MethodInfo> result)
        {
            state = new Request(FindIssuer());
            current = state;
            result = null;
            return true;
        }

        private static void End(Request state)
        {
            if (state != null) current = state.Previous;
        }
    }
}
