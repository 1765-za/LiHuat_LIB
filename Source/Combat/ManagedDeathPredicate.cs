using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Combat
{
    internal sealed class ManagedDeathPredicate
    {
        private readonly ManagedCombatIl il;
        private readonly Type type;
        private int steps;
        internal FieldInfo Storage { get; private set; }
        internal ManagedHealthCodec Codec { get; private set; }
        internal int Core { get; private set; }
        private readonly record struct Value(int Kind, double Number = 0, int Mask = 0, FieldInfo Field = null);
        private sealed class Path
        {
            internal int At, Mask = 15;
            internal List<Value> Stack = new();
            internal Dictionary<int, Value> Locals = new();
            internal Value Pop() { Value value = Stack[^1]; Stack.RemoveAt(Stack.Count - 1); return value; }
            internal Path Fork(int at, int mask) => new() { At = at, Mask = mask, Stack = new(Stack), Locals = new(Locals) };
        }

        private ManagedDeathPredicate(Type type, ManagedCombatIl il) { this.type = type; this.il = il; }
        internal static ManagedDeathPredicate Read(MethodInfo method, ManagedCombatIl il)
        {
            var result = new ManagedDeathPredicate(method.DeclaringType, il);
            if (!result.Evaluate(method, 0, out Value value) || result.Storage == null || value.Kind != 4) return null;
            Type number = result.Codec != null ? typeof(float) : ManagedHealthStorage.Element(result.Storage.FieldType) ?? result.Storage.FieldType;
            int domain = number == typeof(int) ? 7 : 15;
            return (value.Mask & domain) == 3 ? result : null;
        }

        private bool Select(FieldInfo field, int core, ManagedHealthCodec codec = null)
        {
            if (Storage != null) return Storage == field && Core == core && Codec?.Read == codec?.Read;
            Storage = field; Core = core; Codec = codec; return true;
        }

        private bool Evaluate(MethodInfo method, int depth, out Value result)
        {
            result = default;
            if (depth > 4 || method.IsStatic || method.GetParameters().Length != 0 || method.DeclaringType != type
                || method.IsVirtual && !method.IsFinal && !type.IsSealed) return false;
            List<CodeInstruction> body = il.Read(method);
            if (body.Count == 0 || body.Count > 192) return false;
            var labels = new Dictionary<Label, int>();
            for (int i = 0; i < body.Count; i++) foreach (Label label in body[i].labels) labels[label] = i;
            var pending = new Stack<Path>(); pending.Push(new Path());
            int returned = 0, truth = 0;
            while (pending.Count > 0)
            {
                Path path = pending.Pop();
                while (path.At < body.Count)
                {
                    if (++steps > 2048 || path.Stack.Count > 32 || path.Locals.Count > 32) return false;
                    CodeInstruction line = body[path.At++]; OpCode op = line.opcode;
                    if (op == OpCodes.Nop) continue;
                    if (op == OpCodes.Ldarg_0) path.Stack.Add(new Value(1));
                    else if (ManagedCombatIl.Integer(line) is int integer) path.Stack.Add(new Value(0, integer));
                    else if (op == OpCodes.Ldc_R4 || op == OpCodes.Ldc_R8 || op == OpCodes.Ldc_I4_M1)
                        path.Stack.Add(new Value(0, op == OpCodes.Ldc_I4_M1 ? -1 : Convert.ToDouble(line.operand)));
                    else if (ManagedCombatIl.StoredLocal(op, line.operand) is int local)
                    {
                        if (path.Stack.Count == 0) return false;
                        path.Locals[local] = path.Pop();
                    }
                    else if (ManagedCombatIl.LoadedLocal(line) is int loaded)
                    {
                        if (!path.Locals.TryGetValue(loaded, out Value value)) return false;
                        path.Stack.Add(value);
                    }
                    else if (op == OpCodes.Ldfld)
                    {
                        if (path.Stack.Count == 0 || path.Pop().Kind != 1 || line.operand is not FieldInfo field
                            || field.DeclaringType != type || !ManagedHealthStorage.Supported(field.FieldType)) return false;
                        if (ManagedHealthStorage.Numeric(field.FieldType))
                        {
                            if (!Select(field, 0)) return false;
                            path.Stack.Add(new Value(3));
                        }
                        else path.Stack.Add(new Value(2, Field: field));
                    }
                    else if (op.Name.StartsWith("ldelem", StringComparison.Ordinal))
                    {
                        if (!Element(path)) return false;
                    }
                    else if (op == OpCodes.Call || op == OpCodes.Callvirt)
                    {
                        if (line.operand is not MethodInfo call) return false;
                        if (ManagedHealthStorage.Indexer(call, "get_Item")) { if (!Element(path)) return false; }
                        else
                        {
                            if (path.Stack.Count == 0 || path.Pop().Kind != 1 || call.DeclaringType != type
                                || call.GetParameters().Length != 0 || call.IsStatic) return false;
                            ManagedHealthCodec codec = call.ReturnType == typeof(float) ? ManagedHealthCodec.Discover(call, il) : null;
                            if (codec != null)
                            {
                                if (!Select(codec.Storage, 0, codec)) return false;
                                path.Stack.Add(new Value(3));
                            }
                            else
                            {
                                if (!Evaluate(call, depth + 1, out Value value)) return false;
                                path.Stack.Add(value);
                            }
                        }
                    }
                    else if (op == OpCodes.Conv_R4 || op == OpCodes.Conv_R8)
                    {
                        if (path.Stack.Count == 0 || path.Stack[^1].Kind is not (0 or 3)) return false;
                    }
                    else if (op == OpCodes.Ceq || op == OpCodes.Clt || op == OpCodes.Clt_Un || op == OpCodes.Cgt || op == OpCodes.Cgt_Un)
                    {
                        if (!Comparison(path, op, out int mask)) return false;
                        path.Stack.Add(new Value(4, Mask: mask));
                    }
                    else if (op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor)
                    {
                        if (path.Stack.Count < 2 || !Boolean(path.Pop(), out int right) || !Boolean(path.Pop(), out int left)) return false;
                        path.Stack.Add(new Value(4, Mask: op == OpCodes.And ? left & right : op == OpCodes.Or ? left | right : left ^ right));
                    }
                    else if (op == OpCodes.Dup) { if (path.Stack.Count == 0) return false; path.Stack.Add(path.Stack[^1]); }
                    else if (op == OpCodes.Pop) { if (path.Stack.Count == 0) return false; path.Pop(); }
                    else if (op.FlowControl == FlowControl.Branch)
                    {
                        if (line.operand is not Label label || !labels.TryGetValue(label, out path.At)) return false;
                    }
                    else if (op.FlowControl == FlowControl.Cond_Branch)
                    {
                        if (line.operand is not Label label || !labels.TryGetValue(label, out int target)) return false;
                        int mask;
                        if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
                        {
                            if (path.Stack.Count == 0 || !Boolean(path.Pop(), out mask)) return false;
                            if (op == OpCodes.Brfalse || op == OpCodes.Brfalse_S) mask ^= 15;
                        }
                        else if (!Comparison(path, op, out mask)) return false;
                        int taken = path.Mask & mask;
                        if (taken != 0) pending.Push(path.Fork(target, taken));
                        path.Mask &= ~mask;
                        if (path.Mask == 0) break;
                    }
                    else if (op == OpCodes.Ret)
                    {
                        if (path.Stack.Count != 1) return false;
                        Value value = path.Pop();
                        if (method.ReturnType == typeof(bool))
                        {
                            if (!Boolean(value, out int mask)) return false;
                            truth |= mask & path.Mask;
                        }
                        else if (!ManagedHealthStorage.Numeric(method.ReturnType) || value.Kind != 3) return false;
                        returned |= path.Mask;
                        break;
                    }
                    else return false;
                }
            }
            if (returned != 15) return false;
            result = method.ReturnType == typeof(bool) ? new Value(4, Mask: truth) : new Value(3);
            return true;
        }

        private bool Element(Path path)
        {
            if (path.Stack.Count < 2) return false;
            Value index = path.Pop(), owner = path.Pop();
            if (owner.Kind != 2 || index.Kind != 0 || index.Number != (int)index.Number || index.Number < 0
                || !Select(owner.Field, (int)index.Number)) return false;
            path.Stack.Add(new Value(3)); return true;
        }

        private static bool Boolean(Value value, out int mask)
        {
            mask = value.Kind == 4 ? value.Mask : value.Number == 0 ? 0 : 15;
            return value.Kind == 4 || value.Kind == 0 && (value.Number == 0 || value.Number == 1);
        }

        private static bool Comparison(Path path, OpCode op, out int mask)
        {
            mask = 0;
            if (path.Stack.Count < 2) return false;
            Value right = path.Pop(), left = path.Pop();
            if (left.Kind == 3 && (right.Kind != 0 || right.Number != 0)
                || right.Kind == 3 && (left.Kind != 0 || left.Number != 0)
                || left.Kind is not (0 or 3 or 4) || right.Kind is not (0 or 3 or 4)) return false;
            string name = op.Name;
            for (int i = 0; i < 4; i++)
            {
                double a = Sample(left, i), b = Sample(right, i);
                bool unordered = double.IsNaN(a) || double.IsNaN(b);
                bool yes = name.StartsWith("ceq") || name.StartsWith("beq") ? a == b
                    : name.StartsWith("bne") ? a != b
                    : name.StartsWith("bge") ? a >= b
                    : name.StartsWith("ble") ? a <= b
                    : name.StartsWith("cgt") || name.StartsWith("bgt") ? a > b
                    : name.StartsWith("clt") || name.StartsWith("blt") ? a < b : false;
                if (unordered && name.Contains(".un", StringComparison.Ordinal)) yes = true;
                if (yes) mask |= 1 << i;
            }
            return true;
        }
        private static double Sample(Value value, int sign) => value.Kind == 3 ? sign == 3 ? double.NaN : sign - 1
            : value.Kind == 4 ? (value.Mask >> sign) & 1 : value.Number;
    }
}
