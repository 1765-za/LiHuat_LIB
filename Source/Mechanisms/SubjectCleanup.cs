using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class SubjectCleanup(object subject, bool removeReferences = true)
    {
        private readonly HashSet<object> seen = new(ReferenceIdentity<object>.Instance);
        private int remaining = 4096;
        internal int RemovedReferences, RemovedCallbacks, Failures;
        internal bool Limited;
        private bool Step() { if (remaining-- > 0) return true; Limited = true; return false; }
        private bool Owns(object value) => ReferenceEquals(value, subject) || ReferenceEquals(ErasureOwnership.ResolveSubject(value), subject);

        internal void Run(params object[] roots)
        {
            foreach (object root in roots)
                try { Visit(root, 0, true); }
                catch (Exception) { Failures++; }
        }

        private Delegate Filter(Delegate callback)
        {
            var keep = new List<Delegate>();
            foreach (Delegate entry in callback.GetInvocationList())
            {
                if (!Step()) { keep.Add(entry); continue; }
                try { SubjectCallbacks.Install(entry.Method); }
                catch (Exception) { Failures++; }
                if (Owns(entry.Target)) RemovedCallbacks++;
                else keep.Add(entry);
            }
            return keep.Count == callback.GetInvocationList().Length ? callback : Delegate.Combine(keep.ToArray());
        }

        private void Visit(object value, int depth, bool root = false)
        {
            if (value == null || value is string || value is Map || value is Game || value is Def || value is Pawn) return;
            if (depth > 6) { Limited = true; return; }
            if (!Step() || !seen.Add(value)) return;
            if (!root && (value is MapComponent || value is GameComponent || value is RimWorld.Planet.WorldComponent)) return;
            Type type = value.GetType();
            if (RetirementSubjects.Recognizes(type) && !ReferenceEquals(value, subject)) return;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && value is IList list)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (!Step()) break;
                    object item = list[i];
                    if (item is Delegate callback)
                    {
                        Delegate filtered = Filter(callback);
                        if (filtered == null) { list.RemoveAt(i); RemovedReferences++; }
                        else if (!ReferenceEquals(callback, filtered)) list[i] = filtered;
                    }
                    else if (removeReferences && Owns(item)) { ErasureCollectionEntries.Remove(list, item); RemovedReferences++; }
                    else Visit(item, depth + 1);
                }
                return;
            }
            if (value is IDictionary dictionary && dictionary.Count > remaining) { Limited = true; return; }
            if (ErasureDictionaryEntries.TryCapture(value, out var snapshot))
            {
                foreach (var entry in snapshot.Entries)
                {
                    if (entry == null) continue;
                    if (!Step()) break;
                    if (removeReferences && (Owns(entry.Key) || Owns(entry.Value))) { if (snapshot.Remove(entry.Index)) RemovedReferences++; }
                    else Visit(entry.Value, depth + 1);
                }
                return;
            }
            if (!LifeResourceEvidence.IsCustom(type)) return;
            foreach (FieldInfo field in ErasureObservers.Fields(type))
            {
                if (!Step()) break;
                object child = field.GetValue(value);
                if (child is Delegate callback)
                {
                    Delegate filtered = Filter(callback);
                    if (!field.IsInitOnly && !ReferenceEquals(callback, filtered)) field.SetValue(value, filtered);
                }
                else Visit(child, depth + 1);
            }
        }
    }
}
