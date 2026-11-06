using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Combat
{
    public static class ManagedRegistrationBoundary
    {
        private static readonly List<ManagedCombatShape> plans = new();
        private static readonly Dictionary<string, int> tags = new();
        private static readonly HashSet<string> installed = new();
        private static readonly HashSet<(string, MethodInfo)> methods = new();
        private static readonly MethodInfo gate = typeof(ManagedRegistrationBoundary).GetMethod(nameof(Allow), BindingFlags.Static | BindingFlags.NonPublic);
        public static long Blocked { get; private set; }

        internal static void Install(ManagedCombatShape shape)
        {
            if (installed.Contains(shape.Id)) return;
            if (!tags.TryGetValue(shape.Id, out int tag)) { tags.Add(shape.Id, tag = plans.Count); plans.Add(shape); }
            var il = new ManagedCombatIl();
            var admissions = new HashSet<MethodInfo>();
            foreach (MethodInfo method in ManagedCombatIl.Methods(shape.ManagerType).Where(SubjectCallbacks.Eligible))
            {
                ParameterInfo[] parameters = method.GetParameters();
                int[] inputs = Enumerable.Range(0, parameters.Length).Where(i => parameters[i].ParameterType == shape.StateType).ToArray();
                if (inputs.Length != 1 || parameters.Any(p => p.IsOut)) continue;
                int argument = inputs[0] + 1;
                var body = il.Read(method);
                bool adds = false;
                for (int i = 0; i < body.Count; i++)
                {
                    if (!ManagedCombatIl.Calls(body[i], shape.Membership.FieldType, "Add")) continue;
                    int cursor = i - 1;
                    if (!ManagedValueExpression.ReadInputs(body, ref cursor, 2, 0, out var values)) continue;
                    if (values[1].Argument != argument || !Equals(values[0].Operand, shape.Membership)
                        || values[0].Inputs.Length != 1 || !values[0].Inputs[0].IsThis) continue;
                    adds = true;
                }
                if (adds)
                {
                    admissions.Add(method);
                    if (methods.Contains((shape.Id, method))) continue;
                    NativeIlGuards.RegisterTransform(method, typeof(ManagedRegistrationBoundary).FullName + ":" + shape.Id,
                        NativeIlGuards.GateOrder + 220, next => IndependentIl.Guard(method, next, gate, new[] { 0, argument }, tag));
                    methods.Add((shape.Id, method));
                }
            }
            ManagedReconstructionBoundary.Install(shape, admissions, il);
            installed.Add(shape.Id);
        }

        private static bool Allow(int tag, object manager, object state)
        {
            if (state == null) return true;
            ManagedCombatShape shape = plans[tag];
            Map map = ((MapComponent)manager).map;
            bool denied = LihuatGame.Current?.RetiredSubjects.Contains(state) == true
                || map.GetComponent<CombatRetirementMap>().Contains(shape.Id, shape.Key(state));
            if (!denied) return true;
            Blocked++;
            return false;
        }
    }
}
