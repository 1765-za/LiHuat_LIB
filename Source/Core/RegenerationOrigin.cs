using Verse;

namespace LihuatLib.Core
{
    public enum RegenerationKind { Resurrection, Replacement, Preparation }

    public readonly struct RegenerationOrigin
    {
        public readonly Pawn Source;
        public readonly string RootPawnId;
        public readonly RegenerationKind Kind;
        public readonly Pawn Candidate;
        public RegenerationOrigin(Pawn source, string rootPawnId, RegenerationKind kind)
            : this(source, rootPawnId, kind, null) { }
        private RegenerationOrigin(Pawn source, string rootPawnId, RegenerationKind kind, Pawn candidate)
        { Source = source; RootPawnId = rootPawnId; Kind = kind; Candidate = candidate; }
        internal RegenerationOrigin ForBody(Pawn pawn) => new RegenerationOrigin(Source, RootPawnId, Kind, pawn);
    }

    internal sealed class RegenerationLink : IExposable
    {
        public Pawn Body;
        public string RootPawnId;
        public bool Activated;
        public void ExposeData()
        {
            Scribe_References.Look(ref Body, "body");
            Scribe_Values.Look(ref RootPawnId, "rootPawnId");
            Scribe_Values.Look(ref Activated, "activated");
        }
    }
}
