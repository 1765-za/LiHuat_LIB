using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class FieldReferenceReads
    {
        private sealed class Source(MethodInfo method, object target) { internal readonly MethodInfo Method = method; internal readonly object Target = target; }
        private sealed class Reader(Delegate value) { internal readonly Delegate Value = value; }
        private sealed class Origin(MethodInfo method) { internal readonly MethodInfo Method = method; }
        private static readonly ConditionalWeakTable<Delegate, Source> sources = new();
        private static readonly ConditionalWeakTable<MethodInfo, Origin> copies = new();
        private static readonly ConditionalWeakTable<Delegate, Reader> readers = new();
        private static readonly Dictionary<MethodInfo, DynamicMethod> adapters = new();
        internal static void Record(Delegate result, MethodInfo source, object target)
        {
            if (result != null && source.ReturnType.IsByRef) sources.GetValue(result, _ => new Source(source, target));
        }
        internal static void Invalidate() => readers.Clear();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RecordCopy(MethodInfo copy, MethodInfo original)
        {
            CodePublicationAuthority.RequireCaller();
            if (copy.ReturnType.IsByRef) copies.GetValue(copy, _ => new Origin(original));
        }
        internal static MethodInfo Original(MethodInfo method)
        {
            while (copies.TryGetValue(method, out Origin origin)) method = origin.Method;
            return method;
        }

        internal static bool HasRead(List<CodeInstruction> code)
        {
            for (int i = 0; i + 1 < code.Count; i++) if (ReadAt(code, i)) return true;
            return false;
        }
        internal static void Rewrite(List<CodeInstruction> code)
        {
            for (int i = 0; i + 1 < code.Count; i++)
                if (ReadAt(code, i))
                {
                    MethodInfo invoke = (MethodInfo)code[i].operand;
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = Adapter(invoke);
                    code[i + 1].opcode = OpCodes.Nop;
                    code[i + 1].operand = null;
                }
        }
        private static bool ReadAt(List<CodeInstruction> code, int at)
        {
            if (code[at].opcode != OpCodes.Callvirt || code[at].operand is not MethodInfo invoke
                || !invoke.ReturnType.IsByRef || invoke.Name != "Invoke" || !typeof(Delegate).IsAssignableFrom(invoke.DeclaringType)
                || code[at + 1].labels.Count != 0 || code[at + 1].blocks.Count != 0) return false;
            Type value = invoke.ReturnType.GetElementType();
            return code[at + 1].opcode == OpCodes.Ldind_Ref && !value.IsValueType
                || code[at + 1].opcode == OpCodes.Ldobj && Equals(code[at + 1].operand, value);
        }

        private static Delegate Resolve(Delegate reference) => readers.GetValue(reference, Create).Value;
        private static Reader Create(Delegate reference)
        {
            if (!reference.HasSingleTarget) return new Reader(null);
            Delegate leaf = reference;
            while (leaf.Target is DynamicWritePolicy policy) leaf = policy.Original;
            Source source = sources.TryGetValue(reference, out var known) ? known
                : sources.TryGetValue(leaf, out known) ? known : new Source(leaf.Method, leaf.Target);
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try
            {
                DynamicMethod body = FieldValueReader.Build(Original(source.Method));
                if (body == null) return new Reader(null);
                Type[] parameters = reference.GetType().GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
                Type type = Expression.GetDelegateType(parameters.Concat(new[] { body.ReturnType }).ToArray());
                return new Reader(parameters.Length == body.GetParameters().Length ? body.CreateDelegate(type) : body.CreateDelegate(type, source.Target));
            }
            finally { DynamicWriteCatalogue.Building = previous; }
        }
        private static DynamicMethod Adapter(MethodInfo invoke)
        {
            if (adapters.TryGetValue(invoke, out var known)) return known;
            Type[] parameters = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
            Type result = invoke.ReturnType.GetElementType();
            Type readerType = Expression.GetDelegateType(parameters.Concat(new[] { result }).ToArray());
            var adapter = new DynamicMethod(Guid.NewGuid().ToString("N"), result, new[] { invoke.DeclaringType }.Concat(parameters).ToArray(), typeof(FieldReferenceReads).Module, true);
            ILGenerator il = adapter.GetILGenerator();
            Label original = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AccessTools.Method(typeof(FieldReferenceReads), nameof(Resolve)));
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brfalse, original); il.Emit(OpCodes.Castclass, readerType);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)(i + 1));
            il.Emit(OpCodes.Callvirt, readerType.GetMethod("Invoke")); il.Emit(OpCodes.Ret);
            il.MarkLabel(original); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)(i + 1));
            il.Emit(OpCodes.Callvirt, invoke); il.Emit(OpCodes.Ldobj, result); il.Emit(OpCodes.Ret);
            adapters.Add(invoke, adapter);
            return adapter;
        }
    }
}
