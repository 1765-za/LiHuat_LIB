using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.StarBlood;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class LifeRetirementGuard
    {
        private sealed class Entry { internal bool Active; internal MethodInfo Method; internal int Recipient; internal LifeRetirementTarget Target; }
        private static readonly HashSet<Assembly> examined = new HashSet<Assembly>();
        private static readonly Queue<MethodInfo> pending = new Queue<MethodInfo>();
        private static readonly HashSet<MethodInfo> queued = new HashSet<MethodInfo>();
        private static readonly Dictionary<Assembly, HashSet<MethodInfo>> candidates = new();
        private static readonly ConcurrentDictionary<MethodBase, int> guarded = new();
        private static readonly Dictionary<MethodBase, int> installed = new Dictionary<MethodBase, int>();
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly MethodInfo gate = typeof(LifeRetirementGuard).GetMethod(nameof(Allows), BindingFlags.Static | BindingFlags.NonPublic);
        private static int lastFrame = -1;
        private static int lastPublication = -1;
        private static bool pendingChanged;
        public static int Installed => guarded.Count;
        public static int Pending => pending.Count;
        public static int Unreadable { get; private set; }
        public static int Rejected { get; private set; }
        public static long Prevented { get; private set; }
        public static MethodInfo LastPreventedMethod { get; private set; }
        private static long reflectedCalls;
        public static long ReflectedCalls => System.Threading.Interlocked.Read(ref reflectedCalls);
        public static MethodInfo LastReflectedMethod { get; private set; }

        internal static IEnumerable<MethodInfo> Candidates(Assembly assembly) => candidates.TryGetValue(assembly, out HashSet<MethodInfo> methods)
            ? (IEnumerable<MethodInfo>)methods : Array.Empty<MethodInfo>();

        internal static void Discover()
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            if (!LibraryCapabilities.Has(LibraryCapability.StarBlood)) return;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || !examined.Add(assembly)
                    || InfrastructureAssemblies.Contains(assembly) || assembly == typeof(Pawn).Assembly) continue;
                Type[] types = LifeDiscoveryMetadata.Types(assembly);
                var references = new LifeIlReader();
                var methods = new HashSet<MethodInfo>(); candidates[assembly] = methods;
                foreach (Type type in types)
                {
                    if (type == null || !LifeResourceEvidence.IsCustom(type)) continue;
                    foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            FieldReferenceWriterGuard.Discover(method, types);
                            FieldReferenceGetterGuard.DiscoverWithReader(method, references);
                        }
                        catch (Exception error) when (LifeDiscoveryMetadata.ResolutionFailure(error))
                        {
                            LifeDiscoveryMetadata.Report(method, error);
                            continue;
                        }
                        if (type.IsValueType) continue;
                        if (LifeRetirementRecipient.Candidate(method) && !PatchProvenance.IsReplacement(method))
                        { methods.Add(method); if (queued.Add(method)) { pending.Enqueue(method); pendingChanged = true; } }
                    }
                }
            }
        }
        internal static void Update()
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            if (lastFrame == UnityEngine.Time.frameCount) return;
            lastFrame = UnityEngine.Time.frameCount;
            if (pending.Count == 0 || !pendingChanged && lastPublication == LifeAnalysisCoordinator.Shared.Published) return;
            pendingChanged = false; lastPublication = LifeAnalysisCoordinator.Shared.Published;
            int queued = pending.Count;
            for (int i = 0; i < queued; i++)
                AnalyzeNext();
        }
        internal static void AnalyzeNext()
        {
            MethodInfo method = pending.Dequeue();
            var key = new LifeAnalysisKey(LifeAnalysisKind.Retirement, method);
            LifeAnalysisResult result = LifeAnalysisCoordinator.Shared.Find(key);
            if (result == null)
            {
                if (!LifeAnalysisCoordinator.Shared.Request(key)) { queued.Remove(method); Rejected++; return; }
                pending.Enqueue(method);
                return;
            }
            queued.Remove(method);
            try
            {
                Unreadable += result.Unreadable;
                LifeRetirementTarget target = (LifeRetirementTarget)(result.Retirement & 48);
                if ((result.Retirement & ~48) != 1 || target is not (LifeRetirementTarget.Argument or LifeRetirementTarget.Instance or LifeRetirementTarget.ComponentParent))
                {
                    guarded.TryRemove(method, out _);
                    if (installed.TryGetValue(method, out int inactive)) entries[inactive].Active = false;
                    return;
                }
                int recipient = target == LifeRetirementTarget.Argument ? LifeRetirementRecipient.For(method) : LifeRetirementRecipient.Receiver;
                bool known = installed.TryGetValue(method, out int id);
                if (!known)
                {
                    id = entries.Count;
                    entries.Add(new Entry { Method = method, Recipient = recipient, Target = target });
                }
                if (!known || entries[id].Recipient != recipient)
                {
                    int argument = recipient + (method.IsStatic ? 0 : 1);
                    int tag = id;
                    int? deniedResult = LifeRetirementRecipient.ExceptionArgument(method);
                    NativeIlGuards.RegisterTransform(method, typeof(LifeRetirementGuard).FullName, NativeIlGuards.GateOrder,
                        body => IndependentIl.Guard(method, body, gate, new[] { argument }, tag, deniedResult));
                    installed[method] = id;
                }
                entries[id].Recipient = recipient; entries[id].Target = target;
                entries[id].Active = true;
                guarded[method] = id;
            }
            catch (Exception)
            {
                guarded.TryRemove(method, out _);
                if (installed.TryGetValue(method, out int id)) entries[id].Active = false;
                Rejected++;
            }
        }
        internal static void QueueRefresh(MethodInfo root)
        {
            if (candidates.TryGetValue(root.Module.Assembly, out HashSet<MethodInfo> roots) && roots.Contains(root) && queued.Add(root))
            { pending.Enqueue(root); pendingChanged = true; }
        }
        internal static void ObserveInvocation(MethodInfo method, object instance, object[] arguments)
        {
            if (!guarded.TryGetValue(method, out int id)) return;
            Entry entry = entries[id];
            if (!entry.Active || entry.Recipient >= 0 && (arguments == null || entry.Recipient >= arguments.Length)) return;
            object target = entry.Recipient == LifeRetirementRecipient.Receiver ? instance : arguments[entry.Recipient];
            Pawn pawn = PawnFor(entry, target);
            if (!PawnContinuity.Protects(pawn)) return;
            System.Threading.Interlocked.Increment(ref reflectedCalls);
            LastReflectedMethod = method;
        }

        private static bool Allows(int id, object target)
        {
            if (!entries[id].Active) return true;
            Pawn pawn = PawnFor(entries[id], target);
            if (!PawnContinuity.Protects(pawn)) return true;
            Prevented++;
            LastPreventedMethod = entries[id].Method;
            return false;
        }

        private static Pawn PawnFor(Entry entry, object target) => entry.Target switch
        {
            LifeRetirementTarget.ComponentParent => (target as ThingComp)?.parent as Pawn,
            LifeRetirementTarget.Instance => target as Pawn,
            _ => target as Pawn ?? LihuatGame.Current?.Subjects.Find(target)?.Subject.Pawn
        };
    }
}
