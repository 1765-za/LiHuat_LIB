using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class ReferenceSetStorage<T> where T : class
    {
        private static readonly Func<HashSet<T>, T, bool, bool> access = Build();
        internal static bool Remove(HashSet<T> set, T target) => access(set, target, true);
        internal static bool Contains(HashSet<T> set, T target) => access(set, target, false);

        private static Func<HashSet<T>, T, bool, bool> Build()
        {
            Type type = typeof(HashSet<T>);
            FieldInfo bucketsField = AccessTools.Field(type, "_buckets"), slotsField = AccessTools.Field(type, "_entries");
            FieldInfo count = AccessTools.Field(type, "_freeCount"), last = AccessTools.Field(type, "_count"), free = AccessTools.Field(type, "_freeList"), version = AccessTools.Field(type, "_version");
            Type slotType = slotsField.FieldType.GetElementType();
            FieldInfo value = AccessTools.Field(slotType, "Value"), hash = AccessTools.Field(slotType, "HashCode"), link = AccessTools.Field(slotType, "Next");
            var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(bool), new[] { type, typeof(T), typeof(bool) }, typeof(ReferenceSetStorage<T>).Module, true);
            ILGenerator il = method.GetILGenerator();
            LocalBuilder buckets = il.DeclareLocal(typeof(int[])), slots = il.DeclareLocal(slotsField.FieldType);
            LocalBuilder limit = il.DeclareLocal(typeof(int)), index = il.DeclareLocal(typeof(int)), bucket = il.DeclareLocal(typeof(int));
            LocalBuilder current = il.DeclareLocal(typeof(int)), previous = il.DeclareLocal(typeof(int)), steps = il.DeclareLocal(typeof(int)), found = il.DeclareLocal(typeof(bool));
            Label done = il.DefineLabel(), test = il.DefineLabel(), scan = il.DefineLabel(), next = il.DefineLabel(), remove = il.DefineLabel();
            Label chain = il.DefineLabel(), matched = il.DefineLabel(), head = il.DefineLabel(), released = il.DefineLabel(), empty = il.DefineLabel(), invalid = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, bucketsField); il.Emit(OpCodes.Stloc, buckets);
            il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, slotsField); il.Emit(OpCodes.Stloc, slots);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, last); il.Emit(OpCodes.Stloc, limit);
            il.Emit(OpCodes.Br, test);
            il.MarkLabel(scan);
            Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Blt, next);
            Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldfld, value); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Bne_Un, next);
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, found);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Brtrue, remove); il.Emit(OpCodes.Br, done);
            il.MarkLabel(remove);
            // The recorded hash locates the chain even if the pawn's ID has changed.
            Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldfld, hash);
            il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Rem_Un); il.Emit(OpCodes.Stloc, bucket);
            il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Stloc, previous);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, steps);
            il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Ldloc, bucket); il.Emit(OpCodes.Ldelem_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, current);
            il.MarkLabel(chain);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Beq, matched);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, invalid);
            il.Emit(OpCodes.Ldloc, steps); il.Emit(OpCodes.Ldloc, limit); il.Emit(OpCodes.Bge, invalid);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Stloc, previous);
            Slot(il, slots, current, slotType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Stloc, current);
            Add(il, steps, 1); il.Emit(OpCodes.Br, chain);
            il.MarkLabel(matched);
            il.Emit(OpCodes.Ldloc, previous); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, head);
            Slot(il, slots, previous, slotType); Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Stfld, link); il.Emit(OpCodes.Br, released);
            il.MarkLabel(head);
            il.Emit(OpCodes.Ldloc, buckets); il.Emit(OpCodes.Ldloc, bucket);
            Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldfld, link); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stelem_I4);
            il.MarkLabel(released);
            Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, value);
            Slot(il, slots, index, slotType); il.Emit(OpCodes.Ldc_I4, -3); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, free); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stfld, link);
            AddField(il, count, 1); AddField(il, version, 1);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Stfld, free);
            il.MarkLabel(next); Add(il, index, 1);
            il.MarkLabel(test); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, limit); il.Emit(OpCodes.Blt, scan);
            il.MarkLabel(done); il.Emit(OpCodes.Ldloc, found); il.Emit(OpCodes.Ret);
            il.MarkLabel(invalid); il.Emit(OpCodes.Ldstr, "Set storage chain is inconsistent.");
            il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })); il.Emit(OpCodes.Throw);
            return (Func<HashSet<T>, T, bool, bool>)method.CreateDelegate(typeof(Func<HashSet<T>, T, bool, bool>));
        }

        private static void Slot(ILGenerator il, LocalBuilder slots, LocalBuilder index, Type type)
        { il.Emit(OpCodes.Ldloc, slots); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldelema, type); }
        private static void Add(ILGenerator il, LocalBuilder value, int amount)
        { il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ldc_I4, amount); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, value); }
        private static void AddField(ILGenerator il, FieldInfo field, int amount)
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ldc_I4, amount); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, field); }
    }
}
