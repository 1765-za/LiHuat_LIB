using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MovementBoundary
    {
        internal static void Install()
        {
            var method = AccessTools.PropertySetter(typeof(Thing), nameof(Thing.Position));
            NativeIlGuards.UseOriginalWhen(method, AccessTools.Method(typeof(MovementBoundary), nameof(Participates)), 0);
            NativeIlGuards.Scoped(method, AccessTools.Method(typeof(PawnContinuity), nameof(PawnContinuity.BeginMove)), new[] { 0, 1 },
                AccessTools.Method(typeof(MovementBoundary), nameof(End)), new[] { 0 });
        }
        private static bool Participates(Thing thing) => thing is Pawn pawn
            && (PawnContinuity.TracksMovement(pawn) || LihuatGame.Current?.Domains.Contains(pawn) == true);
        private static void End(Thing thing, PawnContinuity.MoveScope scope)
        {
            scope.Dispose();
            if (thing is Pawn pawn && pawn.Spawned) LihuatGame.Current?.Domains.Moved(pawn);
        }
    }
}
