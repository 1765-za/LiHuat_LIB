using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class ObservedSpawn
    {
        private sealed class Notifications
        {
            internal SpawnNotificationEmitter.Notification Before;
            internal SpawnNotificationEmitter.Notification After;
        }

        private static readonly MethodInfo entry = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn),
            new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
        private static readonly Func<Thing, IntVec3, Map, Rot4, WipeMode, bool, bool, Thing> spawn =
            OriginalMethods.CreateSpawn<Func<Thing, IntVec3, Map, Rot4, WipeMode, bool, bool, Thing>>(entry);
        private static Notifications notifications;
        private static int revision;
        private static int compiledRevision = -1;

        static ObservedSpawn()
        {
            PatchIntegrity.WrapperCommitted += changed => { if (changed == entry) Interlocked.Increment(ref revision); };
        }

        private static Notifications Current()
        {
            int currentRevision = Volatile.Read(ref revision);
            if (notifications != null && compiledRevision == currentRevision) return notifications;
            var result = new Notifications();
            HarmonyLib.Patches patches = Harmony.GetPatchInfo(entry);
            if (patches != null)
            {
                foreach (MethodInfo method in PatchProcessor.GetSortedPatchMethods(entry, System.Linq.Enumerable.ToArray(patches.Prefixes)))
                    result.Before += SpawnNotificationEmitter.Compile(entry, method, false);
                foreach (MethodInfo method in PatchProcessor.GetSortedPatchMethods(entry, System.Linq.Enumerable.ToArray(patches.Postfixes)))
                    result.After += SpawnNotificationEmitter.Compile(entry, method, true);
            }
            compiledRevision = currentRevision;
            return notifications = result;
        }

        public static Thing Spawn(Thing thing, IntVec3 cell, Map map, Rot4 rotation,
            WipeMode wipeMode, bool respawningAfterLoad, bool forbidLeavings)
        {
            Notifications current = Current();
            current.Before?.Invoke(thing, cell, map, rotation, wipeMode, respawningAfterLoad, forbidLeavings, null);
            if (thing is Pawn pawn && LihuatGame.Current?.Lifecycle.AllowsRegistration(pawn) == false)
                throw new Core.RegenerationDeniedException();
            Thing result = spawn(thing, cell, map, rotation, wipeMode, respawningAfterLoad, forbidLeavings);
            if (result is Pawn placed && placed.Spawned) LihuatGame.Current?.Lifecycle.Registered(placed);
            current.After?.Invoke(thing, cell, map, rotation, wipeMode, respawningAfterLoad, forbidLeavings, result);
            return result;
        }
    }
}
