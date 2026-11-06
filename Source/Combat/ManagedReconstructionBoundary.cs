using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Combat
{
    public static class ManagedReconstructionBoundary
    {
        private static readonly List<ManagedCombatShape> plans = new();
        private static readonly Dictionary<string, int> tags = new();
        private static readonly HashSet<(string, MethodInfo)> installed = new();
        private static readonly MethodInfo gate = typeof(ManagedReconstructionBoundary).GetMethod(nameof(Allow), BindingFlags.Static | BindingFlags.NonPublic);
        public static long Blocked { get; private set; }

        internal static void Install(ManagedCombatShape shape, HashSet<MethodInfo> admissions, ManagedCombatIl il)
        {
            if (!tags.TryGetValue(shape.Id, out int tag)) { tags.Add(shape.Id, tag = plans.Count); plans.Add(shape); }
            foreach (MethodInfo method in ManagedCombatIl.Methods(shape.ManagerType).Where(SubjectCallbacks.Eligible))
            {
                if (installed.Contains((shape.Id, method)) || method.ReturnType != shape.StateType || method.GetParameters().Any(p => p.IsOut)) continue;
                var body = il.Read(method);
                if (body.Count(l => l.opcode == OpCodes.Newobj && l.operand is ConstructorInfo constructor && constructor.DeclaringType == shape.StateType) != 1
                    || !body.Any(l => l.operand is MethodInfo call && admissions.Contains(call))) continue;
                var keys = new HashSet<int>();
                for (int i = 0; i < body.Count; i++)
                {
                    if (!Equals(body[i].operand, shape.Lookup)) continue;
                    if (!ReturnsExisting(body, i)) continue;
                    int cursor = i - 1;
                    if (ManagedValueExpression.ReadInputs(body, ref cursor, 2, 0, out var inputs)
                        && inputs[0].IsThis && inputs[1].Argument > 0
                        && method.GetParameters()[inputs[1].Argument - 1].ParameterType == typeof(string)) keys.Add(inputs[1].Argument);
                }
                if (keys.Count != 1) continue;
                int argument = keys.Single();
                NativeIlGuards.RegisterTransform(method, typeof(ManagedReconstructionBoundary).FullName + ":" + shape.Id,
                    NativeIlGuards.GateOrder + 230, next => IndependentIl.Guard(method, next, gate, new[] { 0, argument }, tag));
                installed.Add((shape.Id, method));
            }
        }

        private static bool ReturnsExisting(List<HarmonyLib.CodeInstruction> body, int lookup)
        {
            if (lookup + 6 >= body.Count) return false;
            int? local = ManagedCombatIl.StoredLocal(body[lookup + 1].opcode, body[lookup + 1].operand);
            if (!local.HasValue || ManagedCombatIl.LoadedLocal(body[lookup + 2]) != local
                || ManagedCombatIl.LoadedLocal(body[lookup + 4]) != local || body[lookup + 5].opcode != OpCodes.Ret) return false;
            var branch = body[lookup + 3];
            return (branch.opcode == OpCodes.Brfalse || branch.opcode == OpCodes.Brfalse_S)
                && branch.operand is Label label && body[lookup + 6].labels.Contains(label)
                && body.Count(l => l.opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch) == 1;
        }

        private static bool Allow(int tag, object manager, string previous)
        {
            if (!((MapComponent)manager).map.GetComponent<CombatRetirementMap>().Contains(plans[tag].Id, previous)) return true;
            Blocked++;
            return false;
        }
    }
}
