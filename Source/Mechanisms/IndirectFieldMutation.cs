using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class IndirectFieldMutation
    {
        internal static bool Writes(List<CodeInstruction> code, Func<FieldInfo, bool> sensitive,
            Func<MethodInfo, bool> reference = null, Func<CodeInstruction, bool> address = null,
            Func<int, bool> argument = null, Func<MethodInfo, int, bool> callWrite = null)
        {
            if (code.Count == 0) return false;
            bool stores = false;
            foreach (CodeInstruction line in code)
                if (line.opcode == OpCodes.Initobj || line.opcode == OpCodes.Stobj || line.opcode == OpCodes.Cpobj
                    || line.opcode == OpCodes.Cpblk || line.opcode == OpCodes.Initblk
                    || line.opcode == OpCodes.Conv_I || line.opcode == OpCodes.Conv_U
                    || line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt
                    || line.opcode.Name.StartsWith("stind.", StringComparison.Ordinal)) { stores = true; break; }
            if (!stores) return false;
            var labels = new Dictionary<Label, int>();
            for (int i = 0; i < code.Count; i++) foreach (Label label in code[i].labels) labels[label] = i;
            var states = new Dictionary<int, State>();
            var pending = new Queue<int>();
            void Visit(int index, State state)
            {
                if (index < 0 || index >= code.Count) return;
                if (!states.TryGetValue(index, out State previous))
                { states[index] = state.Copy(); pending.Enqueue(index); }
                else if (previous.Merge(state)) pending.Enqueue(index);
            }
            Visit(0, new State());
            // Handler bodies can contain standalone writers even when no normal branch enters.
            for (int i = 0; i < code.Count; i++)
                foreach (ExceptionBlock block in code[i].blocks)
                    if (block.blockType == ExceptionBlockType.BeginCatchBlock || block.blockType == ExceptionBlockType.BeginExceptFilterBlock
                        || block.blockType == ExceptionBlockType.BeginFinallyBlock || block.blockType == ExceptionBlockType.BeginFaultBlock)
                    {
                        var handler = new State();
                        if (block.blockType == ExceptionBlockType.BeginCatchBlock || block.blockType == ExceptionBlockType.BeginExceptFilterBlock)
                            handler.Stack.Add(false);
                        Visit(i, handler);
                    }
            while (pending.Count > 0)
            {
                int index = pending.Dequeue();
                State state = states[index].Copy();
                List<bool> stack = state.Stack;
                Dictionary<int, bool> locals = state.Locals;
                CodeInstruction line = code[index];
                OpCode op = line.opcode;
                if (op == OpCodes.Dup) { stack.Add(stack.Count != 0 && stack[stack.Count - 1]); Visit(index + 1, state); continue; }
                int input = op == OpCodes.Ldarg || op == OpCodes.Ldarg_S ? Convert.ToInt32(line.operand)
                    : op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value ? op.Value - OpCodes.Ldarg_0.Value : -1;
                if (input >= 0) { stack.Add(argument?.Invoke(input) == true); Visit(index + 1, state); continue; }
                int local = Local(line, false);
                if (local >= 0) { locals[local] = Pop(stack); Visit(index + 1, state); continue; }
                local = Local(line, true);
                if (local >= 0) { stack.Add(locals.TryGetValue(local, out bool value) && value); Visit(index + 1, state); continue; }
                if (op == OpCodes.Initobj) { if (Pop(stack)) return true; Visit(index + 1, state); continue; }
                if (op == OpCodes.Stobj || op == OpCodes.Cpobj || op.Name.StartsWith("stind.", StringComparison.Ordinal))
                { Pop(stack); if (Pop(stack)) return true; Visit(index + 1, state); continue; }
                if (op == OpCodes.Cpblk || op == OpCodes.Initblk)
                { Pop(stack); Pop(stack); if (Pop(stack)) return true; Visit(index + 1, state); continue; }
                if (op == OpCodes.Conv_I || op == OpCodes.Conv_U)
                { if (Pop(stack)) return true; stack.Add(false); Visit(index + 1, state); continue; }
                if (op == OpCodes.Ldflda || op == OpCodes.Ldsflda)
                {
                    if (op == OpCodes.Ldflda) Pop(stack);
                    stack.Add(address != null ? address(line) : line.operand is FieldInfo field && sensitive(field)); Visit(index + 1, state); continue;
                }
                if (line.operand is MethodBase method && (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    bool alias = false;
                    for (int i = parameters.Length - 1; i >= 0; i--)
                    {
                        if (!Pop(stack)) continue;
                        alias = true;
                        if (method is MethodInfo helper && parameters[i].ParameterType.IsByRef
                            && (callWrite ?? ReferenceArgumentMutation.Writes)(helper, i + (helper.IsStatic ? 0 : 1))) return true;
                    }
                    if (!method.IsStatic && op != OpCodes.Newobj && Pop(stack))
                    {
                        alias = true;
                        if (method is MethodInfo helper && method.DeclaringType.IsValueType
                            && (callWrite ?? ReferenceArgumentMutation.Writes)(helper, 0)) return true;
                    }
                    if (op == OpCodes.Newobj || method is MethodInfo info && info.ReturnType != typeof(void))
                    {
                        FieldInfo field = UnsafeFieldMetadata.Resolve(method as MethodInfo);
                        stack.Add(alias && method is MethodInfo returning && DynamicWriteCatalogue.AddressResult(returning.ReturnType)
                            || field != null && sensitive(field)
                            || HarmonyFieldReference.Sensitive(method as MethodInfo, sensitive)
                            || method is MethodInfo accessor && reference?.Invoke(accessor) == true);
                    }
                    Visit(index + 1, state); continue;
                }
                int pops = PopCount(op.StackBehaviourPop);
                if (pops < 0) stack.Clear(); else for (int i = 0; i < pops; i++) Pop(stack);
                int pushes = PushCount(op.StackBehaviourPush);
                for (int i = 0; i < pushes; i++) stack.Add(false);
                if (op == OpCodes.Leave || op == OpCodes.Leave_S) stack.Clear();
                if (line.operand is Label target && labels.TryGetValue(target, out int branch)) Visit(branch, state);
                else if (line.operand is Label[] choices)
                    foreach (Label choice in choices) if (labels.TryGetValue(choice, out int branchIndex)) Visit(branchIndex, state);
                if (op.FlowControl != FlowControl.Branch && op.FlowControl != FlowControl.Return && op.FlowControl != FlowControl.Throw
                    && op != OpCodes.Endfinally && op != OpCodes.Endfilter) Visit(index + 1, state);
                else
                {
                    // Snapshot IL does not carry exception tables. Also inspect disconnected
                    // blocks with the known locals, as the previous linear scanner did.
                    stack.Clear(); Visit(index + 1, state);
                }
            }
            return false;
        }
        private sealed class State
        {
            internal readonly List<bool> Stack = new List<bool>();
            internal readonly Dictionary<int, bool> Locals = new Dictionary<int, bool>();
            internal State Copy()
            {
                var copy = new State(); copy.Stack.AddRange(Stack);
                foreach (var local in Locals) copy.Locals.Add(local.Key, local.Value);
                return copy;
            }
            internal bool Merge(State other)
            {
                bool changed = false;
                for (int i = 0; i < other.Stack.Count; i++)
                    if (i >= Stack.Count) { Stack.Add(other.Stack[i]); changed = true; }
                    else if (other.Stack[i] && !Stack[i]) { Stack[i] = true; changed = true; }
                foreach (var local in other.Locals)
                    if (local.Value && (!Locals.TryGetValue(local.Key, out bool previous) || !previous))
                    { Locals[local.Key] = true; changed = true; }
                return changed;
            }
        }
        private static bool Pop(List<bool> stack)
        {
            if (stack.Count == 0) return false;
            bool value = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); return value;
        }
        private static int Local(CodeInstruction line, bool load)
        {
            OpCode op = line.opcode;
            if (op == (load ? OpCodes.Ldloc_0 : OpCodes.Stloc_0)) return 0;
            if (op == (load ? OpCodes.Ldloc_1 : OpCodes.Stloc_1)) return 1;
            if (op == (load ? OpCodes.Ldloc_2 : OpCodes.Stloc_2)) return 2;
            if (op == (load ? OpCodes.Ldloc_3 : OpCodes.Stloc_3)) return 3;
            if (op != (load ? OpCodes.Ldloc : OpCodes.Stloc) && op != (load ? OpCodes.Ldloc_S : OpCodes.Stloc_S)) return -1;
            return line.operand is LocalBuilder local ? local.LocalIndex : Convert.ToInt32(line.operand);
        }
        private static int PopCount(StackBehaviour value) => value switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1 or StackBehaviour.Popref_popi_popi
                or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8
                or StackBehaviour.Popref_popi_popref => 3,
            _ => -1
        };
        private static int PushCount(StackBehaviour value) => value == StackBehaviour.Push0 ? 0 : value == StackBehaviour.Push1_push1 ? 2 : 1;
    }
}

