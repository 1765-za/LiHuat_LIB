using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    [Flags]
    internal enum RegenerationEvidence { None = 0, Generation = 1, Spawn = 2, Removal = 4, Resurrection = 8, SnapshotKind = 16, Unknown = 32, WorldRegistration = 64 }
    internal sealed class RegenerationAnalysis
    {
        private readonly LifeIlReader reader;
        private readonly LifeIndirectCalls indirect;
        internal int Unreadable { get; private set; }
        internal HashSet<MethodInfo> Visited { get; } = new HashSet<MethodInfo>();
        private readonly Stack<MethodInfo> pending = new();
        private RegenerationEvidence result;
        private bool started;
        internal RegenerationAnalysis(LifeIlReader reader) { this.reader = reader; indirect = new LifeIndirectCalls(reader); }
        internal RegenerationEvidence Inspect(MethodInfo root, Func<MethodInfo, bool> available = null)
        {
            var visited = Visited;
            if (!started) { pending.Push(root); started = true; }
            var deferred = new List<MethodInfo>();
            while (pending.Count > 0)
            {
                MethodInfo method = pending.Pop();
                if (visited.Contains(method)) continue;
                if (available != null && !available(method)) { deferred.Add(method); continue; }
                visited.Add(method);
                var code = reader.Read(method);
                if (code == null) { Unreadable++; result |= RegenerationEvidence.Unknown; continue; }
                Dictionary<HarmonyLib.CodeInstruction, MethodInfo> resolved = indirect.Find(code);
                foreach (var instruction in code)
                {
                    if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field
                        && field.FieldType == typeof(PawnKindDef) && (LifeResourceEvidence.IsCustom(field.DeclaringType) || field.DeclaringType == typeof(Pawn)))
                        result |= RegenerationEvidence.SnapshotKind;
                    if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt
                        || !(instruction.operand is MethodInfo call)) continue;
                    if (resolved.TryGetValue(instruction, out MethodInfo target)) call = target;
                    Type type = call.DeclaringType;
                    if (type == typeof(PawnGenerator) && call.Name == nameof(PawnGenerator.GeneratePawn)) result |= RegenerationEvidence.Generation;
                    else if (type == typeof(GenSpawn) && call.Name == nameof(GenSpawn.Spawn)
                        || typeof(Thing).IsAssignableFrom(type) && call.Name == nameof(Thing.SpawnSetup)) result |= RegenerationEvidence.Spawn;
                    else if (type == typeof(WorldPawns) && call.Name == nameof(WorldPawns.PassToWorld)) result |= RegenerationEvidence.WorldRegistration;
                    else if (typeof(Entity).IsAssignableFrom(type) && call.Name == nameof(Thing.DeSpawn)) result |= RegenerationEvidence.Removal;
                    else if (type == typeof(ResurrectionUtility) && call.Name == nameof(ResurrectionUtility.TryResurrect)
                        || type == typeof(Pawn_HealthTracker) && call.Name == nameof(Pawn_HealthTracker.Notify_Resurrected)) result |= RegenerationEvidence.Resurrection;
                    else if (Follows(root, call))
                        pending.Push(call);
                }
            }
            foreach (MethodInfo method in deferred) pending.Push(method);
            return result;
        }
        private static bool Follows(MethodInfo root, MethodInfo call)
        {
            if (!LifeResourceEvidence.IsCustom(call.DeclaringType) || !LifeIlReader.HasMetadataBody(call)) return false;
            if (call.Module.Assembly == root.Module.Assembly) return true;
            if (InfrastructureAssemblies.Contains(call.Module.Assembly)) return false;
            if (typeof(Pawn).IsAssignableFrom(call.ReturnType)) return true;
            foreach (ParameterInfo parameter in call.GetParameters())
            {
                Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                if (typeof(Pawn).IsAssignableFrom(type) || type == typeof(PawnKindDef) || type == typeof(Map)
                    || type.IsClass && type.Assembly == root.Module.Assembly) return true;
            }
            return false;
        }
        internal static bool Proven(RegenerationEvidence evidence, bool snapshot, bool recordedIdentity = false)
        {
            if ((evidence & RegenerationEvidence.Unknown) != 0) return false;
            if (recordedIdentity)
                return (evidence & RegenerationEvidence.Generation) != 0
                    && (evidence & (RegenerationEvidence.Spawn | RegenerationEvidence.WorldRegistration)) != 0;
            if ((evidence & RegenerationEvidence.Resurrection) != 0) return true;
            const RegenerationEvidence restoration = RegenerationEvidence.Spawn | RegenerationEvidence.Removal;
            if ((evidence & RegenerationEvidence.Generation) == 0)
                return (evidence & restoration) == restoration;
            const RegenerationEvidence replacement = RegenerationEvidence.Generation | RegenerationEvidence.Spawn
                | RegenerationEvidence.Removal | RegenerationEvidence.SnapshotKind;
            return (evidence & replacement) == replacement;
        }
    }
}
