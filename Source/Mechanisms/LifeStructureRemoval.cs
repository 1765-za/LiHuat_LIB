using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal enum LifeStructureLocation { None, PawnComp, Hediff, HediffComp, Collection, Field }

    internal readonly struct LifeStructureRemoval
    {
        private readonly LifeStructureLocation location;
        private readonly object owner, value;
        private readonly FieldInfo field;
        internal bool Available => location != LifeStructureLocation.None;
        internal LifeStructureRemoval(LifeStructureLocation location, object owner, object value, FieldInfo field = null)
        { this.location = location; this.owner = owner; this.value = value; this.field = field; }

        internal void Apply()
        {
            switch (location)
            {
                case LifeStructureLocation.PawnComp: Remove(((Pawn)owner).AllComps, value); break;
                case LifeStructureLocation.Hediff: Remove(((Pawn)owner).health.hediffSet.hediffs, value); break;
                case LifeStructureLocation.HediffComp: Remove(((HediffWithComps)owner).comps, value); break;
                case LifeStructureLocation.Field: field.SetValue(owner, null); break;
                case LifeStructureLocation.Collection:
                    if (owner is IList list) Remove(list, value);
                    else if (owner is IDictionary dictionary)
                    {
                        object[] keys = dictionary.Keys.Cast<object>().ToArray();
                        foreach (object key in keys) if (ReferenceEquals(dictionary[key], value)) dictionary.Remove(key);
                    }
                    break;
            }
        }

        private static void Remove(IList list, object value)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (ReferenceEquals(list[i], value))
                {
                    if (list.IsFixedSize) list[i] = null;
                    else list.RemoveAt(i);
                }
        }
    }
}
