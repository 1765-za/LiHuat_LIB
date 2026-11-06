using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Combat
{
    internal sealed class ManagedReadAnalysis
    {
        private readonly ManagedCombatIl il;
        private readonly HashSet<FieldInfo> fields;
        private readonly Action<FieldInfo> register;
        private readonly bool factory;
        private readonly bool allowLoops;
        private readonly ManagedNativeReadPlan native;
        private readonly HashSet<MethodInfo> path = new();
        private readonly ManagedReadSummary summaries = new();
        internal IEnumerable<MethodInfo> Methods => summaries.Methods;
        private int budget = 30000;
        internal string Failure { get; private set; }
        internal ManagedReadAnalysis(ManagedCombatIl il, HashSet<FieldInfo> fields, Action<FieldInfo> register, bool factory = false)
            : this(il, fields, register, factory, !factory) { }
        internal ManagedReadAnalysis(ManagedCombatIl il, HashSet<FieldInfo> fields, Action<FieldInfo> register, bool factory, bool allowLoops)
        { this.il = il; this.fields = fields; this.register = register; this.factory = factory; this.allowLoops = allowLoops; }
        internal ManagedReadAnalysis(ManagedCombatIl il, HashSet<FieldInfo> fields, Action<FieldInfo> register, ManagedNativeReadPlan native)
            : this(il, fields, register, false, true) { this.native = native; }

        internal bool Inspect(MethodInfo method)
        {
            if (!method.IsStatic) return false;
            var args = method.GetParameters().Select((p, i) => ManagedReadValue.For(p.ParameterType) with { Subject = !factory && i == 0 }).ToArray();
            bool valid = Analyze(method, args, out _, out _);
            return valid && (native == null || !native.HasExits || native.HasBridge);
        }

        private bool Analyze(MethodInfo method, ManagedReadValue[] arguments, out ManagedReadValue returned, out ManagedReadValue[] outputs)
        {
            returned = ManagedReadValue.Scalar(); outputs = arguments;
            if (summaries.TryGet(method, arguments, out returned, out outputs)) return true;
            if (path.Count >= 16 || summaries.Count >= 256 || !path.Add(method))
            { Failure ??= "Read call graph exceeds its budget: " + method; return false; }
            try
            {
                var body = il.Read(method); var metadata = method.GetMethodBody();
                if (body.Count == 0 || body.Count > 512 || metadata == null) return false;
                int[] offsets = Offsets(body);
                var labels = new Dictionary<Label, int>();
                for (int i = 0; i < body.Count; i++) foreach (Label label in body[i].labels) labels[label] = i;
                var states = new ManagedReadFrame[body.Count];
                var pending = new Queue<int>();
                var queued = new bool[body.Count];
                bool invalid = false, hasReturn = false;
                void Enqueue(int index, ManagedReadFrame state)
                {
                    if (index < 0 || index >= body.Count) { invalid = true; return; }
                    bool update = states[index] == null;
                    if (update) states[index] = state.Clone();
                    else if (!states[index].Merge(state, out bool changed)) invalid = true;
                    else update = changed;
                    if (update && !queued[index]) { queued[index] = true; pending.Enqueue(index); }
                }
                Enqueue(0, new ManagedReadFrame { Arguments = (ManagedReadValue[])arguments.Clone(),
                    WritableArguments = arguments.Select(value => value.Writable).ToArray(),
                    Locals = metadata.LocalVariables.Select(local => ManagedReadValue.For(local.LocalType)).ToArray() });
                while (pending.Count > 0 && !invalid)
                {
                    int index = pending.Dequeue();
                    queued[index] = false;
                    if (--budget < 0) { Failure ??= "Read flow exceeds its work budget: " + method; return false; }
                    ManagedReadFrame frame = states[index].Clone();
                    foreach (ExceptionHandlingClause clause in metadata.ExceptionHandlingClauses)
                    {
                        if (offsets[index] < clause.TryOffset || offsets[index] >= clause.TryOffset + clause.TryLength) continue;
                        if (clause.Flags == ExceptionHandlingClauseOptions.Filter) return false;
                        var handler = frame.Clone(); handler.Stack.Clear();
                        if (clause.Flags == ExceptionHandlingClauseOptions.Clause) handler.Stack.Add(ManagedReadValue.External(typeof(Exception)));
                        Enqueue(Array.IndexOf(offsets, clause.HandlerOffset), handler);
                    }
                    var line = body[index]; OpCode op = line.opcode;
                    if (op == OpCodes.Ret)
                    {
                        var value = method.ReturnType == typeof(void) ? ManagedReadValue.Scalar() : frame.Resolve(frame.Pop());
                        returned = hasReturn ? ManagedReadValue.Merge(returned, value) : value;
                        if (!hasReturn) outputs = (ManagedReadValue[])frame.Arguments.Clone();
                        else for (int a = 0; a < outputs.Length; a++) outputs[a] = ManagedReadValue.Merge(outputs[a], frame.Arguments[a]);
                        hasReturn = true; continue;
                    }
                    if (op == OpCodes.Endfinally || op == OpCodes.Throw || op == OpCodes.Rethrow) continue;
                    if (op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch)
                    {
                        int pops = Pops(op.StackBehaviourPop);
                        if (pops < 0) return false;
                        for (int p = 0; p < pops; p++) frame.Pop();
                        if (op == OpCodes.Leave || op == OpCodes.Leave_S) frame.Stack.Clear();
                        Label[] targets = line.operand is Label single ? new[] { single } : line.operand as Label[];
                        if (targets == null) return false;
                        foreach (var target in targets)
                        {
                            if (!labels.TryGetValue(target, out int next)) return false;
                            if (!allowLoops && next <= index) { Failure ??= "Unbounded delegate or factory loop: " + method; return false; }
                            Enqueue(next, frame);
                        }
                        if (op.FlowControl == FlowControl.Cond_Branch) Enqueue(index + 1, frame);
                        continue;
                    }
                    int stackDepth = frame.Stack.Count;
                    if (!Step(method, line, frame))
                    {
                        Failure ??= method.DeclaringType.FullName + "." + method.Name + " #" + index + " " + op.Name + " " + line.operand;
                        if (native != null) { native.Exit(method, index, stackDepth); continue; }
                        return false;
                    }
                    Enqueue(index + 1, frame);
                }
                if (invalid || !hasReturn) return false;
                summaries.Add(method, arguments, returned, outputs);
                return true;
            }
            catch (Exception error) when (error is InvalidProgramException or ArgumentException or NotSupportedException or IndexOutOfRangeException)
            { Failure ??= method.Name + ": " + error.GetType().Name; return false; }
            finally { path.Remove(method); }
        }

        private bool Step(MethodInfo method, CodeInstruction line, ManagedReadFrame frame)
        {
            OpCode op = line.opcode;
            if (op == OpCodes.Nop || op == OpCodes.Readonly || op == OpCodes.Constrained) return true;
            if (op == OpCodes.Dup) { frame.Stack.Add(frame.Stack[^1]); return true; }
            if (op == OpCodes.Pop) { frame.Pop(); return true; }
            int? argument = Argument(line);
            if (argument.HasValue)
            {
                int slot = argument.Value;
                bool address = op == OpCodes.Ldarga || op == OpCodes.Ldarga_S;
                bool reference = method.GetParameters()[slot].ParameterType.IsByRef;
                if (address && reference) return false;
                frame.Stack.Add(address || reference ? frame.Arguments[slot] with { Address = true, Argument = slot,
                    Writable = address || frame.WritableArguments[slot] } : frame.Arguments[slot]);
                return true;
            }
            int? store = ManagedCombatIl.StoredLocal(op, line.operand), load = ManagedCombatIl.LoadedLocal(line);
            if (store.HasValue) { frame.Locals[store.Value] = frame.Resolve(frame.Pop()); return true; }
            if (load.HasValue) { frame.Stack.Add(frame.Locals[load.Value]); return true; }
            if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
            { int slot = Convert.ToInt32(line.operand); frame.Stack.Add(frame.Locals[slot] with { Address = true, Writable = true, Local = slot }); return true; }
            if (op == OpCodes.Ldfld || op == OpCodes.Ldflda)
            {
                var owner = frame.Resolve(frame.Pop()); var field = (FieldInfo)line.operand;
                if (owner.Subject) fields.Add(field);
                var value = ManagedReadValue.For(field.FieldType);
                if (op == OpCodes.Ldflda) value = value with { Address = true, Writable = owner.Origin == ReadOrigin.Owned };
                frame.Stack.Add(value); return true;
            }
            if (op == OpCodes.Ldsfld)
            {
                var field = (FieldInfo)line.operand;
                if (factory && field.DeclaringType != typeof(OpCodes)) return false;
                if (!field.IsInitOnly && !field.IsLiteral && field.DeclaringType != typeof(OpCodes)
                    && (native == null || !field.FieldType.IsPrimitive && !typeof(Delegate).IsAssignableFrom(field.FieldType))) return false;
                fields.Add(field);
                frame.Stack.Add(ManagedReadValue.For(field.FieldType) with { Delegate = field }); return true;
            }
            if (op == OpCodes.Stfld)
            {
                frame.Pop(); var owner = frame.Pop();
                return owner.Address && owner.Writable || frame.Resolve(owner).Origin == ReadOrigin.Owned;
            }
            if (op == OpCodes.Initobj) return frame.Write(frame.Pop(), ManagedReadValue.For((Type)line.operand));
            if (op == OpCodes.Stobj || op.Name.StartsWith("stind", StringComparison.Ordinal))
            { var value = frame.Resolve(frame.Pop()); return frame.Write(frame.Pop(), value); }
            if (op == OpCodes.Ldobj || op.Name.StartsWith("ldind", StringComparison.Ordinal))
            { frame.Stack.Add(frame.Resolve(frame.Pop()) with { Address = false, Writable = false, Local = -1, Argument = -1 }); return true; }
            if (op.Name.StartsWith("stelem", StringComparison.Ordinal))
            { frame.Pop(); frame.Pop(); return frame.Resolve(frame.Pop()).Origin == ReadOrigin.Owned; }
            if (op.Name.StartsWith("ldelem", StringComparison.Ordinal))
            {
                frame.Pop(); var array = frame.Resolve(frame.Pop());
                var element = ManagedReadValue.For(array.Type?.GetElementType() ?? typeof(object));
                frame.Stack.Add(op == OpCodes.Ldelema ? element with { Address = true, Writable = array.Origin == ReadOrigin.Owned } : element); return true;
            }
            if (op == OpCodes.Localloc || op == OpCodes.Newarr)
            {
                var size = frame.Resolve(frame.Pop());
                if (!size.Constant.HasValue || size.Constant < 0 || size.Constant > 65536) return false;
                frame.Stack.Add(ManagedReadValue.Owned(op == OpCodes.Newarr ? ((Type)line.operand).MakeArrayType() : typeof(IntPtr))); return true;
            }
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj) return Call((MethodBase)line.operand, op == OpCodes.Newobj, frame);
            if (op == OpCodes.Ldstr || op == OpCodes.Ldnull || op == OpCodes.Ldtoken || op == OpCodes.Ldftn || op.Name.StartsWith("ldc.", StringComparison.Ordinal))
            { frame.Stack.Add(ManagedReadValue.Scalar(op == OpCodes.Ldstr ? typeof(string) : null, ManagedCombatIl.Integer(line))); return true; }
            if (op.Name.StartsWith("conv.", StringComparison.Ordinal) || op == OpCodes.Castclass || op == OpCodes.Isinst)
            { var value = frame.Pop(); frame.Stack.Add(value with { Type = line.operand as Type ?? value.Type }); return true; }
            if (op == OpCodes.Starg || op == OpCodes.Starg_S)
            { int slot = Convert.ToInt32(line.operand); if (method.GetParameters()[slot].ParameterType.IsByRef) return false; frame.Arguments[slot] = frame.Resolve(frame.Pop()); return true; }
            if (op == OpCodes.Ldlen) { frame.Pop(); frame.Stack.Add(ManagedReadValue.Scalar(typeof(int))); return true; }
            if (op.FlowControl != FlowControl.Next || op == OpCodes.Stsfld || op == OpCodes.Cpblk || op == OpCodes.Initblk
                || op == OpCodes.Cpobj || op == OpCodes.Ldsflda || op == OpCodes.Calli || op == OpCodes.Mkrefany || op == OpCodes.Refanyval) return false;
            int count = Pops(op.StackBehaviourPop);
            if (count < 0 || op.StackBehaviourPush != StackBehaviour.Push1 && op.StackBehaviourPush != StackBehaviour.Pushi) return false;
            for (int i = 0; i < count; i++) frame.Pop();
            frame.Stack.Add(ManagedReadValue.Scalar()); return true;
        }

        private bool Call(MethodBase method, bool construct, ManagedReadFrame frame)
        {
            int count = method.GetParameters().Length + (method.IsStatic || construct ? 0 : 1);
            var args = new ManagedReadValue[count];
            for (int i = count - 1; i >= 0; i--) args[i] = frame.Pop();
            bool allowed = ManagedReadFramework.Call(method, args, frame, factory, out var result, out bool known);
            if (!known)
            {
                if (construct && method is ConstructorInfo ctor)
                { allowed = ManagedSnapshotReader.SnapshotConstructor(ctor); result = ManagedReadValue.Scalar(ctor.DeclaringType); }
                else if (method is MethodInfo call)
                {
                    Type owner = call.DeclaringType;
                    if (owner.IsGenericType && owner.GetGenericTypeDefinition() == typeof(Lazy<>) && call.Name == "get_Value")
                    { result = frame.Resolve(args[0]) with { Type = call.ReturnType }; allowed = result.Delegate != null; }
                    else if (typeof(Delegate).IsAssignableFrom(owner) && call.Name == "Invoke")
                    {
                        FieldInfo source = frame.Resolve(args[0]).Delegate;
                        bool bridge = native?.Invoke(source, call, args, frame) == true;
                        allowed = bridge || !ManagedNativeReadPlan.Unmanaged(owner) && source != null
                            && call.ReturnType.IsPrimitive && call.GetParameters().All(p => p.ParameterType.IsPrimitive);
                        if (allowed && !bridge) register(source);
                        result = ManagedReadValue.For(call.ReturnType);
                    }
                    else if (call.IsStatic && call.Module.Assembly != typeof(object).Assembly)
                    {
                        var supplied = args.Select(frame.Resolve).ToArray();
                        var parameters = call.GetParameters();
                        for (int i = 0; i < supplied.Length; i++)
                            if (parameters[i].ParameterType.IsByRef) supplied[i] = supplied[i] with { Writable = args[i].Address && args[i].Writable };
                        allowed = Analyze(call, supplied, out result, out var outputs);
                        if (allowed) for (int i = 0; i < supplied.Length; i++)
                            if (parameters[i].ParameterType.IsByRef && args[i].Writable) frame.Write(args[i], outputs[i]);
                    }
                }
            }
            if (!allowed) return false;
            if (construct || method is MethodInfo info && info.ReturnType != typeof(void)) frame.Stack.Add(result);
            return true;
        }

        private static int? Argument(CodeInstruction line)
        {
            OpCode op = line.opcode;
            if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S || op == OpCodes.Ldarga || op == OpCodes.Ldarga_S) return Convert.ToInt32(line.operand);
            return op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value ? op.Value - OpCodes.Ldarg_0.Value : null;
        }
        private static int Pops(StackBehaviour pop) => pop switch
        { StackBehaviour.Pop0 => 0, StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2, _ => -1 };

        internal static int[] Offsets(List<CodeInstruction> body)
        {
            int cursor = 0; var offsets = new int[body.Count];
            for (int i = 0; i < body.Count; i++)
            {
                offsets[i] = cursor;
                cursor += body[i].opcode.Size + (body[i].opcode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * ((Label[])body[i].operand).Length, _ => 4
                });
            }
            return offsets;
        }
    }
}
