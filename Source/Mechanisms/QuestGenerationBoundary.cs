using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using LihuatLib.Dispatching;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class QuestGenerationBoundary
    {
        internal static void Install()
        {
            var transform = new DispatchMethod(typeof(QuestGenerationBoundary), nameof(Rewrite))
                { priority = Priority.First, order = 100 };
            NativeDispatcher.Register(AccessTools.Method(typeof(QuestNode), nameof(QuestNode.Run)), transpiler: transform);
            NativeDispatcher.Register(AccessTools.Method(typeof(QuestGen), nameof(QuestGen.Generate)), transpiler: transform);
            NativeDispatcher.Register(AccessTools.Method(typeof(WorldComponentUtility), nameof(WorldComponentUtility.WorldComponentTick)), transpiler: transform);
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            List<CodeInstruction> code = instructions.ToList();
            bool node = __originalMethod.DeclaringType == typeof(QuestNode);
            bool step = __originalMethod.DeclaringType == typeof(WorldComponentUtility);
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.blocks.Any(block => block.blockType == ExceptionBlockType.BeginCatchBlock && block.catchType == typeof(Exception)))
                {
                    var entry = new CodeInstruction(OpCodes.Dup);
                    entry.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    entry.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    Label ordinary = generator.DefineLabel();
                    instruction.labels.Add(ordinary);
                    yield return entry;
                    yield return new CodeInstruction(OpCodes.Isinst, typeof(RegenerationDeniedException));
                    yield return new CodeInstruction(OpCodes.Brfalse, ordinary);
                    yield return new CodeInstruction(OpCodes.Pop);
                    if (step)
                    {
                        CodeInstruction leave = code.Skip(code.IndexOf(instruction) + 1)
                            .First(line => line.opcode == OpCodes.Leave || line.opcode == OpCodes.Leave_S);
                        yield return new CodeInstruction(OpCodes.Leave, leave.operand);
                    }
                    else
                    {
                        // Cancellation must escape inner quest catches before a partial quest can be published.
                        if (node)
                        {
                            Label closed = generator.DefineLabel();
                            yield return new CodeInstruction(OpCodes.Volatile);
                            yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(DeepProfiler), nameof(DeepProfiler.enabled)));
                            yield return new CodeInstruction(OpCodes.Brfalse, closed);
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DeepProfiler), nameof(DeepProfiler.End)));
                            var resume = new CodeInstruction(OpCodes.Nop);
                            resume.labels.Add(closed);
                            yield return resume;
                        }
                        yield return new CodeInstruction(OpCodes.Rethrow);
                    }
                }
                yield return instruction;
            }
        }
    }
}
