using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class OwnershipCollectionReader
    {
        private static readonly Dictionary<Type, Func<object, ErasureOwnership.Traversal, int, bool>> readers = new Dictionary<Type, Func<object, ErasureOwnership.Traversal, int, bool>>();
        internal static bool Supports(Type type)
        {
            if (type.IsArray) return true;
            if (OwnershipIndirectReader.Supports(type)) return true;
            if (!type.IsGenericType) return false;
            Type definition = type.GetGenericTypeDefinition();
            return definition == typeof(List<>) || definition == typeof(HashSet<>) || definition == typeof(Dictionary<,>);
        }
        internal static bool Relevant(Type type) => type == typeof(object) || type.IsInterface || type.IsArray || Supports(type)
            || type.IsAssignableFrom(typeof(Pawn)) || typeof(Pawn).IsAssignableFrom(type) || typeof(Hediff).IsAssignableFrom(type)
            || typeof(HediffComp).IsAssignableFrom(type) || typeof(ThingComp).IsAssignableFrom(type)
            || !type.IsEnum && LifeResourceEvidence.IsCustom(type);

        internal static bool Visit(object value, ErasureOwnership.Traversal work, int depth)
        {
            Type type = value.GetType();
            Func<object, ErasureOwnership.Traversal, int, bool> read;
            lock (readers)
                if (!readers.TryGetValue(type, out read)) readers[type] = read = Build(type);
            return read(value, work, depth);
        }

        private static Func<object, ErasureOwnership.Traversal, int, bool> Build(Type type)
        {
            if (OwnershipIndirectReader.Supports(type)) return OwnershipIndirectReader.Build(type);
            bool array = type.IsArray, list = !array && type.GetGenericTypeDefinition() == typeof(List<>);
            bool dictionary = !array && !list && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
            FieldInfo storage = array ? null : AccessTools.Field(type, list ? "_items" : "_entries");
            Type arrayType = array ? type : storage.FieldType, element = arrayType.GetElementType();
            FieldInfo count = array ? null : AccessTools.Field(type, list ? "_size" : "_count");
            FieldInfo link = array || list ? null : AccessTools.Field(element, dictionary ? "next" : "Next");
            FieldInfo key = dictionary ? AccessTools.Field(element, "key") : null;
            FieldInfo value = array || list ? null : AccessTools.Field(element, dictionary ? "value" : "Value");
            bool readKey = key != null && Relevant(key.FieldType), readValue = Relevant(value?.FieldType ?? element);
            if (!readKey && !readValue) return (instance, state, depth) => true;
            if (array && type != element.MakeArrayType()) return (instance, state, depth) => false;
            var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(bool), new[] { typeof(object), typeof(ErasureOwnership.Traversal), typeof(int) }, typeof(OwnershipCollectionReader).Module, true);
            ILGenerator il = method.GetILGenerator();
            LocalBuilder items = il.DeclareLocal(arrayType), length = il.DeclareLocal(typeof(int)), index = il.DeclareLocal(typeof(int));
            Label scan = il.DefineLabel(), test = il.DefineLabel(), next = il.DefineLabel(), incomplete = il.DefineLabel(), done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); if (!array) il.Emit(OpCodes.Ldfld, storage); il.Emit(OpCodes.Stloc, items);
            il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Brfalse, done);
            if (array) { il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); }
            else { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type); il.Emit(OpCodes.Ldfld, count); }
            il.Emit(OpCodes.Stloc, length);
            il.Emit(OpCodes.Ldloc, length); il.Emit(OpCodes.Ldc_I4, 64); il.Emit(OpCodes.Bgt, incomplete);
            il.Emit(OpCodes.Br, test);
            il.MarkLabel(scan);
            if (link != null)
            {
                il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldelema, element); il.Emit(OpCodes.Ldfld, link);
                il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Blt, next);
            }
            if (readKey) Push(il, items, index, element, key, incomplete);
            if (readValue) Push(il, items, index, element, value, incomplete);
            il.MarkLabel(next); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index);
            il.MarkLabel(test); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, length); il.Emit(OpCodes.Blt, scan);
            il.MarkLabel(done); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.MarkLabel(incomplete); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<object, ErasureOwnership.Traversal, int, bool>)method.CreateDelegate(typeof(Func<object, ErasureOwnership.Traversal, int, bool>));
        }

        private static void Push(ILGenerator il, LocalBuilder items, LocalBuilder index, Type element, FieldInfo field, Label incomplete)
        {
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, items); il.Emit(OpCodes.Ldloc, index);
            Type value;
            if (field == null) { il.Emit(OpCodes.Ldelem, element); value = element; }
            else { il.Emit(OpCodes.Ldelema, element); il.Emit(OpCodes.Ldfld, field); value = field.FieldType; }
            if (value.IsValueType) il.Emit(OpCodes.Box, value);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(ErasureOwnership.Traversal), nameof(ErasureOwnership.Traversal.Push)));
            il.Emit(OpCodes.Brfalse, incomplete);
        }
    }
}
