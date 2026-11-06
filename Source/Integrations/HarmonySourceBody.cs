using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;
using LihuatLib.Dispatching;

namespace LihuatLib.Integrations
{
    internal static class HarmonySourceBody
    {
        private static readonly MethodInfo getBody = AccessTools.Method(typeof(MethodBase), nameof(MethodBase.GetMethodBody));
        private static readonly MethodInfo sourceBody = AccessTools.Method(typeof(HarmonySourceBody), nameof(Read));
        private static MethodInfo creatorLocals;
        private static DynamicMethod declareLocals;
        private static bool installed;

        internal static void Install()
        {
            if (installed) return;
            Assembly harmony = typeof(Harmony).Assembly;
            ConstructorInfo reader = AccessTools.Constructor(harmony.GetType("HarmonyLib.MethodBodyReader", true),
                new[] { typeof(MethodBase), typeof(ILGenerator) });
            MethodInfo locals = AccessTools.Method(harmony.GetType("HarmonyLib.MethodPatcherTools", true), "DeclareOriginalLocalVariables",
                new[] { typeof(ILGenerator), typeof(MethodBase) });
            creatorLocals = AccessTools.Method(harmony.GetType("HarmonyLib.MethodCreatorTools", true), "DeclareOriginalLocalVariables",
                new[] { harmony.GetType("HarmonyLib.MethodCreator", true), typeof(MethodBase) });
            MethodInfo replacement = AccessTools.Method(harmony.GetType("HarmonyLib.MethodCreator", true), "CreateReplacement");
            MethodInfo copy = AccessTools.Method(harmony.GetType("MonoMod.Utils.DynamicMethodDefinition", true), "_CopyMethodToDefinition");
            declareLocals = IndependentIl.Clone(creatorLocals, rewrite: (il, opcode, operand) =>
            {
                if (!Equals(operand, getBody)) return false;
                il.Emit(OpCodes.Call, sourceBody);
                return true;
            });
            var rewrite = new DispatchMethod(AccessTools.Method(typeof(HarmonySourceBody), nameof(Transpiler)));
            NativeDispatcher.Register(reader, transpiler: rewrite);
            NativeDispatcher.Register(locals, transpiler: rewrite);
            NativeDispatcher.Register(copy, transpiler: rewrite);
            NativeDispatcher.Register(replacement, transpiler: new DispatchMethod(AccessTools.Method(typeof(HarmonySourceBody), nameof(DeclareSourceLocals))));
            NativeDispatcher.Register(AccessTools.DeclaredMethod(reader.DeclaringType, "FinalizeILCodes"),
                prefix: new DispatchMethod(typeof(HarmonySourceBody), nameof(IncludeTransforms)));
            installed = true;
        }

        private static void IncludeTransforms(MethodBase ___method, ref List<MethodInfo> transpilers) =>
            NativeDispatcher.IncludeTransforms(___method, ref transpilers);

        private static MethodBody Read(MethodBase method) => NativeRuntime.SourceBody(method);

        private static IEnumerable<CodeInstruction> DeclareSourceLocals(IEnumerable<CodeInstruction> instructions)
        {
            int declarations = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(creatorLocals))
                {
                    instruction.operand = declareLocals;
                    declarations++;
                }
                yield return instruction;
            }
            if (declarations != 1) throw new InvalidOperationException("Harmony local-variable declaration changed.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int reads = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getBody))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = sourceBody;
                    reads++;
                }
                yield return instruction;
            }
            if (reads != 1) throw new InvalidOperationException("Harmony source-body reader changed.");
        }
    }
}
