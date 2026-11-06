using Verse;

namespace LihuatLib.WorldArts
{
    public sealed class WorldArtProfileDef : Def
    {
        public float flightTilesPerSecond = 45000f;
        public float flightCostPerSecond = 1f;
        public bool allowOrbit = true;
        public int clearingWidth = 5;
        public int clearingLength = 25;
        public int clearingDelayTicks = 30;
        public float clearingCost = 20f;
        public int impactRadius = 1;
        public float impactCost = 80f;
        public float impactDamage = 60000f;
        public int mapClearingRadius = 55;
        public int mapImpactRadius = 40;
        public int impactDelayTicks = 180;
    }
}
