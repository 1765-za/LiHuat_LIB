using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed partial class LifeDamageEntryIndex
    {
        private (DamageDef Def, Type Worker)[] definitions = Array.Empty<(DamageDef, Type)>();
        private MethodInfo[] candidates;
        private int catalogRevision;
        private int capturedRevision = -1;

        internal MethodInfo[] CaptureCandidates()
        {
            List<DamageDef> current = DefDatabase<DamageDef>.AllDefsListForReading;
            int revision = Volatile.Read(ref catalogRevision);
            bool matches = candidates != null && capturedRevision == revision && current.Count == definitions.Length;
            for (int i = 0; matches && i < current.Count; i++)
                matches = ReferenceEquals(current[i], definitions[i].Def) && current[i].workerClass == definitions[i].Worker;
            if (matches) return candidates;

            var next = new (DamageDef, Type)[current.Count];
            var kinds = new HashSet<Type>();
            var methods = new HashSet<MethodInfo>();
            var result = new List<MethodInfo>();
            Append(TakeDamage, methods, result);
            Append(AddHediff, methods, result);
            for (int i = 0; i < current.Count; i++)
            {
                DamageDef def = current[i];
                next[i] = (def, def.workerClass);
                if (def.workerClass != null && kinds.Add(def.workerClass)) Append(Worker(def.workerClass), methods, result);
            }
            definitions = next;
            candidates = result.ToArray();
            capturedRevision = revision;
            return candidates;
        }

        private void Append(MethodInfo method, HashSet<MethodInfo> seen, List<MethodInfo> result)
        {
            if (method == null) return;
            foreach (MethodInfo candidate in Read(method))
                if (seen.Add(candidate)) result.Add(candidate);
        }
    }
}
