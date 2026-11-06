using System.Text;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.WorldArts
{
    internal static class WorldFlightReadout
    {
        internal static bool TryDescribe(Caravan caravan, StringBuilder explanation, out float tilesPerDay)
        {
            tilesPerDay = 0f;
            var service = LihuatGame.Current?.WorldArts;
            if (service == null || !service.TryGetFlightSpeed(caravan, out float tilesPerSecond)) return false;
            tilesPerDay = tilesPerSecond * (60000f / 60f);
            if (explanation != null)
            {
                explanation.Clear();
                explanation.Append("Lihuat_WorldFlightSpeed".Translate(tilesPerSecond));
            }
            return true;
        }
    }
}
