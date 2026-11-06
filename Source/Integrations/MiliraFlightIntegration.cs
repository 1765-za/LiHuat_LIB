using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class MiliraFlightIntegration
    {
        internal static void Install()
        {
            var type = AccessTools.TypeByName("Milira.MiliraPawnFlyer");
            if (type != null) PawnFlightBoundary.RegisterFlight(type);
        }
    }
}
