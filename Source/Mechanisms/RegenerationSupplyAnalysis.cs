using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class RegenerationSupplyAnalysis
    {
        private readonly LifeIlReader reader;
        internal RegenerationSupplyAnalysis(LifeIlReader reader) { this.reader = reader; }
        private static bool Shape(MethodInfo method) => method.IsStatic && !method.ContainsGenericParameters
            && (method.ReturnType == typeof(void) || method.ReturnType == typeof(Pawn))
            && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Map), typeof(PawnKindDef), typeof(Faction) });

        internal RegenerationSupplySite[] Find(IEnumerable<MethodInfo> reachable)
        {
            var factories = new HashSet<MethodInfo>();
            foreach (MethodInfo candidate in reachable.Where(Shape))
            {
                var analysis = new RegenerationAnalysis(reader);
                RegenerationEvidence evidence = analysis.Inspect(candidate);
                const RegenerationEvidence required = RegenerationEvidence.Generation | RegenerationEvidence.Spawn;
                if ((evidence & (required | RegenerationEvidence.Unknown)) != required) continue;
                if (analysis.Visited.Any(WritesPawnPool)) factories.Add(candidate);
            }
            var sites = new List<RegenerationSupplySite>();
            foreach (Type type in factories.Select(m => m.DeclaringType).Distinct())
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (LifeIlReader.HasMetadataBody(method)) sites.AddRange(FindSites(method, factories));
            return sites.ToArray();
        }

        private bool WritesPawnPool(MethodInfo method)
        {
            List<CodeInstruction> code = reader.Read(method);
            return code != null && code.Any(i => i.opcode == OpCodes.Ldsfld && i.operand is FieldInfo f && ContainsPawn(f.FieldType))
                && code.Any(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && i.operand is MethodInfo call
                    && call.Name == "Add" && call.DeclaringType.IsGenericType
                    && (call.DeclaringType.GetGenericTypeDefinition() == typeof(List<>) || call.DeclaringType.GetGenericTypeDefinition() == typeof(HashSet<>))
                    && call.GetParameters().Length == 1 && call.GetParameters()[0].ParameterType == typeof(Pawn));
        }
        private static bool ContainsPawn(Type type) => type == typeof(Pawn)
            || type.IsGenericType && type.GetGenericArguments().Any(ContainsPawn);

        internal IEnumerable<RegenerationSupplySite> FindSites(MethodInfo caller, HashSet<MethodInfo> factories)
        {
            List<CodeInstruction> code = reader.Read(caller);
            if (code == null) yield break;
            Type[] parameters = IndependentIl.Parameters(caller), locals = reader.Locals(caller);
            var occurrences = new Dictionary<MethodInfo, int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call || !(code[i].operand is MethodInfo factory) || !factories.Contains(factory)) continue;
                occurrences.TryGetValue(factory, out int occurrence); occurrences[factory] = occurrence + 1;
                if (i < 4 || code[i - 3].opcode != OpCodes.Ldfld || !(code[i - 3].operand is FieldInfo kind)
                    || kind.DeclaringType != typeof(Pawn) || kind.Name != nameof(Pawn.kindDef)
                    || code[i - 1].opcode != OpCodes.Callvirt || !(code[i - 1].operand is MethodInfo faction)
                    || faction.DeclaringType != typeof(Thing) || faction.Name != "get_Faction") continue;
                if (!Load(code[i - 4], out int index, out bool local) || !Load(code[i - 2], out int second, out bool secondLocal)
                    || index != second || local != secondLocal) continue;
                Type[] types = local ? locals : parameters;
                if (index < 0 || index >= types.Length || !typeof(Pawn).IsAssignableFrom(types[index])) continue;
                yield return new RegenerationSupplySite { Caller = caller, Factory = factory, Occurrence = occurrence,
                    SourceIndex = index, SourceLocal = local };
            }
        }
        private static bool Load(CodeInstruction instruction, out int index, out bool local)
        {
            OpCode op = instruction.opcode;
            local = op == OpCodes.Ldloc || op == OpCodes.Ldloc_S || op == OpCodes.Ldloc_0 || op == OpCodes.Ldloc_1 || op == OpCodes.Ldloc_2 || op == OpCodes.Ldloc_3;
            if (op == OpCodes.Ldarg_0 || op == OpCodes.Ldloc_0) index = 0;
            else if (op == OpCodes.Ldarg_1 || op == OpCodes.Ldloc_1) index = 1;
            else if (op == OpCodes.Ldarg_2 || op == OpCodes.Ldloc_2) index = 2;
            else if (op == OpCodes.Ldarg_3 || op == OpCodes.Ldloc_3) index = 3;
            else if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S || op == OpCodes.Ldloc || op == OpCodes.Ldloc_S)
                index = Convert.ToInt32(instruction.operand);
            else { index = -1; return false; }
            return true;
        }
    }
}
