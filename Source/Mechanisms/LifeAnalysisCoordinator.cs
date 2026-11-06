using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisCoordinator
    {
        private sealed class Entry
        {
            internal int Generation;
            internal LifeAnalysisWork Work;
            internal LifeAnalysisResult Result;
            internal bool Counted;
        }
        private sealed class Completion
        {
            internal LifeAnalysisWork Work;
            internal LifeAnalysisArtifact Artifact;
            internal byte[] Encoded;
            internal Exception Error;
            internal MethodInfo[] Validate;
            internal int Cursor;
            internal bool Runtime;
            internal int WorkerThread;
            internal double Milliseconds;
        }
        private readonly Dictionary<LifeAnalysisKey, Entry> entries = new Dictionary<LifeAnalysisKey, Entry>();
        private readonly LifeAnalysisDependencyIndex dependencies = new LifeAnalysisDependencyIndex();
        private readonly Queue<LifeAnalysisWork> waiting = new Queue<LifeAnalysisWork>();
        private readonly List<Task<Completion>> running = new List<Task<Completion>>();
        private readonly Queue<Completion> completing = new Queue<Completion>();
        private readonly Dictionary<LifeAnalysisKey, MethodInfo[]> unresolved = new Dictionary<LifeAnalysisKey, MethodInfo[]>();
        private readonly LifeAnalysisWriteQueue writes;
        private readonly LifeIlReader capture = new LifeIlReader(true);
        private readonly LifeAnalysisSnapshots snapshots = new();
        private LifeLoadingAnalysis loading;
        internal void FinishLoading() { MainThread(); loading = null; }
        private readonly bool cacheOnly = StarBloodScanPolicy.CacheOnly;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly int workerLimit = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
        internal readonly LifeAnalysisDiskCache Cache;
        internal readonly LifeDamageEntryIndex DamageEntries = new LifeDamageEntryIndex();
        internal readonly LifeCallGraphRepository CallGraph;
        internal static LifeAnalysisCoordinator Shared { get; private set; }
        internal int Pending => waiting.Count + running.Count + completing.Count + writes.Pending;
        internal int Published { get; private set; }
        internal int Analyzed { get; private set; }
        internal int CacheHits { get; private set; }
        internal int CacheOnlySkipped { get; private set; }
        internal int Discarded { get; private set; }
        internal int Failures { get; private set; }
        internal int PeakWorkers { get; private set; }
        internal int PreparedTypes { get; private set; }
        internal int PreparedCounters { get; private set; }
        internal double MaximumPumpMilliseconds { get; private set; }
        internal double MaximumRuntimePumpMilliseconds { get; private set; }
        internal int RuntimeWorkerPasses { get; private set; }
        internal int LastRuntimeWorkerThread { get; private set; }
        internal int RuntimeDemandAnalyses { get; private set; }
        internal double RuntimeWorkerMilliseconds { get; private set; }
        internal double RuntimeDemandMilliseconds { get; private set; }
        internal int WorkerLimit => workerLimit;
        internal long ReleasedInputBytes { get; private set; }
        internal int DependencyEdges => dependencies.Edges;
        internal long PendingWriteBytes => writes.Bytes;
        internal long PeakWriteBytes => writes.PeakBytes;
        internal int StaleWrites => writes.Stale;
        internal int SkippedWrites => writes.Skipped;
        internal int WriteRetries => writes.Retries;
        internal int WriteBatches => writes.Batches;
        internal int MaximumWriteBatch => writes.MaximumBatch;
        internal long SnapshotHits => snapshots.Hits;
        internal int SnapshotCount => snapshots.Count;
        internal long SnapshotBytes => snapshots.Bytes;
        internal void ReleaseSnapshots() { MainThread(); if (Pending == 0) snapshots.Clear(); }
        private int lastFrame = -1;
        private int assemblyRevision;
        private long nextProgress;

        private LifeAnalysisCoordinator(string directory, IEnumerable<LifeCacheOwner> owners, LifeLoadingAnalysis loading)
        {
            this.loading = loading;
            CallGraph = loading?.CallGraph ?? new LifeCallGraphRepository();
            Cache = new LifeAnalysisDiskCache(directory, snapshots, owners);
            writes = new LifeAnalysisWriteQueue(Cache, (key, generation) => entries.TryGetValue(key, out Entry entry) && entry.Generation == generation);
            PatchIntegrity.WrapperCommitted += DamageEntries.Invalidate;
        }
        internal static void Initialize(string directory, IEnumerable<LifeCacheOwner> owners, LifeLoadingAnalysis loading = null)
        {
            if (!LibraryCapabilities.NeedsAnalysis) throw new InvalidOperationException("No library analysis capability is enabled.");
            if (Shared == null) Shared = new LifeAnalysisCoordinator(directory, owners, loading);
        }
        private void MainThread()
        { if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Life analysis publication requires its owning thread."); }
        internal LifeAnalysisResult Find(LifeAnalysisKey key) => entries.TryGetValue(key, out Entry entry) ? entry.Result : null;
        internal LifeCallAnalysis[] CallAnalyses(MethodInfo method)
        {
            MainThread();
            return method == null ? Array.Empty<LifeCallAnalysis>()
                : dependencies.Roots(NativeIlGuards.Definition(method)).Select(key => new LifeCallAnalysis(key.Root, key.Kind.ToString())).ToArray();
        }
        private bool UsesCacheOnly(LifeAnalysisKey key) => cacheOnly
            && key.Kind != LifeAnalysisKind.Regeneration && key.Kind != LifeAnalysisKind.Reconstruction;
        internal bool Request(LifeAnalysisKey key)
        {
            MainThread();
            if (entries.ContainsKey(key)) return true;
            var entry = new Entry(); entries.Add(key, entry);
            entry.Work = NewWork(key, 0); waiting.Enqueue(entry.Work);
            return true;
        }
        internal LifeAnalysisResult Resolve(LifeAnalysisKey key)
        {
            MainThread();
            RefreshAssemblies();
            if (!Request(key)) return new LifeAnalysisResult { Limited = 1 };
            Entry entry = entries[key];
            if (entry.Result != null) return entry.Result;
            bool runtime = LifeAnalysisPreparation.Ready;
            long started = runtime ? Stopwatch.GetTimestamp() : 0;
            if (runtime) RuntimeDemandAnalyses++;
            if (entry.Work.Running)
            {
                entry.Generation++;
                entry.Work = NewWork(key, entry.Generation);
            }
            LifeAnalysisWork work = entry.Work;
            while (Current(work))
            {
                foreach (MethodInfo method in work.Missing) work.Inputs[method] = Capture(method);
                work.Missing.Clear();
                Completion done = Execute(work);
                if (done.Error != null) { Fail(work, done.Error.ToString()); break; }
                if (work.Missing.Count > 0)
                {
                    continue;
                }
                if (done.Artifact.Inputs.Any(input => !input.Value.Matches(Capture(input.Key, true))))
                {
                    entry.Generation++;
                    entry.Work = work = NewWork(key, entry.Generation);
                    work.CacheChecked = true; Discarded++; continue;
                }
                Publish(done); break;
            }
            if (runtime) RuntimeDemandMilliseconds += Elapsed(started);
            return entry.Result;
        }
        internal void Invalidate(MethodInfo method)
        {
            MainThread();
            snapshots.Invalidate(method);
            LifeExecutionGuard.Invalidate(method);
            foreach (LifeAnalysisKey key in dependencies.Roots(method))
            {
                Entry entry = entries[key];
                if (entry.Result == null && entry.Work != null && !entry.Work.Running
                    && entry.Work.CacheChecked && entry.Work.Inputs.Count == 0
                    && entry.Work.Cached == null && entry.Work.Reads.Count == 0) continue;
                if (entry.Result != null)
                {
                    foreach (MethodInfo installed in entry.Result.Sites.Select(s => s.Method).Concat(entry.Result.Deferred.Select(s => s.Method)).Distinct())
                        if (installed != method) LifeExecutionGuard.Invalidate(installed);
                    if (entry.Counted && key.Kind == LifeAnalysisKind.Type) PreparedTypes--;
                    if (entry.Counted && key.Kind == LifeAnalysisKind.Counter) PreparedCounters--;
                }
                entry.Counted = false;
                entry.Result = null; entry.Generation++;
                if (key.Root is MethodInfo root)
                {
                    if (key.Kind == LifeAnalysisKind.Retirement) LifeRetirementGuard.QueueRefresh(root);
                    else if (key.Kind == LifeAnalysisKind.Regeneration) RegenerationGuard.QueueRefresh(root);
                }
                if (UsesCacheOnly(key))
                {
                    entry.Result = new LifeAnalysisResult { Limited = 1, CacheUnavailable = true };
                    entry.Work = null;
                    dependencies.Replace(key, Array.Empty<MethodInfo>());
                    CacheOnlySkipped++;
                    continue;
                }
                entry.Work = NewWork(key, entry.Generation);
                entry.Work.CacheChecked = true;
                waiting.Enqueue(entry.Work);
            }
        }
        private bool Current(LifeAnalysisWork work) => entries.TryGetValue(work.Key, out Entry entry)
            && entry.Generation == work.Generation && ReferenceEquals(entry.Work, work);
        private LifeAnalysisWork NewWork(LifeAnalysisKey key, int generation) => new(key, generation, UsesCacheOnly(key), snapshots);
        private LifeMethodSnapshot Capture(MethodInfo method, bool fresh = false)
        {
            if (!fresh && snapshots.Find(method) is LifeMethodSnapshot shared) return shared;
            try
            {
                byte[] body = capture.ReadBody(method);
                return snapshots.Intern(method, body, body == null ? Type.EmptyTypes : capture.Locals(method));
            }
            catch (Exception) { return snapshots.Intern(method, null, Type.EmptyTypes); }
        }
        internal void Pump(bool startup = false)
        {
            MainThread();
            if (!startup && lastFrame == UnityEngine.Time.frameCount) return;
            lastFrame = UnityEngine.Time.frameCount;
            if (!startup && Pending == 0 && assemblyRevision == LifeMethodRevisions.AssemblyRevision)
            { snapshots.Clear(); return; }
            long started = Stopwatch.GetTimestamp();
            RefreshAssemblies();
            writes.Poll(startup, running.Count < workerLimit);
            PeakWorkers = Math.Max(PeakWorkers, running.Count + writes.Running);
            for (int i = running.Count - 1; i >= 0; i--)
            {
                if (!running[i].IsCompleted) continue;
                Completion done = running[i].GetAwaiter().GetResult(); running.RemoveAt(i);
                if (done.Runtime)
                {
                    RuntimeWorkerPasses++; RuntimeWorkerMilliseconds += done.Milliseconds;
                    LastRuntimeWorkerThread = done.WorkerThread;
                }
                done.Work.Running = false;
                if (!Current(done.Work)) { Discarded++; continue; }
                if (done.Error != null) { Fail(done.Work, done.Error.ToString()); continue; }
                if (done.Work.Missing.Count > 0)
                {
                    waiting.Enqueue(done.Work);
                    continue;
                }
                done.Validate = done.Artifact.Inputs.Keys.ToArray();
                dependencies.Replace(done.Work.Key, done.Validate);
                completing.Enqueue(done);
            }
            while (true)
            {
                if (completing.Count > 0)
                {
                    Completion done = completing.Peek();
                    if (!Current(done.Work)) { completing.Dequeue(); Discarded++; continue; }
                    if (done.Cursor < done.Validate.Length)
                    {
                        MethodInfo method = done.Validate[done.Cursor++];
                        if (!done.Artifact.Inputs[method].Matches(Capture(method, true)))
                        {
                            completing.Dequeue(); Discarded++;
                            Entry entry = entries[done.Work.Key]; entry.Generation++;
                            entry.Work = NewWork(done.Work.Key, entry.Generation);
                            entry.Work.CacheChecked = true;
                            waiting.Enqueue(entry.Work);
                        }
                        continue;
                    }
                    if (startup && done.Encoded != null && !writes.CanEnqueue(done.Encoded.Length))
                    { writes.Schedule(startup); break; }
                    completing.Dequeue(); Publish(done);
                    continue;
                }
                if (running.Count + writes.Running >= workerLimit) break;
                if (writes.Pending >= 8 || writes.Bytes >= 8 * 1024 * 1024)
                {
                    if (writes.Schedule(startup)) continue;
                    if (startup && writes.Bytes >= 8 * 1024 * 1024) break;
                }
                if (waiting.Count > 0)
                {
                    LifeAnalysisWork work = waiting.Peek();
                    if (!Current(work)) { waiting.Dequeue(); continue; }
                    if (work.CacheChecked && work.Missing.Count > 0)
                    {
                        MethodInfo method = work.Missing.First(); work.Missing.Remove(method);
                        work.Inputs[method] = Capture(method);
                        continue;
                    }
                    bool runtime = LifeAnalysisPreparation.Ready;
                    waiting.Dequeue(); work.Running = true; running.Add(Task.Run(() => Execute(work, runtime)));
                    PeakWorkers = Math.Max(PeakWorkers, running.Count + writes.Running);
                    continue;
                }
                if (writes.Schedule(startup)) continue;
                break;
            }
            MaximumPumpMilliseconds = Math.Max(MaximumPumpMilliseconds, Elapsed(started));
            if (!startup) ReleaseSnapshots();
            if (LifeAnalysisPreparation.Ready) MaximumRuntimePumpMilliseconds = Math.Max(MaximumRuntimePumpMilliseconds, Elapsed(started));
            if (startup && Pending > 0 && Stopwatch.GetTimestamp() >= nextProgress)
            {
                nextProgress = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15;
                var active = entries.Values.Where(e => e.Work?.Running == true).Select(e => e.Work)
                    .Select(w => w.Key.Root.DeclaringType?.FullName + ":" + w.Key.Root.Name + " at="
                        + w.ActiveAnalysis?.CurrentMethod?.DeclaringType?.FullName + "." + w.ActiveAnalysis?.CurrentMethod?.Name
                        + ":" + w.ActiveAnalysis?.CurrentInstruction + " states=" + w.ActiveAnalysis?.VisitedStates);
                Log.Message("[LihuatLib.StarBlood] Preparing: completed=" + Published + " pending=" + Pending
                    + " workers=" + running.Count + " managedMB=" + GC.GetTotalMemory(false) / (1024 * 1024)
                    + " active=" + string.Join(" | ", active));
            }
        }
        private Completion Execute(LifeAnalysisWork work, bool runtime = false)
        {
            long started = Stopwatch.GetTimestamp();
            var done = new Completion { Work = work, Runtime = runtime, WorkerThread = Thread.CurrentThread.ManagedThreadId };
            try
            {
                if (!work.CacheChecked && !UsesCacheOnly(work.Key) && loading != null
                    && loading.TryTake(work.Key, out byte[] prepared, out bool fromCache))
                {
                    done.Artifact = Cache.Decode(work.Key, prepared, fromCache);
                    if (done.Artifact != null)
                    {
                        done.Encoded = fromCache ? null : prepared;
                        work.CacheChecked = true;
                        work.Missing.Clear();
                        done.Milliseconds = Elapsed(started);
                        return done;
                    }
                }
                if (!work.CacheChecked) { work.Cached = Cache.Load(work.Key); work.CacheChecked = true; }
                if (work.Cached != null) { work.Missing.Clear(); done.Artifact = work.Cached; }
                else if (UsesCacheOnly(work.Key))
                {
                    work.Missing.Clear();
                    done.Artifact = new LifeAnalysisArtifact(new LifeAnalysisResult { Limited = 1, CacheUnavailable = true },
                        new Dictionary<MethodInfo, LifeMethodSnapshot>());
                }
                else done.Artifact = work.Analyze();
                if (!UsesCacheOnly(work.Key) && work.Missing.Count == 0 && !done.Artifact.Result.FromCache) done.Encoded = Cache.Encode(work.Key, done.Artifact);
            }
            catch (Exception error) { done.Error = error; }
            done.Milliseconds = Elapsed(started);
            return done;
        }
        private void Publish(Completion done)
        {
            LifeAnalysisResult result = done.Artifact.Result;
            if (done.Artifact.Unavailable != null)
            {
                unresolved[done.Work.Key] = done.Artifact.Unavailable.Keys.ToArray();
                foreach (var item in done.Artifact.Unavailable)
                    LifeDiscoveryMetadata.Report(item.Key, item.Value);
            }
            else unresolved.Remove(done.Work.Key);
            if (done.Work.Key.Kind == LifeAnalysisKind.Counter)
                LifeExecutionGuard.Register(result.Sites.Where(s => !s.RequiresBinding));
            LifeDeferredGuard.Register(result.Deferred);
            Entry entry = entries[done.Work.Key]; entry.Result = result;
            dependencies.Replace(done.Work.Key, done.Artifact.Inputs.Keys);
            foreach (LifeMethodSnapshot input in done.Artifact.Inputs.Values) ReleasedInputBytes += input.Body?.Length ?? 0;
            if (done.Work.Key.Kind == LifeAnalysisKind.Type) PreparedTypes++;
            if (done.Work.Key.Kind == LifeAnalysisKind.Counter) PreparedCounters++;
            if (result.CacheUnavailable) CacheOnlySkipped++;
            else if (!result.FromCache) Analyzed++;
            else { CacheHits++; Cache.Accept(done.Work.Key); }
            Published++;
            entry.Counted = true;
            if (done.Encoded != null) writes.Enqueue(done.Work.Key, done.Work.Generation, done.Encoded);
            entry.Work = null;
            done.Work.Inputs.Clear(); done.Work.Reads.Clear(); done.Work.Cached = null;
            done.Artifact = null; done.Encoded = null;
        }
        private void Fail(LifeAnalysisWork work, string reason)
        {
            Failures++;
            entries[work.Key].Result = new LifeAnalysisResult { Limited = 1 };
            entries[work.Key].Work = null;
            dependencies.Replace(work.Key, work.Inputs.Keys.Concat(work.Roots));
            Published++;
            Log.Warning("[LihuatLib.StarBlood] Analysis incomplete: " + work.Key + " reason=" + reason);
        }
        private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        private void RefreshAssemblies()
        {
            if (assemblyRevision == LifeMethodRevisions.AssemblyRevision) return;
            assemblyRevision = LifeMethodRevisions.AssemblyRevision; Cache.RegisterAssemblies(); snapshots.Clear();
            if (unresolved.Count > 0)
            {
                var methods = unresolved.Values.SelectMany(value => value).Distinct().ToArray(); unresolved.Clear();
                foreach (MethodInfo method in methods) Invalidate(method);
            }
            if (StarBlood.LifeAnalysisPreparation.Ready && LibraryCapabilities.Has(LibraryCapability.Regeneration)) RegenerationGuard.Discover();
        }
    }

    public static class LifeAnalysisDiagnostics
    {
        public static int MetadataUnavailable => LifeDiscoveryMetadata.Unavailable;
        public static int Pending => LifeAnalysisCoordinator.Shared?.Pending ?? 0;
        public static int WorkerLimit => LifeAnalysisCoordinator.Shared?.WorkerLimit ?? 0;
        public static int PeakWorkers => LifeAnalysisCoordinator.Shared?.PeakWorkers ?? 0;
        public static int Analyzed => LifeAnalysisCoordinator.Shared?.Analyzed ?? 0;
        public static int CacheHits => LifeAnalysisCoordinator.Shared?.CacheHits ?? 0;
        public static int CacheOnlySkipped => LifeAnalysisCoordinator.Shared?.CacheOnlySkipped ?? 0;
        public static int CacheMisses => LifeAnalysisCoordinator.Shared?.Cache.Misses ?? 0;
        public static int CacheInvalid => LifeAnalysisCoordinator.Shared?.Cache.Invalid ?? 0;
        public static int CacheWrites => LifeAnalysisCoordinator.Shared?.Cache.Writes ?? 0;
        public static int CacheFailures => LifeAnalysisCoordinator.Shared?.Cache.Failures ?? 0;
        public static int Discarded => LifeAnalysisCoordinator.Shared?.Discarded ?? 0;
        public static double MaximumPumpMilliseconds => LifeAnalysisCoordinator.Shared?.MaximumPumpMilliseconds ?? 0;
        public static double MaximumRuntimePumpMilliseconds => LifeAnalysisCoordinator.Shared?.MaximumRuntimePumpMilliseconds ?? 0;
        public static int RuntimeWorkerPasses => LifeAnalysisCoordinator.Shared?.RuntimeWorkerPasses ?? 0;
        public static int LastRuntimeWorkerThread => LifeAnalysisCoordinator.Shared?.LastRuntimeWorkerThread ?? 0;
        public static int RuntimeDemandAnalyses => LifeAnalysisCoordinator.Shared?.RuntimeDemandAnalyses ?? 0;
        public static double RuntimeWorkerMilliseconds => LifeAnalysisCoordinator.Shared?.RuntimeWorkerMilliseconds ?? 0;
        public static double RuntimeDemandMilliseconds => LifeAnalysisCoordinator.Shared?.RuntimeDemandMilliseconds ?? 0;
        public static long ReleasedInputBytes => LifeAnalysisCoordinator.Shared?.ReleasedInputBytes ?? 0;
        public static int DependencyEdges => LifeAnalysisCoordinator.Shared?.DependencyEdges ?? 0;
        public static long PendingWriteBytes => LifeAnalysisCoordinator.Shared?.PendingWriteBytes ?? 0;
        public static long PeakWriteBytes => LifeAnalysisCoordinator.Shared?.PeakWriteBytes ?? 0;
        public static int StaleWrites => LifeAnalysisCoordinator.Shared?.StaleWrites ?? 0;
        public static int SkippedWrites => LifeAnalysisCoordinator.Shared?.SkippedWrites ?? 0;
        public static string LastCacheFailure => LifeAnalysisCoordinator.Shared?.Cache.LastFailure;
        public static int WriteRetries => LifeAnalysisCoordinator.Shared?.WriteRetries ?? 0;
        public static int WriteBatches => LifeAnalysisCoordinator.Shared?.WriteBatches ?? 0;
        public static int MaximumWriteBatch => LifeAnalysisCoordinator.Shared?.MaximumWriteBatch ?? 0;
        public static long SnapshotHits => LifeAnalysisCoordinator.Shared?.SnapshotHits ?? 0;
        public static int SnapshotCount => LifeAnalysisCoordinator.Shared?.SnapshotCount ?? 0;
        public static long SnapshotBytes => LifeAnalysisCoordinator.Shared?.SnapshotBytes ?? 0;
    }
}
