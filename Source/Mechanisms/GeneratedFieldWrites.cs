using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class GeneratedFieldWrites
    {
        private sealed class SaveValue<T>(T value) { internal T Value = value; }
        private static ref T SaveReference<T>(T value) => ref new SaveValue<T>(value).Value;
        private static readonly object sync = new();
        private static readonly List<FieldInfo> fields = new();
        private static readonly Dictionary<(FieldInfo, bool), DynamicMethod> accessors = new();
        internal static DynamicMethod Reference(FieldInfo field) => Accessor(field, true);
        internal static FieldInfo ReferenceField(MethodInfo method)
        {
            lock (sync)
                foreach (var pair in accessors)
                    if (pair.Key.Item2 && pair.Value == method) return pair.Key.Item1;
            return null;
        }

        internal static bool Rewrite(ILGenerator il, OpCode opcode, object operand)
        {
            if (opcode == OpCodes.Call && operand is MethodInfo raw
                && ObjectAddressCalls.Replacement(raw) is MethodInfo guarded)
            { il.Emit(OpCodes.Call, guarded); return true; }
            if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && operand is MethodInfo method
                && HediffListCalls.Replacement(method) is MethodInfo insertion)
            { il.Emit(OpCodes.Call, insertion); return true; }
            if (operand is not FieldInfo field || !GeneratedMethodCatalogue.Sensitive(field)) return false;
            bool address = opcode == OpCodes.Ldflda || opcode == OpCodes.Ldsflda;
            if (!address && opcode != OpCodes.Stfld && opcode != OpCodes.Stsfld) return false;
            il.Emit(OpCodes.Call, Accessor(field, address));
            return true;
        }

        private static DynamicMethod Accessor(FieldInfo field, bool address)
        {
            lock (sync)
            {
                if (accessors.TryGetValue((field, address), out DynamicMethod existing)) return existing;
                int slot = fields.Count; fields.Add(field);
                Type receiverType = field.DeclaringType.IsValueType ? field.DeclaringType.MakeByRefType() : field.DeclaringType;
                Type[] receiver = field.IsStatic ? Type.EmptyTypes : new[] { receiverType };
                Type[] parameters = address ? receiver : field.IsStatic ? new[] { field.FieldType } : new[] { receiverType, field.FieldType };
                var method = new DynamicMethod(Guid.NewGuid().ToString("N"), address ? field.FieldType.MakeByRefType() : typeof(void),
                    parameters, typeof(GeneratedFieldWrites).Module, true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(field.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);
                if (!field.IsStatic && field.DeclaringType.IsValueType)
                {
                    il.Emit(OpCodes.Ldobj, field.DeclaringType);
                    il.Emit(OpCodes.Box, field.DeclaringType);
                }
                if (address)
                {
                    if (!field.FieldType.IsByRefLike && !field.FieldType.IsPointer && !field.FieldType.IsFunctionPointer)
                    {
                        Label actual = il.DefineLabel();
                        il.Emit(OpCodes.Call, AccessTools.Method(typeof(GeneratedFieldWrites), nameof(NeedsSaveCopy)));
                        il.Emit(OpCodes.Brfalse, actual);
                        if (!field.IsStatic) il.Emit(OpCodes.Ldarg_0);
                        il.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);
                        il.Emit(OpCodes.Call, AccessTools.Method(typeof(GeneratedFieldWrites), nameof(SaveReference)).MakeGenericMethod(field.FieldType));
                        il.Emit(OpCodes.Ret);
                        il.MarkLabel(actual);
                    }
                    else il.Emit(OpCodes.Call, AccessTools.Method(typeof(GeneratedFieldWrites), nameof(RequireAddress)));
                    if (!field.IsStatic) il.Emit(OpCodes.Ldarg_0);
                    il.Emit(field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, field);
                }
                else
                {
                    il.Emit(field.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
                    if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType);
                    il.Emit(OpCodes.Call, AccessTools.Method(typeof(GeneratedFieldWrites), nameof(RequireWrite)));
                    if (!field.IsStatic) il.Emit(OpCodes.Ldarg_0);
                    il.Emit(field.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
                    il.Emit(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field);
                }
                il.Emit(OpCodes.Ret);
                accessors.Add((field, address), method);
                return method;
            }
        }

        private static FieldInfo Field(int slot) { lock (sync) return fields[slot]; }
        private static void RequireWrite(int slot, object target, object value)
        {
            FieldInfo field = Field(slot);
            if (!PawnContinuity.BlocksWrite(field, target, value) && FieldWriteProtection.Allows(field, target, value)) return;
            Denied(field);
        }
        private static void RequireAddress(int slot, object target)
        {
            FieldInfo field = Field(slot);
            if (DynamicWritePolicy.AllowsNativeFields(GeneratedIntegrity.Owns(field)) && DynamicWritePolicy.AllowsNativeTarget(target)) return;
            Denied(field);
        }
        private static bool NeedsSaveCopy(int slot, object target)
        {
            FieldInfo field = Field(slot);
            if (DynamicWritePolicy.AllowsNativeFields(GeneratedIntegrity.Owns(field)) && DynamicWritePolicy.AllowsNativeTarget(target)) return false;
            if (Scribe.mode == LoadSaveMode.Saving) return true;
            Denied(field);
            return false;
        }
        private static void Denied(FieldInfo field)
        {
            DynamicWritePolicy.Denied();
            throw new UnauthorizedAccessException("Generated mutation of protected state was rejected: " + field.DeclaringType.FullName + "." + field.Name);
        }
    }
}
