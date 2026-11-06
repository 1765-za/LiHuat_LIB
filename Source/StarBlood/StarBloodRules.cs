using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using LihuatLib.Core;
using LihuatLib.Mechanisms;

namespace LihuatLib.StarBlood
{
    public sealed class StarBloodRules
    {
        public sealed class Attack : IDisposable
        {
            private readonly StarBloodRules owner;
            internal readonly Pawn Target;
            internal readonly string Source;
            internal readonly LifeStructureSnapshot Before;
            internal readonly StarBloodSubject Record;
            internal bool Effective;
            private bool disposed;
            internal Attack(StarBloodRules owner, Pawn target, Thing source, StarBloodSubject record)
            {
                this.owner = owner; Target = target; Source = source.ThingID; Record = record;
                Before = new LifeStructureSnapshot(target, owner.evidence);
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.End(this);
            }
        }
        private readonly LifeResourceEvidence evidence = new LifeResourceEvidence();
        private readonly HashSet<string> pending = new HashSet<string>();
        private readonly List<Attack> active = new List<Attack>();
        private readonly LifeInjectionWatch injections;
        private readonly LifeCounterWatch counters;
        private readonly LifeDeathQuarantine quarantine;
        public StarBloodSubject Subject { get; private set; }
        internal LifeAnchor Anchor { get; private set; }
        internal int Now => Subject?.Now ?? 0;
        private readonly Action<string> report;
        private readonly Action<Pawn, Type> detached;
        private Pawn markerPawn;
        private Hediff marker;
        private bool repairingMarker;
        private int lastUnblockedLog = int.MinValue;
        private int lastTickFaultLog = int.MinValue;
        private int diagnosticSecond = -1;
        private readonly HashSet<string> reportedThisSecond = new HashSet<string>();
        private string tickCallback;
        public readonly Queue<string> Diagnostics = new Queue<string>();
        public bool Restoring { get; private set; }
        public int UnreadableMethods => evidence.UnreadableMethods;
        public int LimitedAnalyses => evidence.LimitedAnalyses;
        public int CounterSites => LifeExecutionGuard.InstalledSites;
        public int CounterMethodsAnalyzed => counters.Analyzed;
        public int CounterMethodsPending => counters.Pending;
        public int CounterMethodsRejected => LifeExecutionGuard.RejectedMethods;
        public int DeferredSites => LifeDeferredGuard.InstalledSites;
        public int CallbackMethodsAnalyzed => evidence.CallbackMethods;
        public StarBloodRules()
        {
            injections = new LifeInjectionWatch(evidence); counters = new LifeCounterWatch(evidence, () => Now);
            quarantine = new LifeDeathQuarantine(() => Now);
            report = Report; detached = quarantine.Add;
        }

        private StarBloodSubject FindSubject(Pawn pawn) => pawn != null && pawn == Subject?.Pawn ? Subject : null;

        public static bool Enabled(StarBloodSubject record) => record?.Enabled == true;

