using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Combat
{
    internal sealed class ManagedWriteFlow
    {
        private const uint Subject = 1u << 31;
        private readonly Frame[] states;
        private ManagedWriteFlow(Frame[] states) => this.states = states;
        internal static bool IsSubject(uint value) => (value & Subject) != 0;
        internal static bool Depends(uint value, int argument) => (value & (1u << argument)) != 0;
        internal uint[] Inputs(int instruction, int count)
        {
            var stack = states[instruction]?.Stack;
            return stack == null || stack.Count < count ? null : stack.Skip(stack.Count - count).ToArray();
        }

        internal static ManagedWriteFlow Analyze(MethodInfo method, List<CodeInstruction> body)
        {
            var metadata = NativeRuntime.SourceBody(method);
            if (!method.IsStatic || metadata == null || body.Count == 0 || body.Count > 512 || method.GetParameters().Length > 30) return null;
            try
            {
                var states = new Frame[body.Count];
                var pending = new Queue<int>(); var queued = new bool[body.Count];
                var labels = new Dictionary<Label, int>();
                for (int i = 0; i < body.Count; i++) foreach (Label label in body[i].labels) labels[label] = i;
                int[] offsets = ManagedReadAnalysis.Offsets(body);
                void Enqueue(int index, Frame frame)
                {
                    if (index < 0 || index >= body.Count) throw new InvalidProgramException();
                    bool changed = states[index] == null;
                    if (changed) states[index] = frame.Clone();
                    else changed = states[index].Merge(frame);
                    if (changed && !queued[index]) { queued[index] = true; pending.Enqueue(index); }
                }
                Enqueue(0, new Frame(metadata.LocalVariables.Count));
                int budget = 4096;
                while (pending.Count > 0)
                {
                    if (--budget < 0) return null;
                    int index = pending.Dequeue(); queued[index] = false;
                    Frame frame = states[index].Clone();
                    foreach (var clause in metadata.ExceptionHandlingClauses)
                    {
                        if (offsets[index] < clause.TryOffset || offsets[index] >= clause.TryOffset + clause.TryLength) continue;
                        if (clause.Flags == ExceptionHandlingClauseOptions.Filter) return null;
                        Frame handler = frame.Clone(); handler.Stack.Clear();
                        if (clause.Flags == ExceptionHandlingClauseOptions.Clause) handler.Stack.Add(0);
                        Enqueue(Array.IndexOf(offsets, clause.HandlerOffset), handler);
                    }
                    var line = body[index]; OpCode op = line.opcode;
                    if (op == OpCodes.Ret || op == OpCodes.Throw || op == OpCodes.Rethrow || op == OpCodes.Endfinally) continue;
                    if (op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
                    {
                        frame.Pop(Pops(op.StackBehaviourPop));
                        if (op == OpCodes.Leave || op == OpCodes.Leave_S) frame.Stack.Clear();
                        var targets = line.operand is Label single ? new[] { single } : line.operand as Label[];
                        if (targets == null) return null;
                        foreach (Label target in targets) Enqueue(labels[target], frame);
                        if (op.FlowControl == FlowControl.Cond_Branch) Enqueue(index + 1, frame);
                        continue;
                    }
                    if (!Step(line, frame)) return null;
                    Enqueue(index + 1, frame);
                }
                return new ManagedWriteFlow(states);
            }
            catch (Exception error) when (error is InvalidProgramException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException)
            { return null; }
        }

        private static bool Step(CodeInstruction line, Frame frame)
        {
            OpCode op = line.opcode;
            if (op == OpCodes.Nop || op == OpCodes.Readonly || op == OpCodes.Constrained) return true;
            if (op == OpCodes.Dup) { frame.Stack.Add(frame.Stack[^1]); return true; }
            if (op == OpCodes.Pop) { frame.Pop(1); return true; }
            int argument = op == OpCodes.Ldarg || op == OpCodes.Ldarg_S ? Convert.ToInt32(line.operand)
                : op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value ? op.Value - OpCodes.Ldarg_0.Value : -1;
            if (argument >= 0) { frame.Stack.Add((1u << argument) | (argument == 0 ? Subject : 0)); return true; }
            int? local = ManagedCombatIl.LoadedLocal(line), store = ManagedCombatIl.StoredLocal(op, line.operand);
            if (local.HasValue) { frame.Stack.Add(frame.Locals[local.Value]); return true; }
            if (store.HasValue) { frame.Locals[store.Value] = frame.Pop(1, true); return true; }
            if (op == OpCodes.Castclass || op == OpCodes.Isinst) { uint value = frame.Pop(1, true); frame.Stack.Add(value); return true; }
            if (op == OpCodes.Stfld) { frame.Pop(2); return true; }
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
            {
                var method = (MethodBase)line.operand;
                if (method.GetParameters().Any(parameter => parameter.ParameterType.IsByRef)) return false;
                uint value = frame.Pop(method.GetParameters().Length + (method.IsStatic || op == OpCodes.Newobj ? 0 : 1));
                if (op == OpCodes.Newobj || method is MethodInfo call && call.ReturnType != typeof(void)) frame.Stack.Add(value);
                return true;
            }
            if (op == OpCodes.Ldfld || op == OpCodes.Ldlen) { frame.Stack.Add(frame.Pop(1)); return true; }
            if (op == OpCodes.Ldsfld || op == OpCodes.Ldstr || op == OpCodes.Ldnull || op == OpCodes.Ldtoken || op.Name.StartsWith("ldc.", StringComparison.Ordinal))
            { frame.Stack.Add(0); return true; }
            if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S || op == OpCodes.Ldloca || op == OpCodes.Ldloca_S
                || op == OpCodes.Starg || op == OpCodes.Starg_S || op == OpCodes.Ldflda || op == OpCodes.Ldsflda
                || op == OpCodes.Stsfld || op == OpCodes.Calli || op == OpCodes.Cpblk || op == OpCodes.Initblk || op == OpCodes.Cpobj
                || op.Name.StartsWith("stind", StringComparison.Ordinal) || op.Name.StartsWith("stelem", StringComparison.Ordinal)) return false;
            int count = Pops(op.StackBehaviourPop);
            if (op.FlowControl != FlowControl.Next || count < 0 || op.StackBehaviourPush is not (StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8)) return false;
            frame.Stack.Add(frame.Pop(count)); return true;
        }

        private static int Pops(StackBehaviour pop) => pop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            _ => -1
        };
        private static uint Merge(uint left, uint right) => ((left | right) & ~Subject) | (left & right & Subject);

        private sealed class Frame
        {
            internal uint[] Locals;
            internal List<uint> Stack = new();
            internal Frame(int locals) => Locals = new uint[locals];
            internal Frame Clone() => new(0) { Locals = (uint[])Locals.Clone(), Stack = new(Stack) };
            internal uint Pop(int count, bool identity = false)
            {
                if (count < 0 || Stack.Count < count) throw new InvalidProgramException();
                uint value = 0;
                for (int i = 0; i < count; i++) { value |= Stack[^1]; Stack.RemoveAt(Stack.Count - 1); }
                return identity ? value : value & ~Subject;
            }
            internal bool Merge(Frame other)
            {
                if (Stack.Count != other.Stack.Count) throw new InvalidProgramException();
                bool changed = false;
                for (int i = 0; i < Locals.Length; i++)
                { uint value = ManagedWriteFlow.Merge(Locals[i], other.Locals[i]); changed |= value != Locals[i]; Locals[i] = value; }
                for (int i = 0; i < Stack.Count; i++)
                { uint value = ManagedWriteFlow.Merge(Stack[i], other.Stack[i]); changed |= value != Stack[i]; Stack[i] = value; }
                return changed;
            }
        }
    }
}
