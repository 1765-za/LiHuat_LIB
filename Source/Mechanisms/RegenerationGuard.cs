using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class RegenerationGuard
    {
        private sealed class Entry
        {
            internal MethodInfo Method;
            internal RegenerationSource Source;
            internal MapSpawnSource Factory;
            internal RegenerationKind Kind;
            internal bool Active, TrackSource, CheckMap, CreatesPawn;
        }
        private static readonly HashSet<Assembly> examined = new HashSet<Assembly>();
        private static readonly Queue<Entry> pending = new Queue<Entry>();
        private static readonly HashSet<MethodInfo> queued = new HashSet<MethodInfo>();
        private static readonly Dictionary<MethodInfo, Entry> candidates = new Dictionary<MethodInfo, Entry>();
        private static readonly Dictionary<MethodInfo, int> installed = new Dictionary<MethodInfo, int>();
        private static readonly List<Entry> entries = new List<Entry>();
        private static int lastPublication = -1;
        private static bool pendingChanged;
        public static int Installed => installed.Count;
        public static int Pending => pending.Count;
        public static int Rejected { get; private set; }
        public static int Unreadable { get; private set; }
        public static long AmbiguousSources { get; private set; }
        public static long DeniedFactories { get; private set; }
        internal static bool Active(MethodInfo method) => candidates.TryGetValue(method, out Entry entry) && entry.Active;
        internal static void Discover()
        {
            if (!LibraryCapabilities.Has(LibraryCapability.Regeneration)) return;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || InfrastructureAssemblies.Contains(assembly)
                    || assembly == typeof(Pawn).Assembly || !examined.Add(assembly)) continue;
                Type[] types = LifeDiscoveryMetadata.Types(assembly);
                foreach (Type type in types)
                {
                    if (type == null || type.IsGenericType || type.IsValueType || !LifeResourceEvidence.IsCustom(type)) continue;
                    foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (PatchProvenance.IsReplacement(method)) continue;
                        if (LifeIlReader.HasMetadataBody(method)) SerializedPawnReconstruction.Discover(method);
                        if (!LifeIlReader.HasMetadataBody(method) || method.IsSpecialName || method.ContainsGenericParameters
                            || !SupportsResult(method.ReturnType)) continue;
                        RegenerationSource source;
                        try { source = RegenerationSource.Find(method); }
                        catch (Exception error) when (LifeDiscoveryMetadata.ResolutionFailure(error))
                        {
                            LifeDiscoveryMetadata.Report(method, error);
                            continue;
                        }
                        MapSpawnSource factory = MapSpawnSource.Find(method);
                        if (source == null && factory == null) continue;
                        var entry = new Entry { Method = method, Source = source, Factory=factory };
                        candidates.Add(method, entry); queued.Add(method); pending.Enqueue(entry); pendingChanged = true;
                    }
                }
            }
        }
        private static bool SupportsResult(Type type) => type == typeof(void) || typeof(Thing).IsAssignableFrom(type)
            || type.IsEnum || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong);
        internal static void AnalyzeNext()
        {
            Entry entry = pending.Dequeue();
            var key = new LifeAnalysisKey(LifeAnalysisKind.Regeneration, entry.Method);
            LifeAnalysisResult result = LifeAnalysisCoordinator.Shared.Find(key);
            if (result == null) { LifeAnalysisCoordinator.Shared.Request(key); pending.Enqueue(entry); return; }
            queued.Remove(entry.Method);
            Unreadable += result.Unreadable;
            entry.Kind = ((RegenerationEvidence)result.Regeneration & RegenerationEvidence.Generation) != 0
                ? RegenerationKind.Replacement : RegenerationKind.Resurrection;
            entry.CreatesPawn = ((RegenerationEvidence)result.Regeneration & RegenerationEvidence.Generation) != 0;
            entry.CheckMap = entry.Factory != null && entry.Factory.Proven(entry.Method, (RegenerationEvidence)result.Regeneration);
            entry.TrackSource = entry.Source != null
                && RegenerationAnalysis.Proven((RegenerationEvidence)result.Regeneration, entry.Source.Snapshot, entry.Source.RecordedIdentity);
            entry.Active = entry.CheckMap || entry.TrackSource;
            if (entry.TrackSource && entry.Source.Snapshot)
            {
                if (!entry.Source.RecordedIdentity) RegenerationSupplyGuard.Register(entry.Method, result.Supplies);
                RegenerationRecordScribe.Register(entry.Source);
            }
            if (!entry.Active || installed.ContainsKey(entry.Method)) return;
            try
            {
                int id = entries.Count; entries.Add(entry);
                NativeIlGuards.RegisterTransform(entry.Method, "lihuat.lib.regeneration", 1500,
                    body => RegenerationEmitter.Wrap(entry.Method, body, id, entry.Source?.Argument ?? -1, entry.Factory?.Argument ?? -1));
                installed.Add(entry.Method, id);
            }
            catch (Exception e) { entry.Active = false; Rejected++; Log.Warning("[LihuatLib.Regeneration] Admission was not installed for " + entry.Method + ": " + e.GetType().Name); }
        }
        internal static void QueueRefresh(MethodInfo root)
        {
            if (candidates.TryGetValue(root, out Entry entry) && queued.Add(root))
            { pending.Enqueue(entry); pendingChanged = true; }
        }
        internal static void Update()
        {
            if (pending.Count == 0 || !pendingChanged && lastPublication == LifeAnalysisCoordinator.Shared.Published) return;
            pendingChanged = false; lastPublication = LifeAnalysisCoordinator.Shared.Published;
            int count = pending.Count;
            for (int i = 0; i < count; i++) AnalyzeNext();
        }
        internal static bool Begin(int id, object value, Map map, out RegenerationFlow.Scope scope)
        {
            scope = default;
            Entry entry = entries[id];
            if (!entry.Active || LihuatGame.Current == null) return true;
            if (entry.CreatesPawn && !LihuatGame.Current.Lifecycle.AllowsGeneration())
            {
                DeniedFactories++;
                return false;
            }
            if (entry.CheckMap && !LihuatGame.Current.Domains.AllowsSpawn(null, map))
            {
                DeniedFactories++;
                return false;
            }
            if (!entry.TrackSource) return true;
            Pawn source = entry.Source.Resolve(value);
            if (entry.Source.Snapshot)
                return LihuatGame.Current.Regeneration.BeginRecord(entry.Source.Record(value), source, entry.Source.Identity(value), entry.Kind, out scope);
            if (source == null) { AmbiguousSources++; return true; }
            return LihuatGame.Current.Regeneration.Begin(source, entry.Kind, out scope);
        }
        internal static void End(RegenerationFlow.Scope scope) => scope.Dispose();
    }
}
