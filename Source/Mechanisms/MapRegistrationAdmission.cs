using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MapRegistrationAdmission
    {
        private static readonly AccessTools.FieldRef<MapPawns, Map> pawnMap =
            AccessTools.FieldRefAccess<MapPawns, Map>("map");
        private static readonly AccessTools.FieldRef<ThingGrid, Map> gridMap =
            AccessTools.FieldRefAccess<ThingGrid, Map>("map");
        private static readonly AccessTools.FieldRef<DynamicDrawManager, Map> drawMap =
            AccessTools.FieldRefAccess<DynamicDrawManager, Map>("map");

        internal static bool Pawn(MapPawns registry, Pawn pawn) => Allows(pawn, pawnMap(registry));
        internal static bool Grid(ThingGrid registry, Thing thing) => Allows(thing, gridMap(registry));
        internal static bool Draw(DynamicDrawManager registry, Thing thing) => Allows(thing, drawMap(registry));

        private static bool Allows(Thing thing, Map destination)
        {
            if (thing is not Pawn pawn) return true;
            LihuatGame game = LihuatGame.Current;
            return game == null || game.Lifecycle.AllowsRegistration(pawn) && game.Domains.AllowsSpawn(pawn, destination);
        }
    }
}
