using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using LihuatLib.Core;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class ErasureObservers
    {
        private sealed class Mark { public int NextScan = -1; }
        private static Game game;
        private static ConditionalWeakTable<Pawn, Mark> victims = new ConditionalWeakTable<Pawn, Mark>();
        private static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, FieldInfo[]> staticLinks = new Dictionary<Type, FieldInfo[]>();
        private static readonly LifeIlReader reader = new LifeIlReader();
        public static long RemovedReferences { get; private set; }
        public static long RemovedListeners { get; private set; }
        public static long LimitedScans { get; private set; }
        public static long Unreadable { get; private set; }

        private static void CurrentGame()
        {
            if (ReferenceEquals(game, Current.Game)) return;
            game = Current.Game;
            victims = new ConditionalWeakTable<Pawn, Mark>();
        }

        public static bool Contains(Pawn pawn)
        {
            CurrentGame();
            return pawn != null && victims.TryGetValue(pawn, out _)
                && LihuatGame.Current?.Lifecycle.AllowsErasureObservers(pawn) != false;
        }

        public static void Remember(Pawn pawn)
        {
            LibraryCapabilities.Require(LibraryCapability.Erasure);
            CurrentGame();
            if (pawn != null && !victims.TryGetValue(pawn, out _)) victims.Add(pawn, new Mark());
        }

        internal static FieldInfo[] Fields(Type type)
        {
            if (fields.TryGetValue(type, out FieldInfo[] result)) return result;
            var found = new List<FieldInfo>();
            for (Type current = type; LifeResourceEvidence.IsCustom(current); current = current.BaseType)
                found.AddRange(current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(f => !f.FieldType.IsPointer && !f.FieldType.IsValueType && f.FieldType != typeof(string)));
            return fields[type] = found.ToArray();
        }

        internal static Pawn UniqueOwner(object value) => ErasureOwnership.Resolve(value);

        private static FieldInfo[] StaticLinks(Type type)
        {
            if (staticLinks.TryGetValue(type, out FieldInfo[] result)) return result;
            var found = new HashSet<FieldInfo>();
            var pending = new Queue<MethodInfo>(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            var seen = new HashSet<MethodInfo>();
            while (pending.Count > 0 && seen.Count < 96)
            {
                MethodInfo method = pending.Dequeue();
                if (!seen.Add(method) || method.IsAbstract || method.ContainsGenericParameters) continue;
                try
                {
                    if (method.GetParameters().Any(ErasureCallbackIl.TargetArgument)) ErasureCallbacks.Install(method);
                    var code = reader.Read(method);
                    if (code == null) { Unreadable++; continue; }
                    foreach (var line in code)
                    {
                        if ((line.opcode == OpCodes.Ldsfld || line.opcode == OpCodes.Ldsflda) && line.operand is FieldInfo field
                            && LifeResourceEvidence.IsCustom(field.DeclaringType) && !field.FieldType.IsValueType && field.FieldType != typeof(string)) found.Add(field);
                        if (line.operand is MethodInfo called && called.Module.Assembly == type.Assembly && !seen.Contains(called)) pending.Enqueue(called);
                    }
                }
                catch (Exception) { Unreadable++; }
            }
            if (pending.Count > 0) LimitedScans++;
            return staticLinks[type] = found.ToArray();
        }

        public static void Stop(Pawn pawn)
        {
            LibraryCapabilities.Require(LibraryCapability.Erasure);
            CurrentGame();
            Remember(pawn);
            if (LihuatGame.Current?.Lifecycle.AllowsErasureObservers(pawn) == false) return;
            Mark mark = victims.GetValue(pawn, _ => new Mark());
            int now = Find.TickManager.TicksGame;
            if (now < mark.NextScan) return;
            mark.NextScan = now + 60;
            var scan = new Scan(pawn);
            var roots = new List<object>();
            foreach (ThingComp comp in pawn.AllComps) roots.Add(comp);
            if (pawn.health?.hediffSet != null)
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    roots.Add(hediff);
                    if (hediff is HediffWithComps withComps && withComps.comps != null)
                        foreach (HediffComp comp in withComps.comps) roots.Add(comp);
                }
            if (game != null) foreach (GameComponent component in game.components) roots.Add(component);
            if (Find.World != null) foreach (WorldComponent component in Find.World.components) roots.Add(component);
            foreach (Map map in Find.Maps) foreach (MapComponent component in map.components) roots.Add(component);
            foreach (object root in roots) scan.DirectCallbacks(root);
            foreach (object root in roots) scan.Visit(root, 0);
            if (scan.Limited) LimitedScans++;
        }

        private sealed class Scan
        {
            private readonly Pawn pawn;
            private readonly HashSet<object> visited = new HashSet<object>(ReferenceIdentity<object>.Instance);
            private readonly HashSet<Type> statics = new HashSet<Type>();
            private int remaining = 4096;
            public bool Limited;
            public Scan(Pawn pawn) { this.pawn = pawn; }
            private bool Step() { if (remaining-- > 0) return true; Limited = true; return false; }
            private bool Bound(object value) => value != null && ReferenceEquals(UniqueOwner(value), pawn);

            public void DirectCallbacks(object value)
            {
                if (value == null) return;
                foreach (FieldInfo field in Fields(value.GetType()))
                {
                    if (field.IsInitOnly || !typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                    try
                    {
                        if (!(field.GetValue(value) is Delegate work)) continue;
                        Delegate filtered = Filter(work, false);
                        if (!ReferenceEquals(work, filtered)) field.SetValue(value, filtered);
                    }
                    catch (Exception) { Unreadable++; }
                }
            }

            private Delegate Filter(Delegate work, bool inspectTargets = true)
            {
                Delegate[] callbacks = work.GetInvocationList();
                bool changed = false;
                for (int i = 0; i < callbacks.Length; i++)
                {
                    if (!Step()) break;
                    Delegate call = callbacks[i];
                    ErasureCallbacks.Install(call.Method);
                    if (Bound(call.Target))
                    {
                        ErasureCallbacks.Install(call.Target.GetType());
                        callbacks[i] = null;
                        changed = true;
                        RemovedListeners++;
                    }
                    else
                    {
                        callbacks[i] = ErasureDelegateGate.Wrap(call);
                        changed |= !ReferenceEquals(callbacks[i], call);
                        if (inspectTargets) Visit(call.Target, 1);
                    }
                }
                return changed ? Delegate.Combine(callbacks.Where(callback => callback != null).ToArray()) : work;
            }

            public void Visit(object value, int depth)
            {
                if (value == null || value is Pawn || value is Def || value is Map || value is World || value is Game || value is string) return;
                if (depth > 6) { Limited = true; return; }
                if (!Step() || !visited.Add(value)) return;
                Type type = value.GetType();
                if (ErasureWeakTable.TryTake(value, pawn, out object authority))
                {
                    if (authority != null) { RemovedReferences++; Visit(authority, depth + 1); }
                    return;
                }
                bool collection = type.Assembly == typeof(List<>).Assembly || type.Assembly == typeof(HashSet<>).Assembly;
                if (collection && ErasureDictionaryEntries.TryCapture(value, out ErasureDictionaryEntries.Snapshot snapshot))
                {
                    foreach (ErasureDictionaryEntries.Entry item in snapshot.Entries)
                    {
                        if (item == null) continue;
                        if (!Step()) break;
                        bool bound = Bound(item.Key) || Bound(item.Value);
                        Visit(item.Key, depth + 1);
                        Visit(item.Value, depth + 1);
                        if (!bound) continue;
                        if (!snapshot.Remove(item.Index)) { Limited = true; break; }
                        RemovedReferences++;
                    }
                    return;
                }
                if (collection && value is IDictionary dictionary)
                {
                    var remove = new List<object>();
                    foreach (DictionaryEntry item in dictionary)
                    {
                        if (!Step()) break;
                        bool bound = Bound(item.Key) || Bound(item.Value);
                        Visit(item.Value, depth + 1);
                        if (bound) remove.Add(item.Key);
                    }
                    if (!dictionary.IsReadOnly && !dictionary.IsFixedSize)
                        foreach (object key in remove) { dictionary.Remove(key); RemovedReferences++; }
                    return;
                }
                if (collection && value is IList list)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (!Step()) break;
                        object item = list[i];
                        if (item is Delegate call)
                        {
                            Delegate filtered = Filter(call);
                            if (!list.IsReadOnly) list[i] = filtered;
                            if (filtered == null && !list.IsReadOnly && !list.IsFixedSize) list.RemoveAt(i);
                        }
                        else
                        {
                            Visit(item, depth + 1);
                            if (Bound(item) && !list.IsReadOnly && !list.IsFixedSize)
                            {
                                if (!ErasureCollectionEntries.Remove(list, item)) list.RemoveAt(i);
                                RemovedReferences++;
                                i = Math.Min(i, list.Count);
                            }
                        }
                    }
                    return;
                }
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>) && value is IEnumerable entries)
                {
                    var bound = new List<object>();
                    foreach (object item in entries)
                    {
                        if (!Step()) break;
                        Visit(item, depth + 1);
                        if (Bound(item)) bound.Add(item);
                    }
                    foreach (object item in bound) if (ErasureCollectionEntries.Remove(value, item)) RemovedReferences++;
                    return;
                }
                if (!LifeResourceEvidence.IsCustom(type)) return;
                if (Bound(value)) ErasureCallbacks.Install(type);
                foreach (FieldInfo field in Fields(type)) ReadField(field, value, depth);
                if (statics.Add(type)) foreach (FieldInfo field in StaticLinks(type)) ReadField(field, null, depth);
            }

            private void ReadField(FieldInfo field, object owner, int depth)
            {
                if (!Step()) return;
                try
                {
                    object child = field.GetValue(owner);
                    if (child is Delegate work)
                    {
                        Delegate filtered = Filter(work);
                        if (!ReferenceEquals(work, filtered) && !field.IsInitOnly) field.SetValue(owner, filtered);
                    }
                    else Visit(child, depth + 1);
                }
                catch (Exception) { Unreadable++; }
            }
        }
    }
}
