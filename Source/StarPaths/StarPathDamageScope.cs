using System;
using Verse;

namespace LihuatLib.StarPaths
{
    public sealed class StarPathDamageScope : IDisposable
    {
        [ThreadStatic] private static StarPathDamageScope current;
        private readonly StarPathDamageScope previous;
        public readonly Thing Target;
        public readonly DamageChannel Channel;
        public readonly float RawAmount;
        internal StarPathDamageScope(Thing target, DamageChannel channel, float amount)
        { Target = target; Channel = channel; RawAmount = amount; previous = current; current = this; }
        public static StarPathDamageScope For(Thing target)
        {
            for (StarPathDamageScope scope = current; scope != null; scope = scope.previous)
                if (scope.Target == target) return scope;
            return null;
        }
        public static bool ConvertedFor(Thing target) => For(target) is StarPathDamageScope scope && scope.Channel != DamageChannel.Ordinary;
        public void Dispose() => current = previous;
    }
}
