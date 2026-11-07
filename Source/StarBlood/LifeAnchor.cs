using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.StarBlood
{
    internal sealed class LifeAnchor
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly AccessTools.FieldRef<Thing, IntVec3> position = AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, PawnHealthState> healthState = AccessTools.FieldRefAccess<Pawn_HealthTracker, PawnHealthState>("healthState");
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthOwner = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Verse.AI.Pawn_MindState, bool> active = AccessTools.FieldRefAccess<Verse.AI.Pawn_MindState, bool>("activeInt");
        private readonly bool activeOnMap;
        private readonly PawnRuntimeComponents components;
        private readonly PawnLifeStage lifeStage;
        private readonly ThingDef definition;
        private readonly PawnKindDef kind;
        private readonly int identity;
        private readonly HediffSet hediffs;
        private readonly PawnHealthCollection healthCollection;
        internal IEnumerable<object> OwnedStructures => components.Owners;
        internal readonly StarBloodSubject Subject;
        internal Pawn Pawn => Subject.Pawn;
        internal readonly Pawn_HealthTracker Health;
        internal HediffSet Hediffs => hediffs;
        internal Map Map { get; private set; }
        private IntVec3 cell;
        private ThingOwner holder;
        private int nextRegistrationCheck;
        private int nextWarning;
        internal bool Dirty;
        internal bool StructureIntact => Pawn.def == definition && Pawn.kindDef == kind && Pawn.thingIDNumber == identity
            && Pawn.health == Health && Health.hediffSet == hediffs && healthOwner(Health) == Pawn && hediffs.pawn == Pawn
            && components.Intact(Pawn) && healthCollection.Intact && lifeStage.Intact;

        internal LifeAnchor(StarBloodSubject subject)
        {
            Subject = subject;
            definition = Pawn.def; kind = Pawn.kindDef; identity = Pawn.thingIDNumber;
            Health = Pawn.health; hediffs = Health.hediffSet;
            components = new PawnRuntimeComponents(Pawn);
            lifeStage = new PawnLifeStage(Pawn);
            healthCollection = new PawnHealthCollection(Pawn, hediffs);
            activeOnMap = Pawn.mindState != null && active(Pawn.mindState);
            AcceptLocation();
        }
        internal void AcceptLocation()
        {
            int index = mapIndex(Pawn);
            Map current = index >= 0 && index < Find.Maps.Count ? Find.Maps[index] : null;
            if (current != null && position(Pawn).InBounds(current))
            { Map = current; cell = position(Pawn); holder = Pawn.holdingOwner; }
            else if (OperationScope.Transferring || OperationScope.Rebirthing || MapTransfer.InHandoff(Pawn))
            { Map = null; holder = Pawn.holdingOwner; cell = position(Pawn); }
        }
        internal void PrepareArrival()
        {
            // Spawn callbacks must initialize the same tracker instances that continuity retains.
            components.Restore(Pawn);
        }
        internal bool Rejects(FieldInfo field, object target, object value)
        {
            if (lifeStage.Rejects(field, target, value)) return true;
            if (components.Rejects(field, target, value)) return true;
            if (Map != null && activeOnMap && ReferenceEquals(target, Pawn.mindState) && field.Name == "activeInt")
                return !(value is bool enabled) || !enabled;
            if (ReferenceEquals(target, Pawn))
            {
                if (PawnRuntimeComponents.IsComponentRoot(field)) return !ReferenceEquals(value, Pawn.AllComps);
                if (field.Name == nameof(Thing.def)) return !ReferenceEquals(value, definition);
                if (field.Name == nameof(Pawn.kindDef)) return !ReferenceEquals(value, kind);
                if (field.Name == nameof(Thing.thingIDNumber)) return !Equals(value, identity);
                if (components.Rejects(field, value))
                    return value != null || !LifecycleComponentRelease.ReleasesOnDeath(field) || !LethalDeath();
                if (field.Name == nameof(Pawn.health)) return !ReferenceEquals(value, Health);
                if (field.Name == "positionInt" && Map != null) return !(value is IntVec3 next) || !next.InBounds(Map);
                if (field.Name == nameof(Thing.holdingOwner)) return !ReferenceEquals(value, holder) && !WoundEvidence.Lethal(Pawn);
                if (field.Name == "mapIndexOrState") return (Convert.ToInt32(value) < -1
                    || Map != null && Convert.ToInt32(value) != Find.Maps.IndexOf(Map)) && !WoundEvidence.Lethal(Pawn);
            }
            if (ReferenceEquals(target, Health))
            {
                if (field.Name == "pawn") return !ReferenceEquals(value, Pawn);
                if (field.Name == nameof(Pawn_HealthTracker.hediffSet)) return !ReferenceEquals(value, hediffs);
            }
            return ReferenceEquals(target, hediffs) && (field.Name == nameof(HediffSet.pawn) && !ReferenceEquals(value, Pawn)
                || field.Name == nameof(HediffSet.hediffs) && value == null);
        }
        internal void Update()
        {
            if (!Subject.ContinuityEnabled || PawnContinuity.Authorized(Pawn) || LethalDeath()) return;
            bool changed = Pawn.def != definition || Pawn.kindDef != kind || Pawn.thingIDNumber != identity || Pawn.health != Health;
            PawnContinuity.Repairing = true;
            try
            {
#if MAINTENANCE_LAB
                if (StructureIntact)
                {
                    healthCollection.AcceptCurrent();
                    lifeStage.Update();
                }
                else
                {
#endif
                Pawn.def = definition; Pawn.kindDef = kind; Pawn.thingIDNumber = identity; Pawn.health = Health;
                if (!components.Intact(Pawn)) { components.Restore(Pawn); changed = true; }
                if (lifeStage.Update()) changed = true;
                if (healthOwner(Health) != Pawn) { healthOwner(Health) = Pawn; changed = true; }
                if (Health.hediffSet != hediffs) { Health.hediffSet = hediffs; changed = true; }
                if (hediffs.pawn != Pawn) { hediffs.pawn = Pawn; changed = true; }
                if (healthCollection.Restore()) { changed = true; hediffs.DirtyCache(); }
#if MAINTENANCE_LAB
                }
#endif
                if (changed) { PawnContinuity.RestoredStructures++; Dirty = true; }
                bool restoreActivity = Map != null && activeOnMap && !active(Pawn.mindState);
                bool restoreDeath = healthState(Health) == PawnHealthState.Dead;
                bool inspectedRegistration = false;
                if (!restoreActivity && !restoreDeath)
                {
                    if (MapTransfer.InHandoff(Pawn) || Map == null || !Find.Maps.Contains(Map)) return;
                    if (!Dirty && Subject.Now < nextRegistrationCheck) return;
                    PawnLordControl.Reconcile(Pawn);
                    inspectedRegistration = true;
                    if (PawnMapPresence.Placed(Pawn, Map) && PawnMapPresence.Complete(Pawn, Map))
                    {
                        nextRegistrationCheck = Subject.Now + 30;
                        cell = position(Pawn);
                        Dirty = false;
                        return;
                    }
                }
                if (WoundEvidence.Lethal(Pawn)) return;
                using var admission = LihuatGame.Current.Admission.Recover(Pawn);
                if (Map != null && activeOnMap && !active(Pawn.mindState)) { active(Pawn.mindState) = true; Dirty = true; }
                if (healthState(Health) == PawnHealthState.Dead)
                {
                    healthState(Health) = PawnHealthState.Mobile;
                    Health.isBeingKilled = false;
                    hediffs.DirtyCache();
                    PawnContinuity.RestoredDeathState++; Dirty = true;
                }
                if (MapTransfer.InHandoff(Pawn) || Map == null || !Find.Maps.Contains(Map)) return;
                if (!Dirty && Subject.Now < nextRegistrationCheck) return;
                nextRegistrationCheck = Subject.Now + 30;
                Dirty = false;
                if (!inspectedRegistration) PawnLordControl.Reconcile(Pawn);
                bool registered = PawnMapPresence.Placed(Pawn, Map);
                if (registered)
                {
                    cell = position(Pawn);
                    if (!PawnMapPresence.Complete(Pawn, Map))
                    {
                        Dirty = !PawnMapPresence.Restore(Pawn, Map);
                        if (!Dirty) { LihuatGame.Current.Lifecycle.Registered(Pawn); PawnContinuity.RestoredRegistration++; }
                    }
                    return;
                }
                if (!position(Pawn).InBounds(Map)) position(Pawn) = cell;
                using (new OperationScope(OperationKind.Transfer))
                {
                    mapIndex(Pawn) = -1;
                    Pawn.markedForDiscard = false;
                    MapTransfer.Place(Pawn, Map, cell, Pawn.Rotation);
                }
                holder = Pawn.holdingOwner;
                if (!PawnMapPresence.Restore(Pawn, Map)) Dirty = true;
                if (!Dirty) LihuatGame.Current.Lifecycle.Registered(Pawn);
                Pawn.markedForDiscard = false;
                Health.CheckForStateChange(null, null);
                PawnContinuity.RestoredRegistration++;
            }
            catch (Exception error)
            {
                if (Subject.Now >= nextWarning)
                {
                    nextWarning = Subject.Now + 3600;
                    Log.Warning("[LihuatLib.StarBlood] Continuity repair incomplete for pawn " + identity + ": " + error.GetType().Name);
                }
            }
            finally { PawnContinuity.Repairing = false; }
        }

        private bool LethalDeath() => PawnLife.Dead(Pawn) && WoundEvidence.Lethal(Pawn);
    }
}
