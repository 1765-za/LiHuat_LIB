using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.Core
{
    public sealed class PawnPresenceRegistry
    {
        private readonly GenerationAdmission admission;

        internal PawnPresenceRegistry(GenerationAdmission admission) { this.admission = admission; }
        public bool Contains(Pawn pawn) => admission.Contains(pawn);

        internal void Initialize()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
                admission.Remember(pawn);
            foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive) admission.Remember(pawn);
            if (LihuatGame.Current.Characters != null)
                foreach (Thing thing in LihuatGame.Current.Characters.Contents)
                    if (thing is Pawn pawn) admission.Remember(pawn);
        }

        internal void Completed(Pawn pawn, DestructiveEntry entry)
        {
            if (pawn != null && (PawnLife.Dead(pawn) || pawn.Destroyed || pawn.Discarded)) admission.Forget(pawn);
        }
    }
}
