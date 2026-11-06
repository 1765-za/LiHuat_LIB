using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeEntryBlocking
    {
        private sealed class Command
        {
            internal readonly List<CodeInstruction> Body;
            internal readonly HashSet<int> Terminals = new();
            internal Command(List<CodeInstruction> body) { Body = body; }
        }

        internal static bool Proves(MethodInfo root, Dictionary<MethodInfo, List<CodeInstruction>> bodies)
        {
            if (LifeRetirementRecipient.UsesExplicitResult(root) || LifeEntryTarget.Argument(root) < 0) return false;
            var commands = new Dictionary<MethodInfo, Command>();
            var callers = new Dictionary<MethodInfo, List<(MethodInfo Method, int Site)>>();
            var discover = new Queue<MethodInfo>();
            var seen = new HashSet<MethodInfo>();
            discover.Enqueue(root);
            while (discover.Count != 0)
            {
                MethodInfo method = discover.Dequeue();
                if (!seen.Add(method) || !bodies.TryGetValue(method, out var body)) continue;
                var target = new LifeEntryTarget(method, body);
                if (!target.Stable) continue;
                var command = new Command(body);
                commands.Add(method, command);
                for (int at = 0; at < body.Count; at++)
                {
                    CodeInstruction line = body[at];
                    if (line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt || line.operand is not MethodInfo call) continue;
                    int input = TerminalInput(call);
                    if (input >= 0)
                    {
                        if (target.Receives(at, call, input)) command.Terminals.Add(at);
                        continue;
                    }
                    if (line.opcode == OpCodes.Callvirt && call.IsVirtual && !call.IsFinal && !call.DeclaringType.IsSealed) continue;
                    if (!bodies.ContainsKey(call) || (input = LifeEntryTarget.Argument(call)) < 0
                        || !target.Receives(at, call, input)) continue;
                    if (!callers.TryGetValue(call, out var parents)) callers.Add(call, parents = new());
                    parents.Add((method, at));
                    discover.Enqueue(call);
                }
            }
            var proven = new HashSet<MethodInfo>();
            var ready = new Queue<MethodInfo>();
            foreach (var pair in commands)
                if (LifeEntryFlow.Terminates(pair.Value.Body, pair.Value.Terminals))
                { proven.Add(pair.Key); ready.Enqueue(pair.Key); }
            while (ready.Count != 0)
            {
                MethodInfo method = ready.Dequeue();
                if (method == root) return true;
                if (!callers.TryGetValue(method, out var parents)) continue;
                foreach (var parent in parents)
                {
                    if (proven.Contains(parent.Method)) continue;
                    Command command = commands[parent.Method];
                    command.Terminals.Add(parent.Site);
                    if (!LifeEntryFlow.Terminates(command.Body, command.Terminals)) continue;
                    proven.Add(parent.Method); ready.Enqueue(parent.Method);
                }
            }
            return false;
        }

        private static int TerminalInput(MethodInfo call)
        {
            Type owner = call.DeclaringType;
            if (!call.IsStatic && (typeof(Entity).IsAssignableFrom(owner)
                    && (call.Name == nameof(Pawn.Kill) || call.Name == nameof(Pawn.Discard)
                        || call.Name == nameof(Thing.Destroy) || call.Name == nameof(Thing.ForceSetStateToUnspawned)
                        || call.Name == nameof(Thing.DeSpawn) || call.Name == nameof(Pawn.MakeCorpse))
                || owner == typeof(Pawn_HealthTracker) && call.Name == nameof(Pawn_HealthTracker.SetDead))) return 0;
            if (owner == typeof(MapPawns) && call.Name == nameof(MapPawns.DeRegisterPawn)
                || owner == typeof(TickManager) && call.Name == nameof(TickManager.DeRegisterAllTickabilityFor)
                || owner == typeof(ThingGrid) && call.Name == nameof(ThingGrid.Deregister)
                || owner == typeof(ListerThings) && call.Name == nameof(ListerThings.Remove)
                || owner == typeof(PawnComponentsUtility) && (call.Name == nameof(PawnComponentsUtility.RemoveComponentsOnKilled)
                    || call.Name == nameof(PawnComponentsUtility.RemoveComponentsOnDespawned)))
                return LifeEntryTarget.Argument(call);
            return -1;
        }
    }
}
