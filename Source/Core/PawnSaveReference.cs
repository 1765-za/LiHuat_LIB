using Verse;

namespace LihuatLib.Core
{
    public static class PawnSaveReference
    {
        public static bool HasOwner(Pawn pawn)
        {
            if (pawn == null || pawn.Discarded) return false;
            for (IThingHolder holder = pawn; holder != null; holder = holder.ParentHolder)
            {
                if (holder is Pawn member && Find.WorldPawns?.Contains(member) == true) return true;
                if (holder is Thing thing)
                {
                    if (thing.Destroyed) return false;
                    if (thing.Spawned) return true;
                }
                if (holder is Map map) return Find.Maps.Contains(map);
                if (holder is RimWorld.Planet.WorldObject worldObject) return worldObject.Spawned;
                if (holder is GameComponent || holder is RimWorld.Planet.WorldComponent) return true;
            }
            return false;
        }

        public static void Look(ref Pawn pawn, string label)
        {
            if (Scribe.mode != LoadSaveMode.Saving)
            { Scribe_References.Look(ref pawn, label, saveDestroyedThings: true); return; }
            // Identity records do not own bodies; an unowned body cannot be resolved on load.
            Pawn saved = HasOwner(pawn) ? pawn : null;
            Scribe_References.Look(ref saved, label, saveDestroyedThings: true);
        }
    }
}
