using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.StarBlood
{
    public static class PawnContinuity
    {
        [ThreadStatic] internal static bool Repairing;
        [ThreadStatic] private static Pawn moving;
        internal static bool IsMoving(Thing thing) => ReferenceEquals(moving, thing);
        public static long BlockedWrites { get; private set; }
        public static long BlockedRemoval { get; private set; }
        public static long RestoredStructures { get; internal set; }
        public static long RestoredRegistration { get; internal set; }
        public static long RestoredDeathState { get; internal set; }

        internal static LifeAnchor Find(Pawn pawn) => StarBloodRegistry.For(pawn)?.Anchor;
        internal static bool Authorized(Pawn pawn) => Repairing || DeveloperExecution.Allows(pawn)
            || OperationScope.Transferring || OperationScope.Rebirthing || OperationScope.Terminates(pawn);
        public static bool Protects(Pawn pawn)
        {
            LifeAnchor anchor = Find(pawn);
            return anchor?.Subject.ContinuityEnabled == true && !Authorized(pawn) && !WoundEvidence.Lethal(pawn);
        }
        internal static bool TracksMovement(Pawn pawn)
        {
            LifeAnchor anchor = Find(pawn);
            return anchor?.Subject.ContinuityEnabled == true && !Authorized(pawn)
                && (!PawnLife.Dead(pawn) || !WoundEvidence.Lethal(pawn));
        }
        public static bool BlocksRemoval(Thing thing, bool movementAllowed = false)
        {
            Pawn pawn = thing as Pawn;
            if (movementAllowed && moving == pawn || MapTransfer.InHandoff(thing) || !Protects(pawn)) return false;
            Find(pawn).Dirty = true;
            BlockedRemoval++;
            return true;
        }
        public static bool BlocksWrite(FieldInfo field, object target, object value)
        {
            if (Repairing || field == null || target == null) return false;
            LifeAnchor anchor = LihuatGame.Current?.Subjects.Find(target)?.Anchor;
            if (anchor == null || !anchor.Subject.ContinuityEnabled || Authorized(anchor.Pawn)) return false;
            if (MapTransfer.HandoffAllowsWrite(anchor.Pawn, field, target, value)) return false;
            if (!anchor.Rejects(field, target, value)) return false;
            anchor.Dirty = true;
            BlockedWrites++;
            return true;
        }
        internal static void Update(Pawn pawn)
        {
            if (!Configuration.HighMechanismPolicy.Maintenance) return;
            LifeAnchor anchor = Find(pawn);
            if (anchor == null) return;
            anchor.Update();
            LifeRetirementGuard.Update();
        }
        internal static void PrepareTick(Pawn pawn)
        {
            if (!Configuration.HighMechanismPolicy.Maintenance) return;
            LifeAnchor anchor = Find(pawn);
            if (anchor != null && !anchor.StructureIntact) anchor.Update();
        }
        internal static void AcceptTransfers()
        {
            if (!Repairing) LihuatGame.Current?.Subjects.AcceptTransfers();
        }
        internal static void PrepareArrival(Pawn pawn)
        {
            if (pawn.Spawned || !OperationScope.Transferring && !OperationScope.Rebirthing && !MapTransfer.InHandoff(pawn))
                throw new InvalidOperationException("Arrival requires a detached pawn in a lifecycle transfer.");
            Find(pawn)?.PrepareArrival();
        }
        internal static void AcceptHandoff(Pawn pawn) => Find(pawn)?.AcceptLocation();
        public readonly struct MoveScope : IDisposable
        {
            private readonly Pawn pawn;
            private readonly Pawn previous;
            internal MoveScope(Pawn pawn) { this.pawn = pawn; previous = moving; moving = pawn; }
            public void Dispose()
            {
                if (pawn == null) return;
                moving = previous;
                Find(pawn)?.AcceptLocation();
            }
        }
        public static bool BeginMove(Thing thing, IntVec3 cell, out MoveScope scope)
        {
            scope = default;
            Pawn pawn = thing as Pawn;
            LifeAnchor anchor = Find(pawn);
            if (LihuatGame.Current?.Domains.AllowsCell(thing, cell) == false) return false;
            if (MapTransfer.InHandoff(pawn))
            {
                if (!MapTransfer.HandoffAllowsCell(pawn, cell)) return false;
                scope = new MoveScope(pawn); return true;
            }
            if (anchor == null || !anchor.Subject.ContinuityEnabled || Authorized(pawn))
            {
                if (LihuatGame.Current?.Domains.Contains(pawn) == true) scope = new MoveScope(pawn);
                return true;
            }
            if (anchor.Map != null && !cell.InBounds(anchor.Map)) { anchor.Dirty = true; BlockedWrites++; return false; }
            scope = new MoveScope(pawn);
            return true;
        }
    }
}
