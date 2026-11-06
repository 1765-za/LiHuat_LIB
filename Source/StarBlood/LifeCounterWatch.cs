using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using LihuatLib.Core;
using LihuatLib.Mechanisms;

namespace LihuatLib.StarBlood
{
    internal sealed class LifeCounterWatch
    {
        private readonly LifeResourceEvidence evidence;
        private readonly HashSet<Type> componentTypes = new HashSet<Type>();
        private readonly HashSet<MethodInfo> seen = new HashSet<MethodInfo>();
        private readonly Queue<MethodInfo> pending = new Queue<MethodInfo>();
        private MethodInfo[] damageEntries;
        private Map map;
        private int nextDiscovery;
        private int lastFrame = -1;
        private int auditFrame = -1;
        private int assemblyRevision;
        private int lastPublication = -1;
        private bool pendingChanged;
        public int Analyzed { get; private set; }
        public int Pending => pending.Count;
        private readonly Func<int> now;
        public LifeCounterWatch(LifeResourceEvidence evidence, Func<int> now)
        { this.evidence = evidence; this.now = now; }

        public void Initialize(Pawn pawn)
        {
            Discover(pawn);
            auditFrame = UnityEngine.Time.frameCount + 120;
            assemblyRevision = LifeMethodRevisions.AssemblyRevision;
            while (pending.Count > 0 && LifeResourceEvidence.CounterPrepared(pending.Peek())) AnalyzeNext();
        }

        internal void DiscoverLoadedTypes()
        {
            foreach (Type type in GenTypes.AllTypes)
                if (typeof(GameComponent).IsAssignableFrom(type) || typeof(MapComponent).IsAssignableFrom(type)
                    || typeof(RimWorld.Planet.WorldComponent).IsAssignableFrom(type)) Component(type);
            DiscoverDamageEntries();
            foreach (Type type in typeof(DamageWorker).AllSubclassesNonAbstract())
                DamageEntry(LifeAnalysisCoordinator.Shared.DamageEntries.Worker(type));
        }

        public void Update(Pawn pawn)
        {
            if (pawn == null || lastFrame == UnityEngine.Time.frameCount) return;
            lastFrame = UnityEngine.Time.frameCount;
            bool newAssemblies = assemblyRevision != LifeMethodRevisions.AssemblyRevision;
            if (newAssemblies)
            {
                assemblyRevision = LifeMethodRevisions.AssemblyRevision;
                LifeRetirementGuard.Discover();
                LifeAvailabilityGuard.Discover();
                Discover(pawn);
            }
            if (map != pawn.MapHeld || newAssemblies) auditFrame = UnityEngine.Time.frameCount + 120;
            if (map != pawn.MapHeld || now() >= nextDiscovery) Discover(pawn);
            if (!StarBloodScanPolicy.CacheOnly)
            {
                if (auditFrame >= 0 && UnityEngine.Time.frameCount >= auditFrame) { LifeMethodRevisions.Request(); auditFrame = -1; }
                LifeMethodRevisions.Update();
            }
            if (pending.Count != 0 && (pendingChanged || lastPublication != LifeAnalysisCoordinator.Shared.Published))
            {
                pendingChanged = false; lastPublication = LifeAnalysisCoordinator.Shared.Published;
                int queued = pending.Count;
                for (int i = 0; i < queued; i++) AnalyzeNext();
            }
        }

        private void Discover(Pawn pawn)
        {
            map = pawn?.MapHeld;
            nextDiscovery = now() + 600;
            if (Current.Game != null) foreach (GameComponent component in Current.Game.components) Component(component.GetType());
            if (Find.World != null) foreach (RimWorld.Planet.WorldComponent component in Find.World.components) Component(component.GetType());
            if (map != null) foreach (MapComponent component in map.components) Component(component.GetType());
            DiscoverDamageEntries();
        }

        private void DiscoverDamageEntries()
        {
            MethodInfo[] current = LifeAnalysisCoordinator.Shared.DamageEntries.CaptureCandidates();
            if (ReferenceEquals(current, damageEntries)) return;
            damageEntries = current;
            foreach (MethodInfo method in current) Enqueue(method);
        }

        private void Component(Type type)
        {
            if (!LifeResourceEvidence.IsCustom(type) || !componentTypes.Add(type)) return;
            foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.IsVirtual && (method.Name.EndsWith("ComponentTick", StringComparison.Ordinal)
                    || method.Name.EndsWith("ComponentUpdate", StringComparison.Ordinal))) Enqueue(method);
        }

        private void DamageEntry(MethodInfo method)
        {
            if (method == null) return;
            foreach (MethodInfo candidate in LifeAnalysisCoordinator.Shared.DamageEntries.Read(method)) Enqueue(candidate);
        }

        private void Enqueue(MethodInfo method)
        {
            if (method == null || !LifeResourceEvidence.IsCustom(method.DeclaringType) || method.IsAbstract
                || method.ContainsGenericParameters || !seen.Add(method)) return;
            pending.Enqueue(method);
            pendingChanged = true;
        }

        internal void AnalyzeNext()
        {
            MethodInfo method = pending.Dequeue();
            if (!evidence.TryInspectCounterMethod(method, out MethodInfo[] methods)) { pending.Enqueue(method); return; }
            foreach (MethodInfo nested in methods) Enqueue(nested);
            Analyzed++;
        }
    }
}
