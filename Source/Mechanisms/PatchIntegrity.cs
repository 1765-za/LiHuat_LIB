using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    public static class PatchIntegrity
    {
        private static readonly Dictionary<MethodBase, Patch[][]> anchors = new Dictionary<MethodBase, Patch[][]>();
        private static readonly Queue<string> events = new Queue<string>();
        private static readonly Type patchFunctions = typeof(Harmony).Assembly.GetType("HarmonyLib.PatchFunctions");
        private static readonly MethodInfo update = AccessTools.Method(patchFunctions, "UpdateWrapper");
        private static readonly MethodInfo reverse = AccessTools.Method(patchFunctions, "ReversePatch");
        private static readonly MethodInfo detour = AccessTools.Method(typeof(Harmony).Assembly.GetType("HarmonyLib.PatchTools"), "DetourMethod");
        private static readonly Action<MethodBase, MethodBase> commit =
            IndependentIl.Clone(detour).CreateDelegate<Action<MethodBase, MethodBase>>();
        private static readonly HashSet<MethodBase> integrityEntries = new HashSet<MethodBase>
        { update, reverse, detour, HarmonyPatchAuthority.ProcessorEntry, HarmonyPatchAuthority.ClassEntry };
        private static Func<MethodBase, PatchInfo, MethodInfo> rebuild;
        public static long Rejected { get; private set; }
        public static long Restored { get; private set; }
        public static long RejectedCopies { get; private set; }
        public static long RejectedDetours { get; private set; }
        public static IEnumerable<string> Events => events.ToArray();
        private static bool installed;
        internal static event Action<MethodBase> WrapperCommitted;
        internal static event Action<MethodBase, PatchInfo> WrapperPreparing;
        public static void Install()
        {
            if (installed) return;
            DynamicMethod registration = IntegrityEmission.Registration(update, detour,
                AccessTools.Method(typeof(PatchIntegrity), nameof(BeforeUpdate)),
                AccessTools.Method(typeof(PatchIntegrity), nameof(CommitRegistered)));
            DynamicMethod reverseCopy = IntegrityEmission.Reverse(reverse, detour,
                AccessTools.Method(typeof(PatchIntegrity), nameof(BeforeReverse)),
                AccessTools.Method(typeof(PatchIntegrity), nameof(CommitRegistered)));
            DynamicMethod direct = IndependentIl.Guard(detour, IndependentIl.Clone(detour),
                AccessTools.Method(typeof(PatchIntegrity), nameof(AllowDetour)), new[] { 0 });
            rebuild = (Func<MethodBase, PatchInfo, MethodInfo>)IndependentIl.Compile(registration);
            NativeIlGuards.InstallControls(new[] { update, reverse, detour }, new[] { registration, reverseCopy, direct });
            Integrations.HarmonySourceBody.Install();
            if (Configuration.HighMechanismPolicy.NativeProtection) HarmonyPatchAuthority.Install();
            installed = true;
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToArray())
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
                if (!GeneratedIntegrity.Owns(original) && !info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                    .Concat(info.Finalizers).Concat(info.InnerPrefixes).Concat(info.InnerPostfixes).Concat(info.InnerFinalizers).Any(Anchor)) continue;
                if (!GeneratedIntegrity.Owns(original) && !integrityEntries.Contains(original))
                    Capture(original, Groups(info), true);
                RefreshExisting(original);
            }
        }

        internal static void RefreshExisting(MethodBase original)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null) return;
            rebuild(original, new PatchInfo
            {
                prefixes = info.Prefixes.ToArray(), postfixes = info.Postfixes.ToArray(),
                transpilers = info.Transpilers.ToArray(), finalizers = info.Finalizers.ToArray(),
                innerprefixes = info.InnerPrefixes.ToArray(), innerpostfixes = info.InnerPostfixes.ToArray(), innerfinalizers = info.InnerFinalizers.ToArray()
            });
        }

        private static bool ProtectedDestination(MethodBase method) => method != null
            && (GeneratedIntegrity.Owns(method) || integrityEntries.Contains(method) || anchors.ContainsKey(method) || NativeIlGuards.Contains(method));

        private static bool BeforeReverse(HarmonyMethod standin, MethodBase original, ref MethodInfo __result)
        {
            if (!Configuration.HighMechanismPolicy.NativeProtection) return true;
            if (!ProtectedDestination(standin?.method)) return true;
            RejectedCopies++;
            __result = standin.method;
            return false;
        }

        private static bool AllowDetour(MethodBase method)
        {
            if (!Configuration.HighMechanismPolicy.NativeProtection) return true;
            if (!ProtectedDestination(method)) return true;
            RejectedDetours++;
            return false;
        }

        private static void CommitRegistered(MethodBase method, MethodBase replacement)
        {
            using (new ExecutableWriteProtection.Scope(true))
                if (!NativeIlGuards.Commit(method, replacement)) commit(method, replacement);
            if (Dispatching.NativeDispatcher.GenericControl(method) != null)
            { WrapperCommitted?.Invoke(method); return; }
            if (ProtectedDestination(method))
            {
                ExecutableWriteProtection.Track(RuntimeMethodAddress.Get(method));
                ExecutableWriteProtection.Track(RuntimeMethodAddress.Get(replacement));
            }
            WrapperCommitted?.Invoke(method);
        }

        private static bool BeforeUpdate(MethodBase original, PatchInfo patchInfo, ref MethodInfo result)
        {
            if (!Configuration.HighMechanismPolicy.NativeProtection)
            { WrapperPreparing?.Invoke(original, patchInfo); return true; }
            MethodInfo control = NativeIlGuards.Control(original);
            if (control != null)
            {
                Rejected += patchInfo.prefixes.Length + patchInfo.postfixes.Length + patchInfo.transpilers.Length
                    + patchInfo.finalizers.Length + patchInfo.innerprefixes.Length + patchInfo.innerpostfixes.Length + patchInfo.innerfinalizers.Length;
                patchInfo.prefixes = patchInfo.postfixes = patchInfo.transpilers = patchInfo.finalizers
                    = patchInfo.innerprefixes = patchInfo.innerpostfixes = patchInfo.innerfinalizers = Array.Empty<Patch>();
                result = control;
                return false;
            }
            BeforeRebuild(original, patchInfo);
            WrapperPreparing?.Invoke(original, patchInfo);
            return true;
        }

        private static Patch Clone(Patch patch) => new Patch(new HarmonyMethod(patch.PatchMethod)
        { priority = patch.priority, before = patch.before.ToArray(), after = patch.after.ToArray(), debug = patch.debug,
            uncheckedReferenceBinding = patch.uncheckedReferenceBinding, innerMethod = patch.innerMethod, innerTarget = patch.innerTarget }, patch.index, patch.owner);
        internal static Patch[][] CaptureAnchors(MethodBase original) => anchors.TryGetValue(original, out Patch[][] groups)
            ? groups.Select(group => group?.Select(Clone).ToArray()).ToArray() : null;
        internal static void RestoreAnchors(MethodBase original, Patch[][] snapshot)
        {
            if (snapshot == null) anchors.Remove(original);
            else anchors[original] = snapshot;
        }
        private static Patch[][] Groups(HarmonyLib.Patches info) => new[]
        { info.Prefixes.ToArray(), info.Postfixes.ToArray(), info.Transpilers.ToArray(), info.Finalizers.ToArray(),
            info.InnerPrefixes.ToArray(), info.InnerPostfixes.ToArray(), info.InnerFinalizers.ToArray() };
        private static bool Own(Patch patch) => HarmonyPatchAuthority.Owns(patch.PatchMethod);
        private static bool Anchor(Patch patch) => Own(patch) && (InfrastructureAssemblies.Contains(patch.PatchMethod.Module.Assembly));
        private static void Capture(MethodBase method, Patch[][] groups, bool bootstrap = false)
        {
            if (!anchors.TryGetValue(method, out Patch[][] saved)) anchors.Add(method, saved = new Patch[7][]);
            for (int i = 0; i < groups.Length; i++)
            {
                var merged = new List<Patch>(saved[i] ?? Array.Empty<Patch>());
                foreach (Patch patch in groups[i])
                    if (Anchor(patch) && (bootstrap || HarmonyPatchAuthority.Allows(method, patch.PatchMethod))
                        && !merged.Any(p => p.PatchMethod == patch.PatchMethod && p.owner == patch.owner)) merged.Add(Clone(patch));
                saved[i] = merged.ToArray();
            }
        }

        private static void Note(MethodBase original, Patch patch, string action)
        {
            if (events.Count == 32) events.Dequeue();
            events.Enqueue(action + " " + original.DeclaringType?.FullName + "." + original.Name + " <- " + patch.owner + ":" + patch.PatchMethod);
        }

        private static Patch[] Resolve(MethodBase original, Patch[] incoming, Patch[] saved, bool owned)
        {
            var result = new List<Patch>(incoming.Length + (saved?.Length ?? 0));
            foreach (Patch patch in incoming)
            {
                bool anchored = saved?.Any(p => p.PatchMethod == patch.PatchMethod && p.owner == patch.owner) ?? false;
                if (owned && !anchored)
                { Rejected++; Note(original, patch, "reject"); continue; }
                if (anchored && result.Any(p => p.PatchMethod == patch.PatchMethod && p.owner == patch.owner))
                { Rejected++; Note(original, patch, "reject-duplicate"); continue; }
                result.Add(patch);
            }
            foreach (Patch anchor in saved ?? Array.Empty<Patch>())
            {
                int index = result.FindIndex(p => p.PatchMethod == anchor.PatchMethod && p.owner == anchor.owner);
                if (index < 0) { result.Add(Clone(anchor)); Restored++; Note(original, anchor, "restore"); }
                else if (!SameBinding(result[index], anchor))
                { result[index] = Clone(anchor); Restored++; Note(original, anchor, "restore-binding"); }
            }
            return result.ToArray();
        }

        private static bool SameBinding(Patch patch, Patch anchor) => patch.index == anchor.index && patch.priority == anchor.priority
            && patch.debug == anchor.debug && patch.uncheckedReferenceBinding == anchor.uncheckedReferenceBinding
            && patch.innerMethod == anchor.innerMethod && patch.innerTarget == anchor.innerTarget
            && patch.before.SequenceEqual(anchor.before) && patch.after.SequenceEqual(anchor.after);

        private static void BeforeRebuild(MethodBase original, PatchInfo patchInfo)
        {
            bool owned = GeneratedIntegrity.Owns(original) || integrityEntries.Contains(original);
            Patch[][] groups = { patchInfo.prefixes, patchInfo.postfixes, patchInfo.transpilers, patchInfo.finalizers, patchInfo.innerprefixes, patchInfo.innerpostfixes, patchInfo.innerfinalizers };
            if (!owned && !anchors.ContainsKey(original) && !groups.Any(g => g.Any(Anchor))) return;
            Capture(original, groups);
            Patch[][] saved = anchors[original];
            for (int i = 0; i < groups.Length; i++) groups[i] = Resolve(original, groups[i], saved[i], owned);
            OrderResultBoundaries(groups[3]);
            patchInfo.prefixes = groups[0]; patchInfo.postfixes = groups[1]; patchInfo.transpilers = groups[2];
            patchInfo.finalizers = groups[3]; patchInfo.innerprefixes = groups[4]; patchInfo.innerpostfixes = groups[5]; patchInfo.innerfinalizers = groups[6];
        }

        private static void OrderResultBoundaries(Patch[] finalizers)
        {
            string[] owners = finalizers.Where(p => Anchor(p) && p.priority == int.MinValue
                && p.PatchMethod.GetParameters().Any(a => a.Name == "__result" && (a.ParameterType == typeof(float).MakeByRefType()
                    || a.ParameterType == typeof(int).MakeByRefType() || a.ParameterType == typeof(bool).MakeByRefType())))
                .Select(p => p.owner).Distinct().ToArray();
            if (owners.Length == 0) return;
            for (int i = 0; i < finalizers.Length; i++)
            {
                Patch patch = finalizers[i];
                if (Own(patch)) continue;
                finalizers[i] = new Patch(new HarmonyMethod(patch.PatchMethod)
                {
                    priority = patch.priority, debug = patch.debug, uncheckedReferenceBinding = patch.uncheckedReferenceBinding,
                    innerMethod = patch.innerMethod, innerTarget = patch.innerTarget,
                    before = patch.before.Concat(owners).Distinct().ToArray(), after = patch.after.Except(owners).ToArray()
                }, patch.index, patch.owner);
            }
        }
    }
}
