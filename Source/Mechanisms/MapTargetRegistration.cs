using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LihuatLib.Mechanisms
{
    public static class MapTargetRegistration
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<IAttackTarget>> all = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<IAttackTarget>>("allTargets");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, Dictionary<Faction, HashSet<IAttackTarget>>> hostile = AccessTools.FieldRefAccess<AttackTargetsCache, Dictionary<Faction, HashSet<IAttackTarget>>>("targetsHostileToFaction");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> aggressive = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("pawnsInAggroMentalState");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> factionless = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("factionlessHumanlikes");
        public static long RemovedRegistrations { get; private set; }

        internal static bool Registered(Map map, Thing thing) => !(thing is IAttackTarget target)
            || ReferenceSetStorage<IAttackTarget>.Contains(all(map.attackTargetsCache), target);

        public static bool Contains(Map map, Thing thing)
        {
            if (!(thing is IAttackTarget target)) return false;
            AttackTargetsCache cache = map.attackTargetsCache;
            if (ReferenceSetStorage<IAttackTarget>.Contains(all(cache), target)) return true;
            foreach (HashSet<IAttackTarget> set in hostile(cache).Values) if (ReferenceSetStorage<IAttackTarget>.Contains(set, target)) return true;
            return thing is Pawn pawn && (ReferenceSetStorage<Pawn>.Contains(aggressive(cache), pawn) || ReferenceSetStorage<Pawn>.Contains(factionless(cache), pawn));
        }

        public static void Remove(Map map, Thing thing)
        {
            if (!(thing is IAttackTarget target)) return;
            AttackTargetsCache cache = map.attackTargetsCache;
            if (ReferenceSetStorage<IAttackTarget>.Remove(all(cache), target)) RemovedRegistrations++;
            foreach (HashSet<IAttackTarget> set in hostile(cache).Values) if (ReferenceSetStorage<IAttackTarget>.Remove(set, target)) RemovedRegistrations++;
            if (thing is Pawn pawn) { ReferenceSetStorage<Pawn>.Remove(aggressive(cache), pawn); ReferenceSetStorage<Pawn>.Remove(factionless(cache), pawn); }
        }

        private static bool Invalid(Thing thing, Map map)
        {
            if (thing == null) return true;
            int index = mapIndex(thing);
            return index < 0 || index >= Find.Maps.Count || Find.Maps[index] != map || !thing.Position.InBounds(map);
        }

        public static void PruneInvalid(Map map)
        {
            AttackTargetsCache cache = map.attackTargetsCache;
            Predicate<IAttackTarget> invalid = target => Invalid(target?.Thing, map);
            RemovedRegistrations += all(cache).RemoveWhere(invalid);
            foreach (HashSet<IAttackTarget> set in hostile(cache).Values) RemovedRegistrations += set.RemoveWhere(invalid);
            aggressive(cache).RemoveWhere(pawn => Invalid(pawn, map));
            factionless(cache).RemoveWhere(pawn => Invalid(pawn, map));
        }
    }
}
