using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class LifeStackEffects
    {
        private static readonly Dictionary<StackBehaviour, int> counts = Counts();
        private static Dictionary<StackBehaviour, int> Counts()
        {
            var result = new Dictionary<StackBehaviour, int>();
            foreach (StackBehaviour behavior in Enum.GetValues(typeof(StackBehaviour)))
                result[behavior] = behavior == StackBehaviour.Pop0 || behavior == StackBehaviour.Push0 ? 0
                    : behavior == StackBehaviour.Varpop || behavior == StackBehaviour.Varpush ? -1 : behavior.ToString().Split('_').Length;
            return result;
        }

        internal static bool Apply(LifeFlowState state, OpCode opcode)
        {
            int pops = counts[opcode.StackBehaviourPop], pushes = counts[opcode.StackBehaviourPush];
            if (pops < 0 || pushes < 0) return false;
            LifeFlowValue value = LifeFlowValue.Empty;
            for (int i = 0; i < pops; i++)
            {
                LifeFlowValue item = state.Pop();
                if (pushes > 0) value = i == 0 ? item : LifeFlowValue.Union(value, item);
            }
            for (int i = 0; i < pushes; i++) state.Stack.Add(value);
            return true;
        }
    }
}
