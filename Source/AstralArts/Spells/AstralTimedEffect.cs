using Verse;

namespace LihuatLib.AstralArts
{
    internal sealed class AstralTimedEffect : IExposable
    {
        internal Pawn Pawn;
        internal string CasterId;
        internal AstralSpellKind Kind;
        internal int Until, NextPulse, Revision;
        internal float Power, MoveFactor = 1f, DamageFactor = 1f, AimFactor = 1f, Reduction;
        public AstralTimedEffect() { }
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn"); Scribe_Values.Look(ref CasterId, "casterId");
            Scribe_Values.Look(ref Kind, "kind"); Scribe_Values.Look(ref Until, "until");
            Scribe_Values.Look(ref NextPulse, "nextPulse"); Scribe_Values.Look(ref Power, "power");
            Scribe_Values.Look(ref MoveFactor, "move", 1f); Scribe_Values.Look(ref DamageFactor, "damage", 1f);
            Scribe_Values.Look(ref AimFactor, "aim", 1f); Scribe_Values.Look(ref Reduction, "reduction");
        }
    }
}
