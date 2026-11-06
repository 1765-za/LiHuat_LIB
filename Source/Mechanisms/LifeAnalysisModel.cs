using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal enum LifeAnalysisKind { Type, Counter, Callback, Retirement, Regeneration, Reconstruction, Availability }

    internal readonly struct LifeAnalysisKey : IEquatable<LifeAnalysisKey>
    {
        public readonly LifeAnalysisKind Kind;
        public readonly MemberInfo Root;
        public LifeAnalysisKey(LifeAnalysisKind kind, MemberInfo root) { Kind = kind; Root = root; }
        public Type Type => Root as Type ?? Root.DeclaringType;
        public bool Equals(LifeAnalysisKey other) => Kind == other.Kind && Equals(Root, other.Root);
        public override bool Equals(object value) => value is LifeAnalysisKey other && Equals(other);
        public override int GetHashCode() => unchecked(Root.GetHashCode() * 31 + (int)Kind);
        public override string ToString() => Kind + ":" + Root.Module.ModuleVersionId + ":" + Root.MetadataToken + ":" + Type.AssemblyQualifiedName
            + (Root is MethodInfo method && method.IsGenericMethod
                ? ":" + string.Join(";", method.GetGenericArguments().Select(t => t.AssemblyQualifiedName)) : string.Empty);
    }

    internal sealed class LifeMethodSnapshot
    {
        public readonly byte[] Body;
        public readonly Type[] Locals;
        private List<CodeInstruction> decoded;
        private bool decodedReady;
        public LifeMethodSnapshot(byte[] body, Type[] locals)
        {
            Body = body; Locals = locals;
        }
        public bool Matches(LifeMethodSnapshot other) => ReferenceEquals(this, other) || Matches(other.Body, other.Locals);
        internal bool Matches(byte[] body, Type[] locals) => (Body == null) == (body == null)
            && Body.AsSpan().SequenceEqual(body) && Locals.AsSpan().SequenceEqual(locals);
        internal List<CodeInstruction> Code(MethodInfo method)
        {
            lock (this)
            {
                // Analysis consumers read these instructions; patch emitters own separate instruction lists.
                if (!decodedReady) { decoded = LifeIlReader.Decode(method, Body); decodedReady = true; }
                return decoded;
            }
        }
    }

    internal sealed class LifeAnalysisResult
    {
        public LifeDependency[] Dependencies = Array.Empty<LifeDependency>();
        public LifeExecutionSite[] Sites = Array.Empty<LifeExecutionSite>();
        public LifeDeferredSite[] Deferred = Array.Empty<LifeDeferredSite>();
        public RegenerationSupplySite[] Supplies = Array.Empty<RegenerationSupplySite>();
        public MethodInfo[] Methods = Array.Empty<MethodInfo>();
        public KeyValuePair<MethodInfo, bool>[] Availability = Array.Empty<KeyValuePair<MethodInfo, bool>>();
        public bool Reconstruction;
        public bool CacheUnavailable;
        public int Unreadable, Limited, Retirement, Regeneration;
        public bool FromCache;
    }

    internal sealed class LifeAnalysisWork
    {
        public readonly LifeAnalysisKey Key;
        public readonly MethodInfo[] Roots;
        public readonly int Generation;
        public readonly Dictionary<MethodInfo, LifeMethodSnapshot> Inputs = new Dictionary<MethodInfo, LifeMethodSnapshot>();
        public readonly HashSet<MethodInfo> Missing = new HashSet<MethodInfo>();
        public readonly HashSet<MethodInfo> Reads = new HashSet<MethodInfo>();
        public LifeAnalysisArtifact Cached;
        public bool CacheChecked;
        public bool Running;
        internal LifeMethodAnalysis ActiveAnalysis;
        private readonly LifeAnalysisSnapshots snapshots;
        private readonly LifeIlReader reader;
        private readonly LifeAnalysisTraversal traversal;
        private RegenerationAnalysis regeneration;
        public LifeAnalysisWork(LifeAnalysisKey key, int generation, bool cacheOnly = false, LifeAnalysisSnapshots snapshots = null)
        {
            Key = key; Generation = generation; this.snapshots = snapshots;
            reader = new LifeIlReader(Read, Locals, Code);
            if (cacheOnly) { Roots = Array.Empty<MethodInfo>(); return; }
            var methods = new List<MethodInfo>();
            if (key.Root is MethodInfo method) methods.Add(method);
            else for (Type cursor = key.Type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
                methods.AddRange(LifeDiscoveryMetadata.Methods(cursor, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(LifeIlReader.HasMetadataBody));
            Roots = methods.ToArray();
            traversal = new LifeAnalysisTraversal(Roots);
            Missing.UnionWith(Roots);
        }
        internal bool Available(MethodInfo method)
        {
            Reads.Add(method);
            if (Inputs.ContainsKey(method)) return true;
            LifeMethodSnapshot shared = snapshots?.Find(method);
            if (shared != null) { Inputs.Add(method, shared); return true; }
            Missing.Add(method); return false;
        }
        public byte[] Read(MethodInfo method)
        {
            return Available(method) ? Inputs[method].Body : null;
        }
        private List<CodeInstruction> Code(MethodInfo method) => Available(method) ? Inputs[method].Code(method) : null;
        public Type[] Locals(MethodInfo method) => Inputs.TryGetValue(method, out LifeMethodSnapshot input) ? input.Locals : Type.EmptyTypes;
        public LifeAnalysisArtifact Analyze()
        {
            Missing.Clear();
            if (Key.Kind == LifeAnalysisKind.Reconstruction)
            {
                var code = reader.Read((MethodInfo)Key.Root);
                return Artifact(new LifeAnalysisResult { Reconstruction = code != null && code.Any(line =>
                    line.operand is MethodInfo call && call.DeclaringType == typeof(Verse.Scribe_Deep)
                    && call.Name == nameof(Verse.Scribe_Deep.Look)) }, reader);
            }
            if (Key.Kind == LifeAnalysisKind.Regeneration)
            {
                regeneration ??= new RegenerationAnalysis(reader);
                int evidence = (int)regeneration.Inspect((MethodInfo)Key.Root, Available);
                var result = new LifeAnalysisResult { Regeneration = evidence, Unreadable = regeneration.Unreadable };
                if (RegenerationAnalysis.Proven((RegenerationEvidence)evidence, true))
                    result.Supplies = new RegenerationSupplyAnalysis(reader).Find(regeneration.Visited);
                return Artifact(result, reader);
            }
            traversal.Resume(this, reader);
            if (Missing.Count > 0) return Artifact(new LifeAnalysisResult(), reader);
            if (Key.Kind == LifeAnalysisKind.Retirement)
            {
                var retirement = new LifeRetirementAnalysis(reader);
                int effect = retirement.Inspect((MethodInfo)Key.Root);
                return Artifact(new LifeAnalysisResult { Retirement = effect, Unreadable = retirement.Unreadable,
                    Limited = (effect & 4) == 0 ? 0 : 1 }, reader);
            }
            var analysis = new LifeMethodAnalysis(Key.Type, reader);
            ActiveAnalysis = analysis;
            foreach (MethodInfo method in Roots) analysis.Inspect(method, Key.Kind == LifeAnalysisKind.Callback);
            return Artifact(new LifeAnalysisResult { Dependencies = analysis.Dependencies.ToArray(), Sites = analysis.ExecutionSites.ToArray(),
                Deferred = analysis.DeferredSites.ToArray(), Methods = analysis.ReadMethods.ToArray(),
                Unreadable = analysis.Unreadable, Limited = analysis.Limited }, reader);
        }
        private LifeAnalysisArtifact Artifact(LifeAnalysisResult result, LifeIlReader reader)
        {
            if (reader.Unavailable.Count > 0)
            {
                result.Unreadable = Math.Max(result.Unreadable, reader.Unavailable.Count);
                result.Limited = Math.Max(1, result.Limited);
            }
            return new LifeAnalysisArtifact(result, UsedInputs(), Missing.Count == 0 && reader.Unavailable.Count == 0)
            { Unavailable = reader.Unavailable.Count == 0 ? null : reader.Unavailable };
        }
        private Dictionary<MethodInfo, LifeMethodSnapshot> UsedInputs() => Reads.Where(Inputs.ContainsKey).ToDictionary(m => m, m => Inputs[m]);
    }
}
