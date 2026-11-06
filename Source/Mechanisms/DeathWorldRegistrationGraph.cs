using System.Reflection;
using RimWorld.Planet;

namespace LihuatLib.Mechanisms
{
    internal static class DeathWorldRegistrationGraph
    {
        internal static bool Includes(MethodInfo method) => method.DeclaringType == typeof(WorldPawns)
            && (method.Name == nameof(WorldPawns.PassToWorld) || method.Name == "AddPawn"
                || method.Name == nameof(WorldPawns.Notify_PawnDestroyed) || method.Name == nameof(WorldPawns.Contains)
                || method.Name == nameof(WorldPawns.IsBeingDiscarded));
    }
}
