using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.StarBlood
{
    internal static class TickRuntimeRecovery
    {
        private static readonly MethodInfo tick = AccessTools.Method(typeof(Pawn), "Tick");
        private static readonly MethodInfo capture = AccessTools.Method(typeof(TickRuntimeRecovery), nameof(Capture));
        private static readonly MethodInfo entering = AccessTools.Method(typeof(StarBloodRules), nameof(StarBloodRules.EnterTickCallback));
        private static readonly MethodInfo returned = AccessTools.Method(typeof(StarBloodRules), nameof(StarBloodRules.ReturnTickCallback));
        private static readonly AccessTools.FieldRef<Pawn, Pawn_DrawTracker> drawer = AccessTools.FieldRefAccess<Pawn, Pawn_DrawTracker>("drawer");

        private static StarBloodRules Capture(Pawn pawn)
        {
            StarBloodRules rules = StarBloodRegistry.For(pawn);
            rules?.EnterTickCallback(pawn, "entry");
            return rules;
        }

        private static bool IsCallback(MethodInfo method) => method.ReturnType == typeof(void)
            && method.DeclaringType?.IsValueType == false && method.Module.Assembly == typeof(Pawn).Assembly;

        internal static Func<ILGenerator, OpCode, object, bool> IndependentRewrite(MethodInfo original, byte graph,
            Func<MethodInfo, bool, MethodInfo> resolver)
        {
            if (graph != 2 || original != tick || !LibraryCapabilities.Has(LibraryCapability.StarBlood)) return null;
            LocalBuilder rules = null;
            return (generator, opcode, operand) =>
            {
                if (rules == null)
                {
                    rules = generator.DeclareLocal(typeof(StarBloodRules));
                    foreach (CodeInstruction item in Prelude(rules)) Emit(generator, item);
                }
                if ((opcode != OpCodes.Call && opcode != OpCodes.Callvirt) || !(operand is MethodInfo called) || !IsCallback(called)) return false;
                foreach (CodeInstruction item in Boundary(generator, rules, called)) Emit(generator, item);
                MethodInfo replacement = resolver?.Invoke(called, opcode == OpCodes.Callvirt) ?? called;
                generator.Emit(replacement == called ? opcode : OpCodes.Call, replacement);
                foreach (CodeInstruction item in Boundary(generator, rules, null)) Emit(generator, item);
                return true;
            };
        }

        private static IEnumerable<CodeInstruction> Prelude(LocalBuilder rules)
        {
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, capture);
            yield return new CodeInstruction(OpCodes.Stloc, rules);
        }

        private static List<CodeInstruction> Boundary(ILGenerator generator, LocalBuilder rules, MethodInfo callback)
        {
            Label done = generator.DefineLabel();
            var result = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc, rules), new CodeInstruction(OpCodes.Brfalse, done),
                new CodeInstruction(OpCodes.Ldloc, rules), new CodeInstruction(OpCodes.Ldarg_0)
            };
            if (callback != null)
            {
                result.Add(new CodeInstruction(OpCodes.Ldstr, callback.DeclaringType.FullName + "." + callback.Name));
                result.Add(new CodeInstruction(OpCodes.Callvirt, entering));
            }
            else result.Add(new CodeInstruction(OpCodes.Callvirt, returned));
            var end = new CodeInstruction(OpCodes.Nop);
            end.labels.Add(done);
            result.Add(end);
            return result;
        }

        private static void Emit(ILGenerator generator, CodeInstruction instruction)
        {
            foreach (Label label in instruction.labels) generator.MarkLabel(label);
            if (instruction.operand == null) generator.Emit(instruction.opcode);
            else if (instruction.operand is LocalBuilder local) generator.Emit(instruction.opcode, local);
            else if (instruction.operand is Label label) generator.Emit(instruction.opcode, label);
            else if (instruction.operand is string value) generator.Emit(instruction.opcode, value);
            else if (instruction.operand is MethodInfo method) generator.Emit(instruction.opcode, method);
            else throw new InvalidOperationException("Unexpected tick boundary operand.");
        }

        internal static void Finish(Pawn pawn, Exception error)
        {
            StarBloodRules rules = StarBloodRegistry.For(pawn);
            if (rules?.Subject?.Pawn != pawn) return;
            if (error != null) rules.TraceTickFault(pawn, error, drawer(pawn));
            rules.EnterTickCallback(pawn, null);
        }
    }
}
