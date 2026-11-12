using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace LihuatLib.Combat
{
    internal static class ManagedServiceProof
    {
        internal static bool WritesValue(MethodInfo writer, HashSet<FieldInfo> reads, ManagedCombatIl il)
        {
            var pending = new Queue<(MethodInfo Method, int[] Amount)>(); pending.Enqueue((writer, new[] { 1 }));
            var seen = new HashSet<(MethodInfo, string)>(); bool writes = false; int budget = 2048;
            while (pending.Count > 0)
            {
                var (method, amount) = pending.Dequeue();
                if (!seen.Add((method, string.Join(",", amount)))) continue;
                var body = il.Read(method); budget -= body.Count;
                if (seen.Count > 16 || body.Count == 0 || budget < 0) return false;
                var flow = ManagedWriteFlow.Analyze(method, body);
                if (flow == null) return false;
                for (int i = 0; i < body.Count; i++)
                {
                    var line = body[i];
                    if (line.opcode == OpCodes.Stfld && line.operand is FieldInfo field)
                    {
                        var args = flow.Inputs(i, 2);
                        if (args == null) continue;
                        if (!ManagedWriteFlow.IsSubject(args[0])) return false;
                        writes |= reads.Contains(field) && Array.Exists(amount, argument => ManagedWriteFlow.Depends(args[1], argument));
                    }
                    else if (line.opcode == OpCodes.Stsfld || line.opcode == OpCodes.Calli || line.opcode.Name.StartsWith("stind") || line.opcode.Name.StartsWith("stelem")) return false;
                    else if (line.operand is MethodInfo call && call.IsStatic && call.ReturnType == typeof(void))
                    {
                        var parameters = call.GetParameters();
                        if (parameters.Length == 0 || parameters.Length > 6 || parameters[0].ParameterType != writer.GetParameters()[0].ParameterType) return false;
                        var args = flow.Inputs(i, parameters.Length);
                        if (args == null) continue;
                        if (!ManagedWriteFlow.IsSubject(args[0])) return false;
                        var forwarded = new List<int>();
                        for (int a = 1; a < args.Length; a++) if (Array.Exists(amount, argument => ManagedWriteFlow.Depends(args[a], argument))) forwarded.Add(a);
                        pending.Enqueue((call, forwarded.ToArray()));
                    }
                }
            }
            return writes;
        }

        internal static bool Lifecycle(Type target, MethodInfo route, MethodInfo read, ManagedCombatIl il)
        {
            var pending = new Queue<MethodInfo>(); pending.Enqueue(route);
            MethodInfo kill = target.GetMethod(nameof(Thing.Kill));
            if (kill != null && kill.DeclaringType.Assembly != typeof(Thing).Assembly) pending.Enqueue(kill);
            var seen = new HashSet<MethodInfo>(); bool reads = false, ends = false; int budget = 4096;
            while (pending.Count > 0)
            {
                var method = pending.Dequeue();
                if (!seen.Add(method)) continue;
                var body = il.Read(method); budget -= body.Count;
                if (seen.Count > 32 || budget < 0) return false;
                for (int i = 0; i < body.Count; i++)
                {
                    if (body[i].operand is not MethodInfo call) continue;
                    var args = call.GetParameters();
                    bool subject = call.IsStatic ? args.Length > 0 && args[0].ParameterType.IsAssignableFrom(target) : typeof(Thing).IsAssignableFrom(call.DeclaringType);
                    if (!subject) continue;
                    int at = i - 1;
                    if (!ManagedValueExpression.ReadInputs(body, ref at, args.Length + (call.IsStatic ? 0 : 1), 0, out var inputs) || !inputs[0].IsThis) continue;
                    if (call == read) { reads = true; continue; }
                    if (call.DeclaringType.Assembly == typeof(Thing).Assembly)
                        ends |= !call.IsStatic && (call.Name == nameof(Thing.Kill) || call.Name == nameof(Thing.Destroy));
                    else if (call.IsStatic && call.Module.Assembly == route.Module.Assembly) pending.Enqueue(call);
                }
            }
            return reads && ends;
        }
    }
}
