using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeRetirementAnalysis
    {
        [Flags] private enum Effect { None = 0, Removal = 1, Damage = 2, Unknown = 4, ConditionalDecision = 8 }
        private readonly LifeIlReader reader;
        public int Unreadable { get; private set; }
        public LifeRetirementAnalysis(LifeIlReader reader) { this.reader = reader; }
        public int Inspect(MethodInfo method)
        {
            var visited = new HashSet<MethodInfo>();
            var pending = new Stack<MethodInfo>();
            pending.Push(method);
            Effect effect = Effect.None;
            bool indirectWrite = false;
            bool finalizesDeath = false;
            bool referenceWrite = false;
            bool stateWrite = false;
            bool rootStateCommit = false;
            LifeRetirementTarget rootStateTarget = LifeRetirementTarget.Argument;
            LifeRetirementTarget receiver = LifeRetirementTarget.Argument;
            bool receiverProven = true;
            bool hasRemoval = false;
            var indirect = new LifeIndirectCalls(reader);
            var bodies = new Dictionary<MethodInfo, List<CodeInstruction>>();
            var targets = new Dictionary<CodeInstruction, MethodInfo>();
            while (pending.Count > 0)
            {
                MethodInfo current = pending.Pop();
                if (!visited.Add(current)) continue;
                List<CodeInstruction> code = reader.Read(current);
                if (code == null) { Unreadable++; effect |= Effect.Unknown; continue; }
                bodies[current] = code;
                if (current == method) referenceWrite = LifeReferenceWrites.AffectsRecipient(method, code);
                if (IndirectFieldMutation.Writes(code, DynamicWriteCatalogue.Vital))
                { indirectWrite = true; effect |= Effect.Removal; }
                var indirectCalls = indirect.Find(code);
                if (current == method)
                {
                    for (int at = 0; at < code.Count; at++)
                    {
                        if (!indirect.StateCommits.Contains(code[at])) continue;
                        LifeRetirementTarget target = LifeRetirementReceiver.FieldStore(method, code, at);
                        rootStateTarget = !rootStateCommit || rootStateTarget == target ? target : LifeRetirementTarget.Argument;
                        rootStateCommit = true;
                    }
                }
                foreach (MethodInfo callback in indirect.InvokedCallbacks)
                    if (LifeResourceEvidence.IsCustom(callback.DeclaringType)
                        && LifeCallbackRecipient.Matches(current, code, callback, reader)) pending.Push(callback);
                foreach (var pair in indirectCalls) targets[pair.Key] = pair.Value;
                if (code.Exists(indirect.RemovalSites.Contains)) { indirectWrite = true; effect |= Effect.Removal; }
                for (int index = 0; index < code.Count; index++)
                {
                CodeInstruction line = code[index];
                if (LifeRetirementReceiver.ChangesParent(line)) receiverProven = false;
                if (line.opcode == OpCodes.Stfld && line.operand is FieldInfo field
                    && (field.DeclaringType == typeof(Thing) && (field.Name == "def" || field.Name == "thingIDNumber" || field.Name == "mapIndexOrState")
                        || field.DeclaringType == typeof(Pawn) && (field.Name == "health" || field.Name == "kindDef" || field.Name == "markedForDiscard")
                        || field.DeclaringType == typeof(Pawn_HealthTracker) && (field.Name == "healthState" || field.Name == "pawn" || field.Name == "hediffSet")
                        || field.DeclaringType == typeof(HediffSet) && field.Name == "pawn"))
                { stateWrite = true; effect |= Effect.Removal; }
                if (line.operand is Type type && typeof(Hediff_Injury).IsAssignableFrom(type)) effect |= Effect.Damage;
                MethodInfo call;
                if (!indirectCalls.TryGetValue(line, out call))
                {
                    if (line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt) continue;
                    call = line.operand as MethodInfo;
                }
                if (call == null) continue;
                if (call.DeclaringType == typeof(Pawn) && call.Name == "MakeCorpse"
                    || call.DeclaringType == typeof(PawnComponentsUtility) && call.Name == "RemoveComponentsOnKilled")
                    finalizesDeath = true;
                if (UnsafeFieldMetadata.Resolve(call) != null) continue;
                if (call.DeclaringType == typeof(Thing) && call.Name == "TakeDamage"
                    || typeof(DamageWorker).IsAssignableFrom(call.DeclaringType) && call.Name == "Apply"
                    || call.DeclaringType == typeof(Pawn_HealthTracker) && call.Name == "AddHediff"
                    || typeof(Hediff_Injury).IsAssignableFrom(call.DeclaringType)) effect |= Effect.Damage;
                else if (call.DeclaringType == typeof(MapPawns) && call.Name == "DeRegisterPawn"
                    || call.DeclaringType == typeof(ListerThings) && call.Name == "Remove"
                    || call.DeclaringType == typeof(ThingGrid) && call.Name == "Deregister"
                    || call.DeclaringType == typeof(TickManager) && call.Name == "DeRegisterAllTickabilityFor"
                    || call.DeclaringType == typeof(Pawn_HealthTracker) && call.Name == "SetDead"
                    || call.DeclaringType == typeof(PawnComponentsUtility) && (call.Name == "RemoveComponentsOnKilled" || call.Name == "RemoveComponentsOnDespawned")
                    || typeof(Entity).IsAssignableFrom(call.DeclaringType) && (call.Name == "Discard" || call.Name == "Destroy" || call.Name == "DeSpawn"
                        || call.Name == nameof(Thing.ForceSetStateToUnspawned) || call.Name == "Kill" || call.Name == "MakeCorpse")
                    || call.DeclaringType == typeof(Corpse) && call.Name == "set_InnerPawn")
                {
                    effect |= Effect.Removal;
                    LifeRetirementTarget observed = current == method
                        ? LifeRetirementReceiver.Find(method, code, index, call) : LifeRetirementTarget.Argument;
                    receiverProven &= observed != LifeRetirementTarget.Argument && (!hasRemoval || receiver == observed);
                    receiver = observed; hasRemoval = true;
                }
                else if (LifeResourceEvidence.IsCustom(call.DeclaringType) && !call.ContainsGenericParameters)
                    pending.Push(call);
                }
            }
            Unreadable += indirect.Unresolved;
            if (referenceWrite) return (int)Effect.Removal;
            if (rootStateCommit && effect == Effect.Removal)
                return (int)Effect.Removal | (int)(receiverProven ? rootStateTarget : LifeRetirementTarget.Argument);
            if ((effect & Effect.Removal) != 0 && effect != Effect.Removal && LifeEntryBlocking.Proves(method, bodies))
                return (int)Effect.Removal;
            if (LifeRetirementRecipient.UsesExplicitResult(method))
                return (int)(effect == Effect.Removal ? effect | Effect.ConditionalDecision : effect);
            if (indirectWrite && finalizesDeath && (effect & Effect.Damage) == 0) return (int)Effect.Removal;
            if (!indirectWrite && !stateWrite && effect == Effect.Removal && hasRemoval && receiverProven)
                return (int)effect | (int)receiver;
            if (!indirectWrite && effect == Effect.Removal && method.ReturnType == typeof(bool)
                && Preserves(method, bodies, targets)) effect |= Effect.ConditionalDecision;
            return (int)effect;
        }

        private static bool Preserves(MethodInfo root, Dictionary<MethodInfo, List<CodeInstruction>> bodies,
            Dictionary<CodeInstruction, MethodInfo> targets)
        {
            var proven = new HashSet<MethodInfo>();
            bool Removes(CodeInstruction line)
            {
                if (line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt) return false;
                MethodInfo call = targets.TryGetValue(line, out MethodInfo resolved) ? resolved : line.operand as MethodInfo;
                return call != null && (LifeRetirementFlow.Removes(call) || proven.Contains(call));
            }
            int before;
            do
            {
                before = proven.Count;
                foreach (var pair in bodies)
                    if (!proven.Contains(pair.Key) && !LifeRetirementFlow.HasPreservingReturn(pair.Value, Removes)) proven.Add(pair.Key);
            }
            while (proven.Count != before);
            return !proven.Contains(root);
        }
    }
}
