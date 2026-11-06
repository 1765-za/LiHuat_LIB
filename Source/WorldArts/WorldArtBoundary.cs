using LihuatLib.Dispatching;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace LihuatLib.WorldArts
{
    internal static class WorldArtBoundary
    {
        internal static void Install()
        {
            NativeDispatcher.Register(AccessTools.Method(typeof(Caravan_PathFollower), nameof(Caravan_PathFollower.StartPath)),
                prefix: new DispatchMethod(typeof(WorldArtBoundary), nameof(Start)));
            NativeDispatcher.Register(AccessTools.PropertyGetter(typeof(Caravan), nameof(Caravan.DrawPos)),
                postfix: new DispatchMethod(typeof(WorldArtBoundary), nameof(Position)));
            NativeDispatcher.Register(AccessTools.Method(typeof(TilesPerDayCalculator), nameof(TilesPerDayCalculator.ApproxTilesPerDay),
                    new[] { typeof(Caravan), typeof(StringBuilder) }),
                prefix: new DispatchMethod(typeof(WorldArtBoundary), nameof(Speed)));
            NativeDispatcher.Register(AccessTools.Method(typeof(WorldRenderer), nameof(WorldRenderer.DrawWorldLayers)),
                postfix: new DispatchMethod(typeof(WorldArtBoundary), nameof(Draw)));
        }

        private static bool Start(Caravan ___caravan, PlanetTile destTile, CaravanArrivalAction arrivalAction, ref bool __result)
        {
            var service = LihuatGame.Current?.WorldArts;
            var state = service?.Flight(___caravan);
            if (state?.Enabled != true) return true;
            __result = service.StartFlight(state, destTile, arrivalAction);
            return false;
        }
        private static void Position(Caravan __instance, ref Vector3 __result)
        {
            var state = LihuatGame.Current?.WorldArts.Flight(__instance);
            if (state?.Moving == true) __result = state.DrawPosition;
        }
        private static bool Speed(Caravan caravan, StringBuilder explanation, ref float __result)
        {
            if (!WorldFlightReadout.TryDescribe(caravan, explanation, out float speed)) return true;
            __result = speed;
            return false;
        }
        private static void Draw() => LihuatGame.Current?.WorldArts.Draw();
    }
}
