using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class ReferenceListStorage<T> where T : class
    {
        internal static readonly AccessTools.FieldRef<List<T>, T[]> Items = AccessTools.FieldRefAccess<List<T>, T[]>("_items");
        internal static readonly AccessTools.FieldRef<List<T>, int> Count = AccessTools.FieldRefAccess<List<T>, int>("_size");
        private static readonly AccessTools.FieldRef<List<T>, int> Version = AccessTools.FieldRefAccess<List<T>, int>("_version");
        internal static readonly Func<List<T>, T, int> Remove = BuildRemoval();

        internal static bool Contains(List<T> list, T target)
        {
            if (list == null) return false;
            T[] items = Items(list);
            int count = Count(list);
            for (int i = 0; i < count; i++) if (ReferenceEquals(items[i], target)) return true;
            return false;
        }

        internal static List<T> Without(List<T> list, T target)
        {
            if (!Contains(list, target)) return list;
            T[] items = Items(list);
            int count = Count(list), written = 0;
            var result = new List<T>(count);
            T[] output = Items(result);
            for (int i = 0; i < count; i++) if (!ReferenceEquals(items[i], target)) output[written++] = items[i];
            Count(result) = written;
            return result;
        }

        internal static bool Ensure(List<T> list, T target)
        {
            if (Contains(list, target)) return false;
            T[] items = Items(list);
            int count = Count(list);
            if (count == items.Length)
            {
                var expanded = new T[checked(count + Math.Max(4, count / 2))];
                Array.Copy(items, expanded, count);
                Items(list) = items = expanded;
            }
            items[count] = target;
            Count(list) = count + 1;
            Version(list)++;
            return true;
        }

        private static Func<List<T>, T, int> BuildRemoval()
        {
            Type type = typeof(List<T>);
            FieldInfo itemsField = AccessTools.Field(type, "_items"), size = AccessTools.Field(type, "_size"), version = AccessTools.Field(type, "_version");
            var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(int), new[] { type, typeof(T) }, typeof(ReferenceListStorage<T>).Module, true);
            ILGenerator il = method.GetILGenerator();
            LocalBuilder items = il.DeclareLocal(typeof(T[])), count = il.DeclareLocal(typeof(int));
            LocalBuilder read = il.DeclareLocal(typeof(int)), write = il.DeclareLocal(typeof(int)), removed = il.DeclareLocal(typeof(int));
            Label present = il.DefineLabel(), scan = il.DefineLabel(), next = il.DefineLabel(), test = il.DefineLabel();
            Label clear = il.DefineLabel(), clearTest = il.DefineLabel(), done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue, present); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(present);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, itemsField); il.Emit(OpCodes.Stloc, items);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, size); il.Emit(OpCodes.Stloc, count);
            il.Emit(OpCodes.Br, test);
            il.MarkLabel(scan);
            il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldloc, read); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Beq, next);
            il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldloc, write);
            il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldloc, read); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Stelem_Ref);
            Increment(il, write);
            il.MarkLabel(next); Increment(il, read);
            il.MarkLabel(test); il.Emit(OpCodes.Ldloc, read); il.Emit(OpCodes.Ldloc, count); il.Emit(OpCodes.Blt, scan);
            il.Emit(OpCodes.Ldloc, count); il.Emit(OpCodes.Ldloc, write); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, removed);
            il.Emit(OpCodes.Ldloc, removed); il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, write); il.Emit(OpCodes.Stfld, size);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, version); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, version);
            il.Emit(OpCodes.Br, clearTest);
            il.MarkLabel(clear); il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldloc, write); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stelem_Ref); Increment(il, write);
            il.MarkLabel(clearTest); il.Emit(OpCodes.Ldloc, write); il.Emit(OpCodes.Ldloc, count); il.Emit(OpCodes.Blt, clear);
            il.MarkLabel(done); il.Emit(OpCodes.Ldloc, removed); il.Emit(OpCodes.Ret);
            return (Func<List<T>, T, int>)method.CreateDelegate(typeof(Func<List<T>, T, int>));
        }

        private static void Increment(ILGenerator il, LocalBuilder value)
        { il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, value); }
    }
}
