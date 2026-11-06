using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeLoadingAnalysis
    {
        private sealed record Prepared(LifeLoadingPayload Payload, bool FromCache);
        private const long MaximumBytes = 32 * 1024 * 1024;
        private readonly Dictionary<LifeAnalysisKey, Prepared> completed = new();
        private readonly string directory;
        private readonly LifeCacheOwner[] owners;
        private readonly Assembly[] assemblies;
        private readonly string policy = InfrastructureAssemblies.PolicyKey;
        private readonly Stopwatch elapsed = new();
        private Task worker;
        private volatile bool stopped;
        private volatile bool callGraphsRunning;
        internal bool CallGraphsRunning => callGraphsRunning;
        private double callGraphMilliseconds;
        internal double CallGraphMilliseconds => Volatile.Read(ref callGraphMilliseconds);
        internal LifeCallGraphRepository CallGraph { get; } = new();
        private long bytes, peakBytes;
        private int prepared, reused, failures;
        private double workMilliseconds;
        internal string Summary
        {
            get
            {
                lock (completed) return "prepared=" + prepared + " reused=" + reused + " failures=" + failures
                    + " peakBytes=" + peakBytes + " retainedBytes=" + bytes
                    + " windowMs=" + elapsed.Elapsed.TotalMilliseconds.ToString("F1")
                    + " workMs=" + workMilliseconds.ToString("F1") + " workerFinished=" + (worker?.IsCompleted ?? true)
                    + " policyChanged=" + (policy != InfrastructureAssemblies.PolicyKey);
            }
        }

        internal LifeLoadingAnalysis(string directory, LifeCacheOwner[] owners, Assembly[] assemblies)
        { this.directory = directory; this.owners = owners; this.assemblies = assemblies; }

        internal void Start()
        {
            elapsed.Start();
            callGraphsRunning = true;
            worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        internal void Stop()
        {
            lock (completed) { stopped = true; elapsed.Stop(); }
        }

        internal bool TryTake(LifeAnalysisKey key, out byte[] encoded, out bool fromCache)
        {
            encoded = null; fromCache = false;
            Prepared value;
            lock (completed)
            {
                if (policy != InfrastructureAssemblies.PolicyKey) { completed.Clear(); bytes = 0; return false; }
                if (!completed.Remove(key, out value)) return false;
                bytes -= value.Payload.Size + 256; reused++;
            }
            try { encoded = value.Payload.Decode(); fromCache = value.FromCache; return true; }
            catch (IOException) { lock (completed) failures++; return false; }
        }

        internal void Release()
        {
            Stop();
            lock (completed) { completed.Clear(); completed.TrimExcess(); bytes = 0; }
        }

        private void Run()
        {
            var snapshots = new LifeAnalysisSnapshots(1024, 8 * 1024 * 1024);
            Thread thread = Thread.CurrentThread;
            ThreadPriority priority = thread.Priority;
            try
            {
                thread.Priority = ThreadPriority.BelowNormal;
                var graphWatch = Stopwatch.StartNew();
                foreach (Assembly assembly in LifeCallGraphPreparation.Sources(assemblies))
                {
                    if (stopped) break;
                    var graph = new LifeCallGraphBuilder(assembly);
                    foreach (object step in graph.Build(() => stopped)) Thread.Sleep(1);
                    if (graph.Result != null) CallGraph.Publish(graph.Result);
                }
                callGraphMilliseconds = graphWatch.Elapsed.TotalMilliseconds;
                callGraphsRunning = false;
                var cache = new LifeAnalysisDiskCache(directory, snapshots, owners);
                long slice = Stopwatch.GetTimestamp();
                foreach (LifeAnalysisKey key in LifeLoadingRoots.Discover(assemblies, () => stopped))
                {
                    if (stopped || policy != InfrastructureAssemblies.PolicyKey) break;
                    long start = Stopwatch.GetTimestamp();
                    try
                    {
                        LifeAnalysisArtifact artifact = cache.Load(key);
                        if (artifact == null)
                        {
                            artifact = Analyze(key, snapshots);
                            if (artifact == null) break;
                            if (!artifact.Persistable) continue;
                        }
                        byte[] encoded = cache.Encode(key, artifact);
                        if (encoded == null) { lock (completed) failures++; continue; }
                        var payload = new LifeLoadingPayload(encoded);
                        long size = payload.Size + 256;
                        lock (completed)
                        {
                            workMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                            if (stopped || bytes + size > MaximumBytes || completed.Count >= 32768) break;
                            completed.Add(key, new Prepared(payload, artifact.Result.FromCache));
                            bytes += size; peakBytes = Math.Max(peakBytes, bytes); prepared++;
                        }
                    }
                    catch (Exception) { lock (completed) failures++; }
                    if (Stopwatch.GetElapsedTime(slice).TotalMilliseconds >= 8)
                    { Thread.Sleep(1); slice = Stopwatch.GetTimestamp(); }
                }
            }
            catch (Exception) { lock (completed) failures++; }
            finally { callGraphsRunning = false; snapshots.Clear(); thread.Priority = priority; }
        }

        private LifeAnalysisArtifact Analyze(LifeAnalysisKey key, LifeAnalysisSnapshots snapshots)
        {
            var work = new LifeAnalysisWork(key, 0, false, snapshots);
            while (!stopped)
            {
                foreach (MethodInfo method in work.Missing)
                {
                    if (stopped) return null;
                    // Early reads do not register runtime watchers or install guards.
                    MethodBody body = method.GetMethodBody();
                    work.Inputs[method] = snapshots.Intern(method, body?.GetILAsByteArray(),
                        body?.LocalVariables.Select(local => local.LocalType).ToArray() ?? Type.EmptyTypes);
                }
                work.Missing.Clear();
                LifeAnalysisArtifact artifact = work.Analyze();
                if (work.Missing.Count == 0) return artifact;
            }
            return null;
        }

    }
}
