using Verse;

namespace LihuatLib.Domains
{
    public sealed class GenStep_Domain : GenStep
    {
        public override int SeedPart => 14803559;
        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamed("Lihuat_DomainFloor");
            foreach (IntVec3 cell in map.AllCells) { map.terrainGrid.SetTerrain(cell, floor); map.fogGrid.Unfog(cell); }
        }
    }
}
