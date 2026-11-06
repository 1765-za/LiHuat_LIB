using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DeathPlacementGraph
    {
        internal static bool StartsAt(MethodInfo caller, MethodInfo called) => caller.DeclaringType == typeof(Pawn)
            && caller.Name == nameof(Pawn.Kill) && called.DeclaringType == typeof(GenPlace)
            && called.Name == nameof(GenPlace.TryPlaceThing);

        internal static bool Includes(MethodInfo method)
        {
            if (method.DeclaringType == typeof(GenPlace))
                return method.Name == nameof(GenPlace.TryPlaceThing) || method.Name == "TryPlaceDirect"
                    || method.Name == "SplitAndSpawnOneStackOnCell";
            if (method.DeclaringType == typeof(GenSpawn)) return method.Name == nameof(GenSpawn.Spawn);
            if (!typeof(Entity).IsAssignableFrom(method.DeclaringType)) return false;
            return method.Name == nameof(Thing.SpawnSetup) || method.Name == "get_Spawned"
                || method.Name == "get_Destroyed" || method.Name == "get_Map" || method.Name == "get_MapHeld"
                || method.Name == "get_InnerPawn" || method.Name == "set_Position" || method.Name == "set_Rotation";
        }
    }
}
