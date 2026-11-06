using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Combat
{
    internal sealed class ManagedHealthCodec
    {
        internal readonly FieldInfo Storage;
        internal readonly MethodInfo Read, Write;
        private ManagedHealthCodec(FieldInfo storage, MethodInfo read, MethodInfo write) { Storage = storage; Read = read; Write = write; }

        internal static ManagedHealthCodec Discover(MethodInfo read, ManagedCombatIl il)
        {
            Type type = read.DeclaringType;
            if (read.IsStatic || read.ReturnType != typeof(float) || !ManagedCombatIl.Signature(read)
                || read.IsVirtual && !read.IsFinal && !type.IsSealed) return null;
            var reads = new HashSet<FieldInfo>();
            int budget = 1024;
            if (!Inspect(read, il, reads, null, new HashSet<MethodInfo>(), 0, ref budget)) return null;
            FieldInfo storage = ManagedCombatIl.Only(reads);
            if (storage == null || storage.DeclaringType != type || !Scalar(storage.FieldType)) return null;
            var writers = new List<MethodInfo>();
            foreach (MethodInfo write in ManagedCombatIl.Methods(type).Where(m => m.ReturnType == typeof(void)
                && (!m.IsVirtual || m.IsFinal || type.IsSealed)
                && (ManagedCombatIl.Signature(m, typeof(float)) || ManagedCombatIl.Signature(m, typeof(float), typeof(object))
                    || ManagedCombatIl.Signature(m, typeof(float), type))))
            {
                var fields = new HashSet<FieldInfo>();
                budget = 1024;
                if (!Inspect(write, il, fields, storage, new HashSet<MethodInfo>(), 0, ref budget) || fields.Any(f => f != storage)) continue;
                var body = il.Read(write);
                int[] stores = Enumerable.Range(0, body.Count).Where(i => body[i].opcode == OpCodes.Stfld).ToArray();
                if (stores.Length != 1 || !Equals(body[stores[0]].operand, storage)) continue;
                int cursor = stores[0] - 1;
                ManagedValueExpression value = ManagedValueExpression.Before(body, ref cursor);
                ManagedValueExpression owner = ManagedValueExpression.Before(body, ref cursor);
                if (owner?.IsThis == true && value?.DependsOnArgument(1) == true) writers.Add(write);
            }
            MethodInfo setter = ManagedCombatIl.Only(writers);
            return setter == null ? null : new ManagedHealthCodec(storage, read, setter);
        }

        internal bool Assignment(List<CodeInstruction> body, int index, out ManagedValueExpression value)
        {
            value = null;
            if (!Equals(body[index].operand, Write)) return false;
            int cursor = index - 1;
            if (!ManagedValueExpression.ReadInputs(body, ref cursor, Write.GetParameters().Length + 1, 0, out var args)
                || !args[0].IsThis || args.Length == 3 && !args[2].IsThis) return false;
            value = args[1];
            return true;
        }

        internal ManagedHealthReduction Reduction(List<CodeInstruction> body, int damageArgument)
        {
            int[] writes = Enumerable.Range(0, body.Count).Where(i => Equals(body[i].operand, Write)).ToArray();
            if (writes.Length != 1 || !Assignment(body, writes[0], out var value)) return null;
            return ManagedHealthReduction.Create(value, writes[0], typeof(float),
                v => Equals(v.Unwrapped().Operand, Read) && v.Unwrapped().Inputs.Length == 1 && v.Unwrapped().Inputs[0].IsThis,
                v => ManagedHealthReduction.ReadsDamage(v, damageArgument),
                Write.GetParameters().Length == 2 ? Write.GetParameters()[1].ParameterType : null);
        }

        internal IEnumerable<MethodInfo> Initializers(List<CodeInstruction> body)
        {
            for (int i = 0; i < body.Count; i++)
                if (Assignment(body, i, out var value) && value.Operand is MethodInfo call && value.Inputs.Length == 1
                    && value.Inputs[0].IsThis && call.DeclaringType == Read.DeclaringType && call.ReturnType == typeof(float)
                    && ManagedCombatIl.Signature(call) && call != Read) yield return call;
        }

        internal bool CanReadMaximum(MethodInfo method, ManagedCombatIl il) => CanReadMaximum(method, Storage, il);
        internal static bool CanReadMaximum(MethodInfo method, FieldInfo storage, ManagedCombatIl il)
        {
            int budget = 1024;
            var fields = new HashSet<FieldInfo>();
            return (!method.IsVirtual || method.IsFinal || method.DeclaringType.IsSealed)
                && Inspect(method, il, fields, null, new HashSet<MethodInfo>(), 0, ref budget, true) && !fields.Contains(storage);
        }

        private static bool Scalar(Type type) => type == typeof(string) || type == typeof(float) || type == typeof(double)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

        private static bool Inspect(MethodInfo method, ManagedCombatIl il, HashSet<FieldInfo> reads, FieldInfo write,
            HashSet<MethodInfo> path, int depth, ref int budget, bool definitions = false)
        {
            if (depth >= 6 || !path.Add(method) || method.IsAbstract || method.ContainsGenericParameters) return false;
            try
            {
                var body = il.Read(method);
                budget -= body.Count;
                if (body.Count == 0 || body.Count > 192 || budget < 0) return false;
                for (int i = 0; i < body.Count; i++)
                {
                    var line = body[i];
                    OpCode op = line.opcode;
                    if (op == OpCodes.Ldfld && line.operand is FieldInfo field)
                    {
                        if (definitions && typeof(Verse.Def).IsAssignableFrom(field.DeclaringType)) continue;
                        int cursor = i - 1;
                        if (field.DeclaringType != method.DeclaringType || ManagedValueExpression.Before(body, ref cursor)?.IsThis != true) return false;
                        reads.Add(field);
                    }
                    else if (op == OpCodes.Ldsfld && definitions && line.operand is FieldInfo definition
                        && typeof(Verse.Def).IsAssignableFrom(definition.FieldType)) continue;
                    else if (op == OpCodes.Stfld)
                    {
                        if (write == null || !Equals(line.operand, write)) return false;
                    }
                    else if (op == OpCodes.Call || op == OpCodes.Callvirt)
                    {
                        if (line.operand is not MethodInfo call) return false;
                        if (definitions && DefinitionLookup(call)) continue;
                        if (PureRuntimeCall(call)) continue;
                        bool definitionCall = definitions && typeof(Verse.Def).IsAssignableFrom(call.DeclaringType);
                        if (call.DeclaringType != method.DeclaringType && !definitionCall || call.IsVirtual && !call.IsFinal
                            || call.GetParameters().Any(p => !Scalar(p.ParameterType))
                            || !Inspect(call, il, reads, null, path, depth + 1, ref budget, definitions)) return false;
                        int cursor = i - 1;
                        if (!call.IsStatic && (!ManagedValueExpression.ReadInputs(body, ref cursor, call.GetParameters().Length + 1, 0, out var args)
                            || !args[0].IsThis)) return false;
                    }
                    else if (op == OpCodes.Ldsfld || op == OpCodes.Ldsflda || op == OpCodes.Ldflda || op == OpCodes.Stsfld
                        || op == OpCodes.Calli || op == OpCodes.Newobj || op == OpCodes.Newarr || op == OpCodes.Jmp
                        || op.Name.StartsWith("stind", StringComparison.Ordinal) || op.Name.StartsWith("stelem", StringComparison.Ordinal)
                        || op == OpCodes.Stobj || op == OpCodes.Cpobj || op == OpCodes.Initobj || op == OpCodes.Initblk || op == OpCodes.Cpblk) return false;
                }
                return true;
            }
            finally { path.Remove(method); }
        }

        private static bool DefinitionLookup(MethodInfo method) => method.IsStatic && method.DeclaringType.IsGenericType
            && method.DeclaringType.GetGenericTypeDefinition() == typeof(Verse.DefDatabase<>)
            && method.Name == nameof(Verse.DefDatabase<Verse.ThingDef>.GetNamed)
            && ManagedCombatIl.Signature(method, typeof(string), typeof(bool));

        private static bool PureRuntimeCall(MethodInfo method)
        {
            if (method.ReturnType == typeof(void) || method.ReturnType.IsPointer || method.GetParameters().Any(p => p.ParameterType.IsByRef || p.ParameterType.IsPointer)) return false;
            Type type = method.DeclaringType;
            if (type == typeof(object)) return method.Name == nameof(ReferenceEquals);
            if (method.GetParameters().Any(p => !p.ParameterType.IsPrimitive && p.ParameterType != typeof(string))) return false;
            if (type == typeof(BitConverter) || type == typeof(Math) || type == typeof(MathF)) return true;
            if (type == typeof(Convert)) return method.Name.StartsWith("To", StringComparison.Ordinal) && Scalar(method.ReturnType);
            if (type == typeof(string)) return method.Name is "Concat" or "Substring" or "StartsWith" or "EndsWith" or "get_Length" or "get_Chars";
            return type.IsPrimitive && method.Name is "ToString" or "Parse";
        }
    }
}
