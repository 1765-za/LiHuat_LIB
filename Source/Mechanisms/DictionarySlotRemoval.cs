using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class DictionarySlotRemoval
    {
        internal static Func<object, int, int, bool> Build(Type type)
        {
            FieldInfo bucketsField = AccessTools.Field(type, "_buckets"), entriesField = AccessTools.Field(type, "_entries");
            FieldInfo count = AccessTools.Field(type, "_count"), free = AccessTools.Field(type, "_freeList"), freeCount = AccessTools.Field(type, "_freeCount"), version = AccessTools.Field(type, "_version");
            Type entryType = entriesField.FieldType.GetElementType();
            FieldInfo key = AccessTools.Field(entryType, "key"), value = AccessTools.Field(entryType, "value"), hash = AccessTools.Field(entryType, "hashCode"), link = AccessTools.Field(entryType, "next");
            var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(bool), new[] { typeof(object), typeof(int), typeof(int) }, typeof(DictionarySlotRemoval).Module, true);
            ILGenerator il = method.GetILGenerator();
            LocalBuilder owner = il.DeclareLocal(type), buckets = il.DeclareLocal(typeof(int[])), entries = il.DeclareLocal(entriesField.FieldType);
            LocalBuilder bucket = il.DeclareLocal(typeof(int)), current = il.DeclareLocal(typeof(int)), previous = il.DeclareLocal(typeof(int)), steps = il.DeclareLocal(typeof(int));
            Label rejected = il.DefineLabel(), chain = il.DefineLabel(), matched = il.DefineLabel(), head = il.DefineLabel(), released = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Stloc, owner);
            // A captured slot is valid only while no other dictionary mutation has occurred.
            il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, version); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Bne_Un, rejected);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, rejected);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, count); il.Emit(OpCodes.Bge, rejected);
            il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, entriesField); il.Emit(OpCodes.Stloc, entries);
            Slot(il, entries, entryType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Blt, rejected);
            il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, bucketsField); il.Emit(OpCodes.Stloc, buckets);
            Slot(il, entries, entryType); il.Emit(OpCodes.Ldfld, hash); il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Rem_Un); il.Emit(OpCodes.Stloc, bucket);
            il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Stloc, previous);
            il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Ldloc, bucket); il.Emit(OpCodes.Ldelem_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, current);
            il.MarkLabel(chain);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Beq, matched);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, rejected);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, count); il.Emit(OpCodes.Bge, rejected);
            il.Emit(OpCodes.Ldloc, steps); il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, count); il.Emit(OpCodes.Bge, rejected);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Stloc, previous);
            Slot(il, entries, entryType, current); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Stloc, current);
            il.Emit(OpCodes.Ldloc, steps); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, steps); il.Emit(OpCodes.Br, chain);
            il.MarkLabel(matched);
            il.Emit(OpCodes.Ldloc, previous); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, head);
            Slot(il, entries, entryType, previous); Slot(il, entries, entryType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Stfld, link); il.Emit(OpCodes.Br, released);
            il.MarkLabel(head); il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Ldloc, bucket);
            Slot(il, entries, entryType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stelem_I4);
            il.MarkLabel(released);
            Slot(il, entries, entryType); il.Emit(OpCodes.Ldc_I4, -3); il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldfld, free); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stfld, link);
            Slot(il, entries, entryType); il.Emit(OpCodes.Ldflda, key); il.Emit(OpCodes.Initobj, key.FieldType);
            Slot(il, entries, entryType); il.Emit(OpCodes.Ldflda, value); il.Emit(OpCodes.Initobj, value.FieldType);
            il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, free);
            Increment(il, owner, freeCount); Increment(il, owner, version);
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.MarkLabel(rejected); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<object, int, int, bool>)method.CreateDelegate(typeof(Func<object, int, int, bool>));
        }
        private static void Slot(ILGenerator il, LocalBuilder entries, Type type, LocalBuilder index = null)
        { il.Emit(OpCodes.Ldloc, entries); if (index == null) il.Emit(OpCodes.Ldarg_1); else il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldelema, type); }
        private static void Increment(ILGenerator il, LocalBuilder owner, FieldInfo field)
        { il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, field); }
    }
}
