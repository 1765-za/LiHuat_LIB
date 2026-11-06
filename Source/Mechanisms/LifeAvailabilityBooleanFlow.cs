using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class LifeAvailabilityBooleanFlow
    {
        internal static bool Returns(LifeControlFlow flow, int at, bool negate) =>
            Result(flow, at, 1) == (negate ? 0 : 1) && Result(flow, at, 0) != (negate ? 0 : 1);

        private static int Result(LifeControlFlow flow, int at, int input)
        {
            var stack = new List<int> { input };
            var locals = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            int pc = at + 1;
            while (pc < flow.Code.Count && visited.Add(pc))
            {
                var line = flow.Code[pc];
                OpCode op = line.opcode;
                if (op == OpCodes.Ret) return Pop(stack);
                if (op == OpCodes.Nop) { pc++; continue; }
                if (op == OpCodes.Castclass) { pc++; continue; }
                if (op.FlowControl == FlowControl.Branch) { pc = flow.Next[pc][0]; continue; }
                if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
                {
                    int value = Pop(stack);
                    if (value < 0) return -1;
                    bool taken = (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S) ? value != 0 : value == 0;
                    pc = flow.Next[pc][taken ? 0 : 1]; continue;
                }
                if (op.FlowControl == FlowControl.Cond_Branch || op.FlowControl == FlowControl.Throw) return -1;
                if (op.Name.StartsWith("stloc", StringComparison.Ordinal)) locals[AvailabilityIlPatterns.Index(line)] = Pop(stack);
                else if (op.Name.StartsWith("ldloc", StringComparison.Ordinal))
                    stack.Add(locals.TryGetValue(AvailabilityIlPatterns.Index(line), out int value) ? value : -1);
                else if (op == OpCodes.Ldnull || op == OpCodes.Ldc_I4_0 || op == OpCodes.Ldc_I4_1) stack.Add(op == OpCodes.Ldc_I4_1 ? 1 : 0);
                else if (op == OpCodes.Ceq || op == OpCodes.Cgt_Un || op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor)
                {
                    int b = Pop(stack), a = Pop(stack);
                    stack.Add(a < 0 || b < 0 ? -1 : op == OpCodes.Ceq ? (a == b ? 1 : 0)
                        : op == OpCodes.Cgt_Un ? (a > b ? 1 : 0)
                        : op == OpCodes.And ? a & b : op == OpCodes.Or ? a | b : a ^ b);
                }
                else if (op == OpCodes.Dup) stack.Add(stack.Count > 0 ? stack[^1] : -1);
                else
                {
                    int pops = AvailabilityIlPatterns.Count(op.StackBehaviourPop), pushes = AvailabilityIlPatterns.Count(op.StackBehaviourPush);
                    if ((op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj) && line.operand is MethodBase call)
                    {
                        pops = call.GetParameters().Length + (!call.IsStatic && op != OpCodes.Newobj ? 1 : 0);
                        pushes = op == OpCodes.Newobj || call is MethodInfo result && result.ReturnType != typeof(void) ? 1 : 0;
                    }
                    if (pops < 0 || pushes < 0) return -1;
                    for (int i = 0; i < pops; i++) Pop(stack);
                    for (int i = 0; i < pushes; i++) stack.Add(-1);
                }
                pc++;
            }
            return -1;
        }

        private static int Pop(List<int> stack)
        { if (stack.Count == 0) return -1; int value = stack[^1]; stack.RemoveAt(stack.Count - 1); return value; }
    }
}
