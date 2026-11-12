using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    public static class LifeMethodRevisions
    {
        private sealed class Entry
        {
            public MethodInfo Method;
            public ulong Fingerprint;
        }
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly Dictionary<MethodInfo, Entry> known = new Dictionary<MethodInfo, Entry>();
        private static int cursor, end;
        private static int lastFrame = -1;
        private static int assemblyRevision;
        static LifeMethodRevisions()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (_, loaded) =>
            { if (!loaded.LoadedAssembly.IsDynamic) System.Threading.Interlocked.Increment(ref assemblyRevision); };
        }
        internal static int AssemblyRevision => System.Threading.Volatile.Read(ref assemblyRevision);
        public static int Watched => entries.Count;
        public static int Pending => end - cursor;
        public static int Revision { get; private set; }
        internal static event Action<MethodInfo> Changed;
        public static long Probes { get; private set; }
        public static double MaximumProbeMilliseconds { get; private set; }

        private static byte[] ManagedBody(MethodInfo method)
        {
            if (!LifeIlReader.HasMetadataBody(method)) return null;
            try
            {
                byte[] body = NativeRuntime.SourceBody(method)?.GetILAsByteArray();
                return body;
            }
            catch (Exception) { return null; }
        }
        private static ulong Fingerprint(byte[] body)
        {
            if (body == null) return 0;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < body.Length; i++) hash = unchecked((hash ^ body[i]) * 1099511628211UL);
            return hash;
        }
        internal static byte[] Read(MethodInfo method, byte[] diskBody)
        {
            method = MethodIdentity.Declaration(method);
            byte[] body = ManagedBody(method);
            if (!known.ContainsKey(method) && LifeResourceEvidence.IsCustom(method.DeclaringType))
            {
                var entry = new Entry { Method = method, Fingerprint = Fingerprint(body) };
                known.Add(method, entry); entries.Add(entry);
            }
            return body ?? diskBody;
        }
        internal static void Request()
        {
            if (Pending > 0) return;
            cursor = 0; end = entries.Count;
        }
        internal static void Update()
        {
            if (Pending <= 0 || lastFrame == UnityEngine.Time.frameCount) return;
            lastFrame = UnityEngine.Time.frameCount;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 8 && cursor < end; i++)
            {
                Entry entry = entries[cursor++];
                ulong fingerprint = Fingerprint(ManagedBody(entry.Method));
                Probes++;
                if (fingerprint != entry.Fingerprint)
                {
                    entry.Fingerprint = fingerprint; Revision++;
                    Changed?.Invoke(entry.Method);
                    LifeAnalysisCoordinator.Shared?.Invalidate(entry.Method);
                    LifeAvailabilityGuard.QueueRefresh(entry.Method);
                }
                if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= .5) break;
            }
            MaximumProbeMilliseconds = Math.Max(MaximumProbeMilliseconds, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }
    }
}