        public void Initialize(StarBloodSubject subject)
        {
            if (subject != null) LibraryCapabilities.Require(LibraryCapability.StarBlood);
            if (Subject != null) StarBloodRegistry.Remove(Subject.Pawn, this);
            Subject = subject;
            Anchor = null;
            tickCallback = null;
            lastTickFaultLog = int.MinValue;
            Pawn pawn = subject?.Pawn;
            if (pawn == null || !Configuration.HighMechanismPolicy.Maintenance) return;
            Anchor = new LifeAnchor(subject);
            StarBloodRegistry.Register(this);
            LifeRetirementGuard.Discover();
            EnsureMarker(pawn);
            if (Configuration.HighMechanismPolicy.Scanning)
            { injections.Initialize(pawn); counters.Initialize(pawn); }
        }
        public void Update()
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.Maintenance) return;
            PawnContinuity.Update(Subject?.Pawn);
            if (Configuration.HighMechanismPolicy.Scanning) counters.Update(Subject?.Pawn);
        }
        public void PrepareTick() => PawnContinuity.PrepareTick(Subject?.Pawn);
        internal void EnterTickCallback(Pawn pawn, string callback)
        {
            if (Subject?.Pawn == pawn) tickCallback = callback;
        }
        internal void ReturnTickCallback(Pawn pawn)
        {
            if (Subject?.Pawn != pawn) return;
            long before = PawnContinuity.RestoredStructures;
            PrepareTick();
            if (PawnContinuity.RestoredStructures != before) Report("Tick runtime restored after=" + tickCallback + " pawnId=" + pawn.thingIDNumber);
            tickCallback = null;
        }
        internal void TraceTickFault(Pawn pawn, Exception error, Pawn_DrawTracker drawing)
        {
            string message = "Tick fault callback=" + (tickCallback ?? "before-body") + " pawnId=" + pawn.thingIDNumber
                + " error=" + error.GetType().Name + " health=" + (pawn.health != null)
                + " pather=" + (pawn.pather != null) + " verbs=" + (pawn.verbTracker != null)
                + " natives=" + (pawn.natives != null) + " stances=" + (pawn.stances != null)
                + " needs=" + (pawn.needs != null) + " mind=" + (pawn.mindState != null)
                + " jobs=" + (pawn.jobs != null) + " renderer=" + (drawing?.renderer != null);
            Report(message);
            if (Prefs.DevMode) return;
            int now = Now;
            if (lastTickFaultLog != int.MinValue && now >= lastTickFaultLog && now - lastTickFaultLog < 600) return;
            lastTickFaultLog = now;
            Log.Warning("[LihuatLib.StarBlood] " + message);
        }
        internal void AcceptRestoration()
        {
            if (Subject == null || !Configuration.HighMechanismPolicy.Maintenance) return;
            StarBloodRegistry.Remove(Subject.Pawn, this);
            Subject.State.DeveloperDeathPawnId = null;
            Anchor = new LifeAnchor(Subject);
            StarBloodRegistry.Register(this);
        }

        internal void AcceptHealthExchange()
        {
            if (!Configuration.HighMechanismPolicy.Maintenance) return;
            AcceptRestoration();
            marker = Subject.Pawn.health.hediffSet.GetFirstHediffOfDef(Subject.MarkerDef);
            markerPawn = Subject.Pawn;
            if (Configuration.HighMechanismPolicy.Scanning) injections.Initialize(Subject.Pawn);
        }

        public void BindDeferred(Delegate work, Pawn owner)
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            evidence.BindDeferred(work, owner,
            Enabled(FindSubject(owner)) && !OperationScope.Terminates(owner) && !OperationScope.Transferring
                && !OperationScope.Rebirthing && !OperationScope.MedicalFor(owner));
        }

        public void EnsureMarker(Pawn pawn)
        {
            if (!Configuration.HighMechanismPolicy.Maintenance || repairingMarker || pawn?.health?.hediffSet == null || FindSubject(pawn) == null
                || Subject?.State.DeveloperRemovedMarker == true || OperationScope.Terminates(pawn) || PawnLife.Dead(pawn)) return;
            repairingMarker = true;
            try
            {
                bool changed = false;
                if (markerPawn != pawn)
                {
                    markerPawn = pawn;
                    marker = pawn.health.hediffSet.GetFirstHediffOfDef(Subject.MarkerDef);
                }
                if (marker == null) marker = HediffMaker.MakeHediff(Subject.MarkerDef, pawn);
                if (marker.pawn != pawn || marker.def != Subject.MarkerDef)
                { marker.pawn = pawn; marker.def = Subject.MarkerDef; changed = true; }
                List<Hediff> all = pawn.health.hediffSet.hediffs;
                if (!all.Contains(marker))
                {
                    for (int i = all.Count - 1; i >= 0; i--) if (all[i].def == Subject.MarkerDef) all.RemoveAt(i);
                    all.Add(marker); changed = true;
                }
                if (changed) pawn.health.hediffSet.DirtyCache();
            }
            finally { repairingMarker = false; }
        }

        public bool BlockFieldWrite(System.Reflection.FieldInfo field, object target) => Scribe.mode != LoadSaveMode.LoadingVars
            && (target == this || target == marker && marker != null || target == Subject);

        public void TraceUnblocked(Pawn pawn, string entry)
        {
            if (entry != "Kill" && entry != "SetDead" && entry != "Destroy" && entry != "Discard") return;
            int now = Now;
            if (lastUnblockedLog != int.MinValue && now - lastUnblockedLog < 600) return;
            lastUnblockedLog = now;
            var chain = new System.Text.StringBuilder();
            System.Diagnostics.StackFrame[] frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
            if (frames != null)
                for (int i = 0; i < Math.Min(10, frames.Length); i++)
                {
                    System.Reflection.MethodBase method = frames[i].GetMethod();
                    if (i > 0) chain.Append(" <- ");
                    chain.Append(method?.DeclaringType?.FullName).Append('.').Append(method?.Name);
                }
            Log.Warning("[LihuatLib.StarBlood] Unblocked destructive entry=" + entry + " pawnId=" + pawn.thingIDNumber
                + " marker=" + pawn.health.hediffSet.HasHediff(Subject.MarkerDef)
                + " sites=" + CounterSites + " pending=" + CounterMethodsPending + " rejected=" + CounterMethodsRejected
                + " limited=" + LimitedAnalyses
                + " deferredSites=" + DeferredSites + " callbacks=" + CallbackMethodsAnalyzed
                + " build=" + typeof(StarBloodRules).Assembly.ManifestModule.ModuleVersionId + " chain=" + chain);
        }

        public int Observe(Pawn target, bool force = false)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.ScannedProtection) return 0;
            StarBloodSubject record = FindSubject(target);
            if (Restoring || record == null || (!force && active.Count > 0)) return 0;
            Restoring = true;
            try { return injections.Inspect(record, force, report, detached); }
            finally { Restoring = false; }
        }

        public Attack Begin(Pawn target, Thing source, bool validHit)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.ScannedProtection) return null;
            StarBloodSubject record = FindSubject(target);
            if (!validHit || !Enabled(record) || OperationScope.MedicalFor(target) || Restoring || source == null
                || source == target || !source.HostileTo(target) || record.State.InspectedSources.Contains(source.ThingID)
                || !pending.Add(source.ThingID)) return null;
            EnsureMarker(target);
            Observe(target, true);
            Attack attack = new Attack(this, target, source, record);
            active.Add(attack);
            return attack;
        }

        public void MarkEffective(Pawn target)
        {
            foreach (Attack attack in active) if (attack.Target == target) attack.Effective = true;
        }

        public bool BeforeDeath(Pawn target)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.ScannedProtection) return false;
            if (Restoring || OperationScope.Terminates(target) || OperationScope.MedicalFor(target)
                || !Enabled(FindSubject(target))) return false;
            if (WoundEvidence.Lethal(target)) return false;
            EnsureMarker(target);
            foreach (Attack attack in active)
            {
                if (attack.Target != target || !Enabled(attack.Record)) continue;
                int stripped = Inspect(attack);
                if (stripped > 0) { attack.Effective = true; return true; }
            }
            return Observe(target, true) > 0 || quarantine.ContainsCaller(target);
        }

        private void End(Attack attack)
        {
            active.Remove(attack);
            pending.Remove(attack.Source);
            if (!attack.Effective || !Enabled(attack.Record)) return;
            attack.Record.State.InspectedSources.Add(attack.Source);
            attack.Record.State.Inspections++;
            Inspect(attack);
        }

        private int Inspect(Attack attack)
        {
            Restoring = true;
            try
            {
                LifeStructureSnapshot after = new LifeStructureSnapshot(attack.Target, evidence);
                int stripped = after.StripIntroduced(attack.Before, report, detached: detached);
                int remaining = stripped == 0 ? 0 : new LifeStructureSnapshot(attack.Target, evidence).RemainingRejected(after);
                attack.Record.State.StrippedStructures += stripped - remaining;
                return stripped;
            }
            finally { Restoring = false; }
        }

        private void Report(string message)
        {
            int second = Now / 60;
            if (second != diagnosticSecond) { diagnosticSecond = second; reportedThisSecond.Clear(); }
            if (reportedThisSecond.Count >= 64 || !reportedThisSecond.Add(message)) return;
            if (Diagnostics.Count >= 24) Diagnostics.Dequeue();
            Diagnostics.Enqueue(message);
            if (Prefs.DevMode) Log.Message("[LihuatLib.StarBlood] " + message);
        }
    }
}
