using Verse;

namespace LihuatLib.Domains
{
    public sealed class CounterRecord : IExposable
    {
        public Pawn Pawn;
        public bool Amplification;
        public bool HandSigns;
        public int LastPaidAt;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Amplification, "amplification");
            Scribe_Values.Look(ref HandSigns, "handSigns");
            Scribe_Values.Look(ref LastPaidAt, "lastPaidAt");
        }
    }
}
