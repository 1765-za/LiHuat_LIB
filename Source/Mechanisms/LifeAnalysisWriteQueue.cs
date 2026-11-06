using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisWriteQueue
    {
        private sealed class Job
        {
            internal LifeAnalysisKey Key;
            internal int Generation, Length;
            internal byte[] Bytes;
            internal LifePreparedCacheWrite Prepared;
            internal long RetryAt;
            internal int Attempts;
            internal IOException PreparationError;
        }
        private readonly LifeAnalysisDiskCache cache;
        private readonly Func<LifeAnalysisKey, int, bool> current;
        private readonly int maximumBytes;
        private readonly Dictionary<LifeAnalysisKey, LinkedListNode<Job>> queued = new Dictionary<LifeAnalysisKey, LinkedListNode<Job>>();
        private readonly LinkedList<Job> order = new LinkedList<Job>();
        private readonly List<Job> running = new List<Job>();
        private readonly List<Job> ready = new List<Job>();
        private Task preparation;
        internal int Pending => queued.Count + running.Count + ready.Count;
        internal int Running => running.Count == 0 ? 0 : 1;
        internal long Bytes { get; private set; }
        internal long PeakBytes { get; private set; }
        internal int Skipped { get; private set; }
        internal int Stale { get; private set; }
        internal int Retries { get; private set; }
        internal int Batches { get; private set; }
        internal int MaximumBatch { get; private set; }
        internal bool CanEnqueue(int length) => length <= maximumBytes - Bytes;

        internal LifeAnalysisWriteQueue(LifeAnalysisDiskCache cache, Func<LifeAnalysisKey, int, bool> current, int maximumBytes = 16 * 1024 * 1024)
        { this.cache = cache; this.current = current; this.maximumBytes = maximumBytes; }

        internal void Enqueue(LifeAnalysisKey key, int generation, byte[] bytes)
        {
            if (queued.TryGetValue(key, out LinkedListNode<Job> previous))
            {
                Bytes -= previous.Value.Length;
                order.Remove(previous); queued.Remove(key);
            }
            if (bytes.Length > maximumBytes - Bytes)
            {
                Skipped++;
                return;
            }
            queued.Add(key, order.AddLast(new Job { Key = key, Generation = generation, Bytes = bytes, Length = bytes.Length }));
            Bytes += bytes.Length; PeakBytes = Math.Max(PeakBytes, Bytes);
        }
        internal void Poll(bool startup, bool canPrepare)
        {
            if (ready.Count == 0 && preparation?.IsCompleted == true)
            {
                preparation.GetAwaiter().GetResult();
                ready.AddRange(running); running.Clear(); preparation = null;
            }
            if (startup && canPrepare && ready.Count > 0) Schedule(true);
            for (int i = ready.Count - 1; i >= 0; i--)
            {
                Job done = ready[i];
                if (done.PreparationError != null)
                {
                    if (!current(done.Key, done.Generation) || queued.ContainsKey(done.Key)) Stale++;
                    else if (++done.Attempts < 4)
                    {
                        Retries++; done.PreparationError = null;
                        done.RetryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 20;
                        queued.Add(done.Key, order.AddLast(done)); ready.RemoveAt(i); continue;
                    }
                    else cache.PreparationFailed(done.Key, done.PreparationError);
                    done.Bytes = null; ready.RemoveAt(i); Bytes -= done.Length; continue;
                }
                bool finished = done.Prepared == null;
                if (!finished && !current(done.Key, done.Generation))
                { Stale++; done.Prepared.Dispose(); finished = true; }
                if (!finished)
                {
                    if (Stopwatch.GetTimestamp() < done.RetryAt) continue;
                    finished = cache.Commit(done.Prepared, ++done.Attempts >= 4);
                    if (!finished) { Retries++; done.RetryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 20; }
                }
                if (finished) { ready.RemoveAt(i); Bytes -= done.Length; }
            }
        }
        internal bool Schedule(bool startup)
        {
            if (running.Count > 0 || !startup && ready.Count > 0) return false;
            int batchBytes = 0;
            int available = order.Count;
            while (order.Count > 0 && available-- > 0 && running.Count < (startup ? 16 : 1))
            {
                Job job = order.First.Value;
                if (Stopwatch.GetTimestamp() < job.RetryAt)
                {
                    LinkedListNode<Job> deferred = order.First;
                    order.RemoveFirst(); order.AddLast(deferred); continue;
                }
                if (running.Count > 0 && batchBytes + job.Length > 1024 * 1024) break;
                order.RemoveFirst(); queued.Remove(job.Key);
                if (!current(job.Key, job.Generation)) { Bytes -= job.Length; Stale++; continue; }
                running.Add(job); batchBytes += job.Length;
            }
            if (running.Count == 0) return false;
            Job[] batch = running.ToArray();
            Batches++; MaximumBatch = Math.Max(MaximumBatch, batch.Length);
            preparation = Task.Run(() =>
            {
                foreach (Job job in batch)
                {
                    try { job.Prepared = cache.PrepareWrite(job.Key, job.Bytes, true); }
                    catch (IOException error) { job.PreparationError = error; }
                    finally { if (job.PreparationError == null) job.Bytes = null; }
                }
            });
            return true;
        }
    }
}
