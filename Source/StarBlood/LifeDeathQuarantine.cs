using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;
using LihuatLib.Core;

namespace LihuatLib.StarBlood
{
    internal sealed class LifeDeathQuarantine
    {
        private readonly Func<int> clock;
        public LifeDeathQuarantine(Func<int> clock) { this.clock = clock; }
        private Pawn pawn;
        private int tick = -1;
        private readonly HashSet<Type> detachedTypes = new HashSet<Type>();

        public void Add(Pawn target, Type type)
        {
            int now = clock();
            if (pawn != target || tick != now) detachedTypes.Clear();
            pawn = target; tick = now;
            if (detachedTypes.Count < 64) detachedTypes.Add(Normalize(type));
        }

        public bool ContainsCaller(Pawn target)
        {
            if (detachedTypes.Count == 0 || pawn != target) return false;
            if (tick != clock()) { detachedTypes.Clear(); return false; }
            // Detaching a component cannot unwind the callback that is already executing it.
            var trace = new StackTrace(1, false);
            for (int i = 0; i < Math.Min(32, trace.FrameCount); i++)
            {
                Type caller = trace.GetFrame(i).GetMethod()?.DeclaringType;
                if (caller != null && detachedTypes.Contains(Normalize(caller))) return true;
            }
            detachedTypes.Clear();
            return false;
        }
        private static Type Normalize(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }
}
