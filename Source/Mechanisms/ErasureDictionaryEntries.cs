using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureDictionaryEntries
    {
        internal sealed class Entry
        {
            internal readonly int Index;
            internal readonly object Key, Value;
            internal Entry(int index, object key, object value) { Index = index; Key = key; Value = value; }
        }

        internal sealed class Snapshot
        {
            internal readonly Entry[] Entries;
            private readonly object dictionary;
            private readonly Plan plan;
            private int version;
            internal Snapshot(object dictionary, Plan plan)
            {
                this.dictionary = dictionary; this.plan = plan; version = plan.Version(dictionary);
                Entries = new Entry[plan.Count(dictionary)];
                for (int i = 0; i < Entries.Length; i++) Entries[i] = plan.Read(dictionary, i);
            }
            internal bool Remove(int index)
            {
                if (!plan.Remove(dictionary, index, version)) return false;
                version = unchecked(version + 1);
                return true;
            }
        }

        internal sealed class Plan
        {
            internal readonly Func<object, int> Count, Version;
            internal readonly Func<object, int, Entry> Read;
            internal readonly Func<object, int, int, bool> Remove;
            internal Plan(Type type)
            {
                Count = Integer(type, "_count"); Version = Integer(type, "_version");
                FieldInfo entries = AccessTools.Field(type, "_entries");
                Type slot = entries.FieldType.GetElementType();
                FieldInfo key = AccessTools.Field(slot, "key"), value = AccessTools.Field(slot, "value"), link = AccessTools.Field(slot, "next");
                var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(Entry), new[] { typeof(object), typeof(int) }, typeof(ErasureDictionaryEntries).Module, true);
                ILGenerator il = method.GetILGenerator(); Label active = il.DefineLabel();
                LoadSlot(il, type, entries, slot); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Bge, active);
                il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
                il.MarkLabel(active); il.Emit(OpCodes.Ldarg_1);
                LoadSlot(il, type, entries, slot); il.Emit(OpCodes.Ldfld, key); if (key.FieldType.IsValueType) il.Emit(OpCodes.Box, key.FieldType);
                LoadSlot(il, type, entries, slot); il.Emit(OpCodes.Ldfld, value); if (value.FieldType.IsValueType) il.Emit(OpCodes.Box, value.FieldType);
                il.Emit(OpCodes.Newobj, typeof(Entry).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int), typeof(object), typeof(object) }, null));
                il.Emit(OpCodes.Ret);
                Read = (Func<object, int, Entry>)method.CreateDelegate(typeof(Func<object, int, Entry>));
                Remove = DictionarySlotRemoval.Build(type);
            }
            private static Func<object, int> Integer(Type type, string name)
            {
                var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(int), new[] { typeof(object) }, typeof(ErasureDictionaryEntries).Module, true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Ldfld, AccessTools.Field(type, name)); il.Emit(OpCodes.Ret);
                return (Func<object, int>)method.CreateDelegate(typeof(Func<object, int>));
            }
            private static void LoadSlot(ILGenerator il, Type type, FieldInfo entries, Type slot)
            { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Ldfld, entries); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldelema, slot); }
        }

        private static readonly Dictionary<Type, Plan> plans = new Dictionary<Type, Plan>();
        internal static bool TryCapture(object dictionary, out Snapshot snapshot)
        {
            snapshot = null;
            Type type = dictionary.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Dictionary<,>)) return false;
            if (!plans.TryGetValue(type, out Plan plan)) plans[type] = plan = new Plan(type);
            snapshot = new Snapshot(dictionary, plan);
            return true;
        }
    }
}
