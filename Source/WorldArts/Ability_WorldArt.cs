using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed class Ability_WorldArt : Ability
    {
        public Ability_WorldArt() { }
        public Ability_WorldArt(Pawn pawn) : base(pawn) { }
        public Ability_WorldArt(Pawn pawn, Precept sourcePrecept) : base(pawn, sourcePrecept) { }
        public Ability_WorldArt(Pawn pawn, AbilityDef def) : base(pawn, def) { }
        public Ability_WorldArt(Pawn pawn, Precept sourcePrecept, AbilityDef def) : base(pawn, sourcePrecept, def) { }
        public override IEnumerable<Command> GetGizmos()
        {
            if (!WorldRendererUtility.WorldRendered) yield break;
            WorldArtService service = LihuatGame.Current?.WorldArts;
            WorldArtSubject subject = service?.Find(pawn);
            if (subject == null || !service.Available(subject)) yield break;
            foreach (Command command in WorldArtCommands.For(this, subject, service)) yield return command;
        }
    }
}
