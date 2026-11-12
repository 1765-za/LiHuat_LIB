using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ThingListerStorage
    {
        private static readonly AccessTools.FieldRef<ListerThings, Dictionary<ThingDef, List<Thing>>> definitions = AccessTools.FieldRefAccess<ListerThings, Dictionary<ThingDef, List<Thing>>>("listsByDef");
        private static readonly AccessTools.FieldRef<ListerThings, List<Thing>[]> groups = AccessTools.FieldRefAccess<ListerThings, List<Thing>[]>("listsByGroup");
        private static readonly AccessTools.FieldRef<ListerThings, List<IHaulSource>> sources = AccessTools.FieldRefAccess<ListerThings, List<IHaulSource>>("haulSources");
        private static readonly AccessTools.FieldRef<ListerThings, int[]> hashes = AccessTools.FieldRefAccess<ListerThings, int[]>("stateHashByGroup");
        internal static void RemoveStored(ListerThings lister, Thing target) => Process(lister, target, true, true);

        internal static bool Registered(ListerThings lister, Thing target)
            => ContainsByDefinition(lister, target)
                && ReferenceListStorage<Thing>.Contains(groups(lister)[(int)ThingRequestGroup.Everything], target);

        internal static bool ContainsByDefinition(ListerThings lister, Thing target)
            => definitions(lister).TryGetValue(target.def, out List<Thing> values)
                && ReferenceListStorage<Thing>.Contains(values, target);

        internal static bool Process(ListerThings lister, Thing target, bool remove, bool notify = false)
        {
            if (lister == null) return false;
            bool found = false;
            foreach (List<Thing> list in definitions(lister).Values) found |= RegistrationStorage.List(list, target, remove);
            List<Thing>[] lists = groups(lister);
            int[] versions = hashes(lister);
            for (int i = 0; i < lists.Length; i++)
                if (RegistrationStorage.List(lists[i], target, remove))
                {
                    found = true;
                    if (remove) versions[i]++;
                }
            if (target is IHaulSource source) found |= RegistrationStorage.List(sources(lister), source, remove);
            if (remove && found && notify) ErasureNotifications.Invoke(lister.thingListChangedCallbacks?.onThingRemoved, target);
            return found;
        }
    }
}
