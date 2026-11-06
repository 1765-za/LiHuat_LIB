using RimWorld;
using Verse;

namespace LihuatLib.WorldArts
{
    [DefOf]
    public static class WorldArtDefs
    {
        public static AbilityDef Lihuat_StarPower;
        public static AbilityDef Lihuat_Opening;
        public static AbilityDef Lihuat_Starfall;
        internal static AbilityDef[] Abilities => new[] { Lihuat_StarPower, Lihuat_Opening, Lihuat_Starfall };
    }
}
