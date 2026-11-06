using System;
using Verse;

namespace LihuatLib.Core
{
    internal sealed class LifecycleExecution : IDisposable
    {
        [ThreadStatic] private static LifecycleExecution current;
        private readonly LifecycleExecution previous;
        private readonly Pawn pawn;
        private readonly DestructiveEntry entry;
        internal Pawn Pawn => pawn;
        internal DestructiveEntry Entry => entry;
        internal LifecycleExecution(Pawn pawn, DestructiveEntry entry)
        { this.pawn = pawn; this.entry = entry; previous = current; current = this; }
        internal static bool EndingLife(Pawn pawn)
        {
            for (LifecycleExecution scope = current; scope != null; scope = scope.previous)
                if (scope.pawn == pawn && (scope.entry == DestructiveEntry.Kill || scope.entry == DestructiveEntry.Destroy)) return true;
            return false;
        }
        public void Dispose() => current = previous;
    }
}
