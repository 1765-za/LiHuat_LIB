using System.Runtime.CompilerServices;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.Core
{
    public sealed class PawnPresenceRegistry
    {
        private sealed class Presence { }
        private readonly ConditionalWeakTable<Pawn, Presence> established = new();
        private readonly LifecycleChannel lifecycle;

        internal PawnPresenceRegistry(LifecycleChannel lifecycle) { this.lifecycle = lifecycle; }
        public bool Contains(Pawn pawn) => pawn != null && established.TryGetValue(pawn, out _);

        internal void Initialize()
        {
            established.Clear();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
                if (!PawnLife.Dead(pawn)) established.GetValue(pawn, _ => new Presence());
        }

        internal void Registered(Pawn pawn)
        {
            if (pawn?.Spawned == true && !PawnLife.Dead(pawn) && lifecycle.AllowsGeneration())
                established.GetValue(pawn, _ => new Presence());
        }

        internal void Completed(Pawn pawn, DestructiveEntry entry)
        {
            if (pawn != null && (PawnLife.Dead(pawn) || pawn.Destroyed || pawn.Discarded)) established.Remove(pawn);
        }
    }
}
