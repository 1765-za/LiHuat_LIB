using System;
using Verse;

namespace LihuatLib.StarPaths
{
    internal sealed class StarPathAttackScope : IDisposable
    {
        [ThreadStatic] private static StarPathAttackScope current;
        private readonly StarPathAttackScope previous;
        internal readonly Pawn Target, Attacker;
        internal readonly StarPathDamageSplit Split;
        internal readonly float OrdinaryFraction;
        internal StarPathAttackScope(Pawn attacker, Pawn target, StarPathDamageSplit split, float amount)
        {
            Attacker = attacker; Target = target; Split = split;
            OrdinaryFraction = split.Ordinary / amount;
            previous = current; current = this;
        }
        internal static StarPathAttackScope For(Thing target)
        {
            for (StarPathAttackScope scope = current; scope != null; scope = scope.previous)
                if (scope.Target == target) return scope;
            return null;
        }
        public void Dispose() => current = previous;
    }
}
