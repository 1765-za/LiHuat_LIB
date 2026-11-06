using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureOwnership
    {
        internal sealed class Traversal
        {
            internal readonly object[] Pending = new object[64], Seen = new object[64];
            internal readonly int[] Depth = new int[64];
            internal int Count, SeenCount, Peak;
            internal bool Active;
            internal void Clear()
            {
                for (int i = 0; i < Peak; i++) Pending[i] = null;
                for (int i = 0; i < SeenCount; i++) Seen[i] = null;
                Count = SeenCount = Peak = 0; Active = false;
            }
            internal bool Push(object value, int depth)
            {
                if (value == null) return true;
                if (Count == Pending.Length) return false;
                Pending[Count] = value; Depth[Count++] = depth;
                if (Count > Peak) Peak = Count;
                return true;
            }
            internal bool Contains(object value)
            {
                for (int i = 0; i < SeenCount; i++) if (ReferenceEquals(Seen[i], value)) return true;
                return false;
            }
        }

        private static readonly Dictionary<Type, Func<object, object>[]> readers = new Dictionary<Type, Func<object, object>[]>();
        [ThreadStatic] private static Traversal traversal;

        internal static Pawn Resolve(object value)
            => ResolveCore(value, false) as Pawn;

        internal static object ResolveSubject(object value) => ResolveCore(value, true);

        private static object ResolveCore(object value, bool subjects)
        {
            object direct = Direct(value, subjects);
            if (direct != null || value == null) return direct;
            Traversal work = traversal ?? (traversal = new Traversal());
            if (work.Active) work = new Traversal();
            work.Active = true;
            try
            {
                object owner = null;
                work.Push(value, 0);
                while (work.Count != 0)
                {
                    int slot = --work.Count, depth = work.Depth[slot];
                    object item = work.Pending[slot]; work.Pending[slot] = null;
                    object candidate = Direct(item, subjects);
                    if (candidate != null)
                    {
                        if (owner != null && !ReferenceEquals(owner, candidate)) return null;
                        owner = candidate;
                        continue;
                    }
                    if (item == null || work.Contains(item)) continue;
                    if (subjects && (item is MapComponent || item is GameComponent || item is RimWorld.Planet.WorldComponent)) continue;
                    Type type = item.GetType();
                    bool collection = OwnershipCollectionReader.Supports(type);
                    if (!collection && !LifeResourceEvidence.IsCustom(type)) continue;
                    // Incomplete ownership must never authorize blocking a shared controller.
                    if (depth == 8 || work.SeenCount == work.Seen.Length) return null;
                    work.Seen[work.SeenCount++] = item;
                    if (collection)
                    {
                        if (!OwnershipCollectionReader.Visit(item, work, depth + 1)) return null;
                        continue;
                    }
                    foreach (Func<object, object> read in Readers(item.GetType()))
                        if (!work.Push(read(item), depth + 1)) return null;
                }
                return owner;
            }
            finally { work.Clear(); }
        }

        private static object Direct(object value, bool subjects)
        {
            Pawn pawn = value as Pawn ?? (value is Hediff hediff ? hediff.pawn
                : value is HediffComp hc ? hc.parent?.pawn : value is ThingComp comp ? comp.parent as Pawn : null);
            return pawn ?? (subjects && value != null && Core.RetirementSubjects.Recognizes(value.GetType()) ? value : null);
        }

        private static Func<object, object>[] Readers(Type type)
        {
            lock (readers)
            {
                if (readers.TryGetValue(type, out Func<object, object>[] result)) return result;
                FieldInfo[] fields = ErasureObservers.Fields(type);
                result = new Func<object, object>[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(object), new[] { typeof(object) }, typeof(ErasureOwnership).Module, true);
                    ILGenerator il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0); il.Emit(field.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
                    result[i] = (Func<object, object>)method.CreateDelegate(typeof(Func<object, object>));
                }
                return readers[type] = result;
            }
        }
    }
}
