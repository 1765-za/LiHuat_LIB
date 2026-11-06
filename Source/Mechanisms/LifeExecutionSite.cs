using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeExecutionSite
    {
        public MethodInfo Method;
        public MemberInfo Operation;
        public int Occurrence;
        public int ReceiverArgument;
        public FieldInfo[] ReceiverFields;
        public LifeObjectPath[] Receivers;
        public bool Indexed;
        public bool StateOwned;
        public bool RequiresBinding => !Indexed && !StateOwned;
        public int Id;
        public bool Installed;
        public List<CodeInstruction> OriginalCode;

        public bool Owns(object instance, Pawn pawn)
        {
            if (Receivers.Length == 0) return false;
            foreach (LifeObjectPath path in Receivers) if (!path.ReachesOwner(instance, pawn)) return false;
            return true;
        }
        public bool SameLocation(LifeExecutionSite other) => Method == other.Method && Operation == other.Operation && Occurrence == other.Occurrence;
    }

    internal sealed class LifeDeathEffect
    {
        public readonly LifeObjectPath[] Receivers;
        public LifeDeathEffect(IEnumerable<LifeObjectPath> receivers) { Receivers = LifeFlowSet.Collect(receivers).ToArray(); }
    }
}
