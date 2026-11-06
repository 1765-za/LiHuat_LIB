using System;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class SpawnRejectedException : Exception
    {
        internal readonly Thing Thing;
        internal SpawnRejectedException(Thing thing) { Thing = thing; }
    }

    internal static class SpawnCompletion
    {
        [ThreadStatic] private static Thing currentThing;
        [ThreadStatic] private static Map currentMap;

        internal struct State
        {
            internal bool Entered;
            internal Thing PreviousThing;
            internal Map PreviousMap;
            internal SubmapTransferScope Handoff;
        }

        internal static State Begin(Thing thing, Map map)
        {
            var state = new State { Entered = true, PreviousThing = currentThing, PreviousMap = currentMap };
            currentThing = thing; currentMap = map;
            return state;
        }

        internal static void Validate(Thing thing)
        {
            // Derived setup must not continue after its base registration was vetoed.
            if (ReferenceEquals(currentThing, thing) && !thing.Spawned) throw new SpawnRejectedException(thing);
        }

        internal static Thing Rejected(SpawnRejectedException rejection)
        {
            Thing thing = rejection.Thing;
            if (!ReferenceEquals(currentThing, thing)) throw rejection;
            if (!thing.Spawned)
            {
                Map map = currentMap;
                if (map != null && !map.Disposed)
                {
                    MapRegistrationStorage.Process(map, thing, MapRegistrationStorage.Footprint(thing), true);
                    if (ReferenceEquals(thing.holdingOwner, map.spawnedThings)) thing.holdingOwner = null;
                }
                Find.TickManager?.DeRegisterAllTickabilityFor(thing);
            }
            return null;
        }

        internal static void End(State state)
        {
            if (!state.Entered) return;
            try { state.Handoff?.Dispose(); }
            finally { currentThing = state.PreviousThing; currentMap = state.PreviousMap; }
        }
    }
}
