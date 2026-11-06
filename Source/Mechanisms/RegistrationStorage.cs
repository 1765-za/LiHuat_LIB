using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RegistrationStorage
    {
        private static readonly AccessTools.FieldRef<ThingOwner<Thing>, List<Thing>> heldThings = AccessTools.FieldRefAccess<ThingOwner<Thing>, List<Thing>>("innerList");
        private static readonly AccessTools.FieldRef<ThingOwner<Pawn>, List<Pawn>> heldPawns = AccessTools.FieldRefAccess<ThingOwner<Pawn>, List<Pawn>>("innerList");
        private static readonly AccessTools.FieldRef<Selector, List<object>> selected = AccessTools.FieldRefAccess<Selector, List<object>>("selected");
        private static readonly AccessTools.FieldRef<TickList, List<List<Thing>>> buckets = AccessTools.FieldRefAccess<TickList, List<List<Thing>>>("thingLists");
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> registering = AccessTools.FieldRefAccess<TickList, List<Thing>>("thingsToRegister");
        private static readonly AccessTools.FieldRef<TickList, List<Thing>> deregistering = AccessTools.FieldRefAccess<TickList, List<Thing>>("thingsToDeregister");
        private static readonly FieldInfo[] tickLists = typeof(TickManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(f => f.FieldType == typeof(TickList)).ToArray();
        private static readonly AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> alive = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsAlive");
        private static readonly AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> mothballed = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsMothballed");
        private static readonly AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> dead = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsDead");
        private static readonly AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> kept = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsForcefullyKeptAsWorldPawns");

        internal static bool List<T>(List<T> list, T target, bool remove) where T : class
            => remove ? ReferenceListStorage<T>.Remove(list, target) != 0 : ReferenceListStorage<T>.Contains(list, target);
        internal static bool Set<T>(HashSet<T> set, T target, bool remove) where T : class
            => remove ? ReferenceSetStorage<T>.Remove(set, target) : ReferenceSetStorage<T>.Contains(set, target);

        internal static bool Holder(ThingOwner holder, Thing target, bool remove)
        {
            if (holder == null) return false;
            if (holder is ThingOwner<Thing> things) return List(heldThings(things), target, remove);
            if (holder is ThingOwner<Pawn> pawns && target is Pawn pawn) return List(heldPawns(pawns), pawn, remove);
            throw new System.InvalidOperationException("Unsupported pawn holder: " + holder.GetType().FullName);
        }

        internal static bool Process(Thing target, ThingOwner originalHolder, CellRect footprint, bool remove)
        {
            bool found = Holder(originalHolder, target, remove);
            foreach (Map map in Find.Maps) found |= MapRegistrationStorage.Process(map, target, footprint, remove);
            found |= Ticks(target, remove);
            found |= Signals(target, remove);
            if (target is Pawn pawn) found |= World(pawn, remove);
            if (Find.Selector is Selector selector) found |= List(selected(selector), (object)target, remove);
            return found;
        }

        internal static bool Ticks(Thing target, bool remove)
        {
            bool found = false;
            TickManager manager = Find.TickManager;
            if (manager != null)
                foreach (FieldInfo field in tickLists)
                {
                    var ticks = (TickList)field.GetValue(manager);
                    if (ticks == null) continue;
                    found |= List(registering(ticks), target, remove) | List(deregistering(ticks), target, remove);
                    List<List<Thing>> lists = buckets(ticks);
                    List<Thing>[] items = ReferenceListStorage<List<Thing>>.Items(lists);
                    int count = ReferenceListStorage<List<Thing>>.Count(lists);
                    for (int i = 0; i < count; i++)
                    {
                        if (!ReferenceListStorage<Thing>.Contains(items[i], target)) continue;
                        found = true;
                        // Tick may still be iterating the old bucket; preserve its indices.
                        if (remove) items[i] = ReferenceListStorage<Thing>.Without(items[i], target);
                    }
                }
            return found;
        }

        internal static bool World(Pawn pawn, bool remove)
        {
            WorldPawns world = Find.WorldPawns;
            return world != null && (Set(alive(world), pawn, remove) | Set(mothballed(world), pawn, remove)
                | Set(dead(world), pawn, remove) | Set(kept(world), pawn, remove));
        }

        internal static bool Signals(Thing thing, bool remove) => thing is ISignalReceiver receiver
            && Find.SignalManager != null && List(Find.SignalManager.receivers, receiver, remove);
    }
}
