using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static partial class PinnedHediffs
    {
        private sealed class Registry
        {
            internal readonly Game Game;
            internal readonly ConditionalWeakTable<Pawn, Owner> Owners = new();
            internal readonly ConditionalWeakTable<HediffSet, Owner> Sets = new();
            internal readonly ConditionalWeakTable<List<Hediff>, Owner> Lists = new();
            internal readonly ConditionalWeakTable<Hediff, Entry> Entries = new();
            internal Registry(Game game) { Game = game; }
        }
        private static Game game => registry.Value.Game;
        private static ConditionalWeakTable<Pawn, Owner> owners => registry.Value.Owners;
        private static ConditionalWeakTable<HediffSet, Owner> sets => registry.Value.Sets;
        private static ConditionalWeakTable<List<Hediff>, Owner> lists => registry.Value.Lists;
        private static ConditionalWeakTable<Hediff, Entry> entries => registry.Value.Entries;
    }
}
