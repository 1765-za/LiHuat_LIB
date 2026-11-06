using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAvailabilityCatalog
    {
        private sealed class Edge { internal MethodInfo Method; internal bool SameTarget; }
        private sealed class Effect { internal int Flags; internal readonly List<Edge> Calls = new List<Edge>(); }
        private readonly LifeIlReader reader = new LifeIlReader(true);
        private readonly LifeAvailabilityStorageFlow storage;
        internal readonly Dictionary<MethodInfo, MethodInfo[]> ProposedPrefixes = new();
        private readonly Dictionary<MethodInfo, HashSet<FieldInfo>> predicates = new Dictionary<MethodInfo, HashSet<FieldInfo>>();
        internal readonly HashSet<MethodInfo> Reads = new HashSet<MethodInfo>();
        internal readonly Dictionary<MethodInfo, bool> Guards = new Dictionary<MethodInfo, bool>();
        internal readonly HashSet<MethodInfo> Prefixes = new HashSet<MethodInfo>();
        internal readonly HashSet<MethodInfo> Roots = new HashSet<MethodInfo>();
        internal int Unreadable;

        internal LifeAvailabilityCatalog()
        {
            storage = new LifeAvailabilityStorageFlow(Read);
            AddRoot(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt));
            AddRoot(typeof(Pawn), "Tick");
            AddRoot(typeof(Pawn), "TickInterval");
            AddRoot(typeof(Pawn), "TickRare");
            AddRoot(typeof(Pawn), nameof(Pawn.DrawGUIOverlay));
            AddRoot(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob));
            AddRoot(typeof(Pawn_JobTracker), "JobTrackerTick");
            AddRoot(typeof(Pawn_JobTracker), "JobTrackerTickInterval");
            AddRoot(typeof(JobDriver), nameof(JobDriver.DriverTick));
            AddRoot(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist));
            Roots.Add(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Spawned)));
            Roots.Add(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Dead)));
        }

        private void AddRoot(Type type, string name)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                if (method.Name == name) Roots.Add(method);
        }

        private List<CodeInstruction> Read(MethodInfo method)
        {
            Reads.Add(method);
            try { return reader.Read(method); }
            catch (Exception) { Unreadable++; return null; }
        }

        internal HashSet<MethodInfo> ReadPrefixes()
        {
            foreach (MethodInfo root in Roots)
            {
                if (ProposedPrefixes.TryGetValue(root, out MethodInfo[] proposed))
                {
                    foreach (MethodInfo prefix in proposed)
                        if (LifeResourceEvidence.IsCustom(prefix.DeclaringType)) Prefixes.Add(prefix);
                    continue;
                }
                HarmonyLib.Patches patches = Harmony.GetPatchInfo(root);
                if (patches == null) continue;
                foreach (Patch patch in patches.Prefixes)
                    if (LifeResourceEvidence.IsCustom(patch.PatchMethod.DeclaringType)) Prefixes.Add(patch.PatchMethod);
            }
            return Prefixes;
        }

        internal void Scan()
        {
            ReadPrefixes();
            var forbidden = new HashSet<FieldInfo>();
            foreach (MethodInfo prefix in Prefixes)
            {
                int target = AvailabilityIlPatterns.Recipient(prefix);
                if (target < 0 || prefix.ReturnType != typeof(bool)) continue;
                List<CodeInstruction> code = Read(prefix);
                if (code == null) continue;
                var flow = new LifeControlFlow(code);
                for (int i = 0; i < code.Count; i++)
                    if (Call(code[i]) is MethodInfo call && call.ReturnType == typeof(bool)
                        && AvailabilityIlPatterns.Recipient(call) is int input && input >= 0
                        && AvailabilityIlPatterns.TargetInput(code, i, call, input, target)
                        && LifeAvailabilityBooleanFlow.Returns(flow, i, true))
                    {
                        HashSet<FieldInfo> fields = PredicateFields(call);
                        forbidden.UnionWith(fields);
                        if (fields.Count > 0 && !prefix.GetParameters().Any(p => p.ParameterType.IsByRef)) Guards[prefix] = true;
                    }
            }
            foreach (var pair in storage.Summaries)
            {
                var reads = pair.Key.ReturnType == typeof(bool) ? pair.Value.Reads : pair.Value.ReferenceReads;
                if (reads.Count > 0 && reads.All(source => source.Field != null))
                    predicates[pair.Key] = new HashSet<FieldInfo>(reads.Select(source => source.Field));
            }
            foreach (var pair in predicates)
                if (pair.Value.Overlaps(forbidden)) Guards[pair.Key] = false;
            foreach (Assembly assembly in forbidden.Select(f => f.Module.Assembly).Distinct()) Writers(assembly, forbidden);
        }

        private HashSet<FieldInfo> PredicateFields(MethodInfo method)
        {
            var summary = storage.Inspect(method);
            return summary.Reads.Any(source => source.Field == null) ? new HashSet<FieldInfo>()
                : new HashSet<FieldInfo>(summary.Reads.Select(source => source.Field));
        }

        private void Writers(Assembly assembly, HashSet<FieldInfo> fields)
        {
            var methods = new Dictionary<MethodInfo, Effect>();
            foreach (MethodInfo method in LifeRetirementGuard.Candidates(assembly))
            {
                int target = AvailabilityIlPatterns.Recipient(method);
                if (target < 0) continue;
                List<CodeInstruction> code = Read(method);
                if (code == null) continue;
                var effect = new Effect(); methods[method] = effect;
                foreach (var write in storage.Inspect(method).Mutations)
                    if (write.Key.Field != null && fields.Contains(write.Key.Field)) effect.Flags |= write.Value;
                for (int i = 0; i < code.Count; i++)
                {
                    MethodInfo call = Call(code[i]);
                    if (call == null) continue;
                    if (call.Module.Assembly == assembly)
                    {
                        int input = AvailabilityIlPatterns.Recipient(call);
                        if (input >= 0) effect.Calls.Add(new Edge { Method = call,
                            SameTarget = AvailabilityIlPatterns.TargetInput(code, i, call, input, target) });
                    }
                }
            }
            bool changed;
            do
            {
                changed = false;
                foreach (var pair in methods)
                {
                    int before = pair.Value.Flags;
                    foreach (Edge edge in pair.Value.Calls)
                        if (methods.TryGetValue(edge.Method, out Effect child))
                            pair.Value.Flags |= edge.SameTarget ? child.Flags : child.Flags == 0 ? 0 : 2;
                    changed |= before != pair.Value.Flags;
                }
            } while (changed);
            foreach (var pair in methods)
                if (pair.Value.Flags == 1 && (pair.Key.ReturnType == typeof(void) || pair.Key.ReturnType == typeof(bool))
                    && !pair.Key.GetParameters().Any(p => p.ParameterType.IsByRef))
                    Guards[pair.Key] = Prefixes.Contains(pair.Key);
        }

        private static MethodInfo Call(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt ? instruction.operand as MethodInfo : null;
    }
}
