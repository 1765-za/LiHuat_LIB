using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.Mechanisms;
using UnityEngine;
using Verse;

namespace LihuatLib.Domains
{
    public static class DomainShellRules
    {
        public const int Capacity = 500000;
        public const int RecoveryPerSecond = 10000;
        public static bool Coexists(BuildableDef first, BuildableDef second) => IsShell(first) || IsShell(second);
        private static bool IsShell(BuildableDef def) => def is ThingDef thing && typeof(Building_DomainShell).IsAssignableFrom(thing.thingClass);
        public static DomainArea At(Map map, IntVec3 cell) => LihuatGame.Current?.Domains.ShellAt(map, cell);
        public static bool Blocks(Map map, IntVec3 cell) => At(map, cell) != null;
        public static bool Conceals(Thing thing) => !(thing is Building_DomainShell) && thing?.Spawned == true && Blocks(thing.Map, thing.Position);
        public static bool AllowsMove(Thing thing, IntVec3 cell) => OperationScope.Transferring || !(thing is Pawn) || !thing.Spawned || !Blocks(thing.Map, cell);
        public static bool PlaceArrival(Thing thing, Map map, ref IntVec3 cell)
        {
            if (!(thing is Pawn) && (!(thing is IThingHolder) || thing is Corpse) || !Blocks(map, cell)) return true;
            IntVec3 outside = Exterior(map, cell);
            if (!outside.IsValid) return false;
            cell = outside; return true;
        }
        public static void Repel(Pawn pawn)
        {
            if (OperationScope.Transferring || pawn?.Spawned != true || !Blocks(pawn.Map, pawn.Position)) return;
            IntVec3 outside = Exterior(pawn.Map, pawn.Position);
            if (!outside.IsValid) return;
            pawn.Position = outside; pawn.Notify_Teleported();
        }
        private static IntVec3 Exterior(Map map, IntVec3 near)
        {
            DomainArea area = At(map, near);
            if (area == null) return IntVec3.Invalid;
            IntVec3 best = IntVec3.Invalid; int distance = int.MaxValue;
            for (int ring = 1; ring <= 8; ring++)
            {
                foreach (IntVec3 cell in area.SourceFootprint.ExpandedBy(ring).EdgeCells)
                {
                    if (!cell.InBounds(map) || !cell.Standable(map) || Blocks(map, cell)) continue;
                    int candidate = (cell - near).LengthHorizontalSquared;
                    if (candidate >= distance) continue;
                    best = cell; distance = candidate;
                }
                if (best.IsValid) break;
            }
            return best;
        }
        public static bool Create(DomainArea area, Map map, IntVec3 origin, ThingDef def)
        {
            int half = area.Size / 2;
            if (map == null || map.Size.x < area.Size || map.Size.z < area.Size) return false;
            IntVec3 position = new IntVec3(Mathf.Clamp(origin.x, half, map.Size.x - half - 1), 0, Mathf.Clamp(origin.z, half, map.Size.z - half - 1));
            var shell = (Building_DomainShell)ThingMaker.MakeThing(def);
            shell.DomainId = area.Id; shell.SetFaction(area.Owner.Faction);
            area.Anchor = position;
            if (area.ShellCapacity <= 0) area.ShellCapacity = area.Profile.ShellCapacity(area.Size);
            shell.Capacity = area.ShellCapacity; shell.Width = area.Size;
            try
            {
                DomainShellPlacement.Place(shell, map, position);
                if (!shell.Spawned || shell.Destroyed) throw new InvalidOperationException("Domain shell did not spawn.");
                shell.HitPoints = area.ShellCapacity;
                area.Shell = shell; area.ShellBound = true; area.LastShellRecoveryAt = Find.TickManager.TicksGame;
                LihuatGame.Current.Domains.IndexShell(area);
                RepelOccupants(area, map);
                return true;
            }
            catch (Exception ex)
            {
                if (!shell.Destroyed) shell.Destroy();
                Log.Error("[LihuatLib.Domain] Shell creation failed: " + ex); return false;
            }
        }
        private static void RepelOccupants(DomainArea area, Map map)
        {
            var occupants = new HashSet<Pawn>(Core.ReferenceIdentity<Pawn>.Instance);
            foreach (IntVec3 cell in area.SourceFootprint)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Pawn pawn && pawn.Map == map && pawn.Spawned && !pawn.Destroyed
                        && !PawnLife.Dead(pawn) && pawn.pather != null) occupants.Add(pawn);
            }
            foreach (Pawn pawn in occupants) Repel(pawn);
        }
        internal static void Tick(DomainArea area, int now)
        {
            if (area.Ending || !area.ShellBound) return;
            Thing shell = area.Shell;
            if (shell?.Spawned != true || shell.Destroyed || shell.HitPoints <= 0)
            { LihuatGame.Current.Domains.Collapse(area.Id, "shell-broken"); return; }
            int seconds = (now - area.LastShellRecoveryAt) / 60;
            if (seconds <= 0) return;
            area.LastShellRecoveryAt += seconds * 60;
            double recovered = Math.Round(seconds * (double)area.ShellCapacity * area.Profile.shellRecoveryPerSecond);
            shell.HitPoints = (int)Math.Min(area.ShellCapacity, shell.HitPoints + recovered);
        }
        public static void Remove(DomainArea area)
        {
            if (area.Shell?.Destroyed == false) area.Shell.Destroy();
        }
    }
}
