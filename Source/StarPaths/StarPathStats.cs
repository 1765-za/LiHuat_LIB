using RimWorld;
using Verse;

namespace LihuatLib.StarPaths
{
    internal static class StarPathStats
    {
        internal static void Apply(StatDef stat, StatRequest request, ref float value)
        {
            bool equipment = !(request.Thing is Pawn);
            Pawn pawn = request.Thing as Pawn ?? (request.Thing?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            StarPathService service = LihuatGame.Current?.StarPaths;
            StarPathSubject subject = service?.Find(pawn);
            if (service?.Active(subject) == true && subject.Effects.Stats.TryGetValue(stat, out StarPathStatEffect effect)
                && effect.Equipment == equipment) value = effect.Apply(value);
        }

        internal static void Invalidate(Pawn pawn, StarPathEffects effects)
        {
            if (pawn == null) return;
            foreach (StatDef stat in effects.Stats.Keys)
            {
                stat.Worker.ClearCacheForThing(pawn);
                var weapons = pawn.equipment?.AllEquipmentListForReading;
                if (weapons != null) foreach (ThingWithComps weapon in weapons) stat.Worker.ClearCacheForThing(weapon);
            }
        }
    }
}
