using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Core
{
    internal static class PawnHealthStateAccess
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "healthState")]
        private static extern ref PawnHealthState State(Pawn_HealthTracker health);

        internal static PawnHealthState Read(Pawn_HealthTracker health) => State(health);
        internal static void Write(Pawn_HealthTracker health, PawnHealthState value) => State(health) = value;
    }
}
