using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using LihuatLib.Core;
using LihuatLib.StarBlood;
using LihuatLib.Dispatching;

namespace LihuatLib.Mechanisms
{
    internal static class LifeExecutionGuard
    {
        private sealed class Ownership
        {
            public Pawn Pawn;
            public readonly HashSet<int> Sites = new HashSet<int>();
        }
        private static readonly ConditionalWeakTable<object, Ownership> owners = new ConditionalWeakTable<object, Ownership>();
        private static readonly List<LifeExecutionSite> sites = new List<LifeExecutionSite>();
        private static readonly Dictionary<MethodBase, List<LifeExecutionSite>> byMethod = new Dictionary<MethodBase, List<LifeExecutionSite>>();
        private static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
        private static readonly MethodInfo gate = AccessTools.Method(typeof(LifeExecutionGuard), nameof(Block));
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthPawn = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        public static int InstalledSites => sites.Count(s => s.Installed);
        public static int RejectedMethods { get; private set; }

        internal static void Invalidate(MethodInfo method)
        {
            bool changed = byMethod.TryGetValue(method, out List<LifeExecutionSite> previous);
            if (changed)
            {
                foreach (LifeExecutionSite site in previous) site.Installed = false;
                byMethod.Remove(method);
            }
            changed |= LifeDeferredGuard.Invalidate(method);
            if (changed) Refresh(new[] { method });
        }

        internal static void Register(IEnumerable<LifeExecutionSite> candidates, object instance = null, Pawn pawn = null)
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            HashSet<MethodBase> changed = null;
            foreach (LifeExecutionSite candidate in candidates)
            {
                if (candidate.RequiresBinding && (instance == null || pawn == null || !candidate.Owns(instance, pawn))) continue;
                byMethod.TryGetValue(candidate.Method, out List<LifeExecutionSite> group);
                LifeExecutionSite site = null;
                if (group != null)
                    for (int i = 0; i < group.Count; i++)
                        if (group[i].SameLocation(candidate)) { site = group[i]; break; }
                if (site == null)
                {
                    if (!Supported(candidate)) continue;
                    if (group == null) byMethod.Add(candidate.Method, group = new List<LifeExecutionSite>());
                    site = candidate; site.Id = sites.Count; sites.Add(site); group.Add(site);
                    (changed ??= new HashSet<MethodBase>()).Add(site.Method);
                }
                if (site.RequiresBinding)
                {
                    Ownership owner = owners.GetOrCreateValue(instance);
                    if (owner.Pawn != pawn) { owner.Pawn = pawn; owner.Sites.Clear(); }
                    owner.Sites.Add(site.Id);
                }
            }
            if (changed != null) Refresh(changed);
        }

        internal static void Unbind(IEnumerable<LifeExecutionSite> candidates, object instance)
        {
            if (!owners.TryGetValue(instance, out Ownership owner)) return;
            foreach (LifeExecutionSite candidate in candidates)
                if (byMethod.TryGetValue(candidate.Method, out List<LifeExecutionSite> group))
                    foreach (LifeExecutionSite site in group)
                        if (site.SameLocation(candidate)) owner.Sites.Remove(site.Id);
        }

        internal static void Refresh(IEnumerable<MethodBase> changed)
        {
            foreach (MethodBase method in changed)
            {
                try
                {
                    if (patched.Contains(method)) NativeDispatcher.Refresh(method);
                    else NativeDispatcher.Register(method, transpiler: new DispatchMethod(AccessTools.Method(typeof(LifeExecutionGuard), nameof(Transpile))) { priority = int.MaxValue, order = 300 });
                    patched.Add(method);
                }
                catch (Exception e)
                {
                    RejectedMethods++;
                    if (byMethod.TryGetValue(method, out List<LifeExecutionSite> direct)) foreach (LifeExecutionSite site in direct) site.Installed = false;
                    List<LifeDeferredSite> deferred = LifeDeferredGuard.For(method);
                    if (deferred != null) foreach (LifeDeferredSite site in deferred) site.Installed = false;
                    Log.Warning("[LihuatLib.StarBlood] Counter guard unavailable for " + method.DeclaringType.FullName + "." + method.Name + ": " + e.GetType().Name);
                }
            }
        }

        private static bool Supported(LifeExecutionSite site)
        {
            MethodInfo method = site.Method;
            if (method.DeclaringType.IsValueType || method.Module.Assembly.IsDynamic
                || method.DeclaringType.IsGenericType && site.RequiresBinding) return false;
            Type[] operands = OperandTypes(site.Operation);
            return operands != null && site.ReceiverArgument < operands.Length && operands.All(t => !t.IsByRef && !t.IsPointer && !t.ContainsGenericParameters)
                && site.ReceiverFields.All(f => !f.IsStatic && !f.FieldType.IsPointer);
        }

        private static Type[] OperandTypes(MemberInfo operation)
        {
            if (operation is FieldInfo field) return new[] { field.DeclaringType, field.FieldType };
            if (!(operation is MethodInfo method) || method.ReturnType != typeof(void) && method.ReturnType != typeof(bool)) return null;
            return (method.IsStatic ? Enumerable.Empty<Type>() : new[] { method.DeclaringType }).Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
        }

        private static bool Block(int id, object instance, object recipient)
        {
            LihuatGame game = LihuatGame.Current;
            if (game == null || !game.Subjects.HasSubjects || OperationScope.Transferring
                || OperationScope.Rebirthing) return false;
            LifeExecutionSite site = sites[id];
            foreach (FieldInfo field in site.ReceiverFields)
            {
                if (recipient == null || !field.DeclaringType.IsInstanceOfType(recipient)) return false;
                recipient = field.GetValue(recipient);
            }
            Pawn pawn = recipient as Pawn ?? (recipient is Pawn_HealthTracker health ? healthPawn(health) : null);
            if (OperationScope.Terminates(pawn) || OperationScope.MedicalFor(pawn)) return false;
            StarBloodRules engine = game.Subjects.Find(pawn);
            StarBloodSubject record = engine?.Subject;
            if (!StarBloodRules.Enabled(record)) return false;
            if (site.RequiresBinding && (instance == null || !owners.TryGetValue(instance, out Ownership owner) || owner.Pawn != pawn || !owner.Sites.Contains(id))) return false;
            engine.EnsureMarker(pawn);
            record.State.CounterExecutionsBlocked++;
            return true;
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            List<CodeInstruction> code = instructions.ToList();
            var counts = new Dictionary<MemberInfo, int>();
            if (!byMethod.TryGetValue(__originalMethod, out List<LifeExecutionSite> candidates)) candidates = new List<LifeExecutionSite>();
            List<LifeDeferredSite> deferred = LifeDeferredGuard.For(__originalMethod) ?? new List<LifeDeferredSite>();
            foreach (LifeExecutionSite site in candidates) site.Installed = false;
            foreach (LifeDeferredSite site in deferred) site.Installed = false;
            if (candidates.Count == 0 && deferred.Count == 0)
            { foreach (CodeInstruction line in code) yield return line; yield break; }
            List<CodeInstruction> original = candidates.Count > 0 ? candidates[0].OriginalCode : deferred[0].OriginalCode;
            if (!LifeInstructionMatch.Equivalent(original, code))
            {
                RejectedMethods++;
                Log.Warning("[LihuatLib.StarBlood] Counter callsite changed before installation: " + __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name);
                foreach (CodeInstruction line in code) yield return line;
                yield break;
            }
            foreach (CodeInstruction line in code)
            {
                if (!(line.operand is MemberInfo member)) { yield return line; continue; }
                counts.TryGetValue(member, out int occurrence); counts[member] = occurrence + 1;
                LifeExecutionSite site = candidates.FirstOrDefault(s => s.Operation == member && s.Occurrence == occurrence);
                LifeDeferredSite[] schedules = deferred.Where(s => s.Operation == member && s.Occurrence == occurrence).ToArray();
                if (site == null && schedules.Length == 0 || (line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt && line.opcode != OpCodes.Stfld && line.opcode != OpCodes.Stsfld))
                { yield return line; continue; }
                Type[] types = site != null ? OperandTypes(member) : LifeDeferredSite.OperandTypes(member);
                LocalBuilder[] locals = types.Select(generator.DeclareLocal).ToArray();
                var injected = new List<CodeInstruction>();
                for (int i = locals.Length - 1; i >= 0; i--) injected.Add(new CodeInstruction(OpCodes.Stloc, locals[i]));
                injected[0].labels.AddRange(line.labels); line.labels.Clear();
                foreach (ExceptionBlock block in line.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock).ToArray())
                { injected[0].blocks.Add(block); line.blocks.Remove(block); }
                foreach (LifeDeferredSite schedule in schedules)
                {
                    injected.Add(new CodeInstruction(OpCodes.Ldc_I4, schedule.Id));
                    injected.Add(new CodeInstruction(OpCodes.Ldloc, locals[schedule.DelegateArgument]));
                    injected.AddRange(schedule.Owner.Load());
                    injected.Add(new CodeInstruction(OpCodes.Call, LifeDeferredGuard.BindMethod));
                    schedule.Installed = true;
                }
                Label done = generator.DefineLabel();
                Label blocked = generator.DefineLabel();
                bool returnsFlag = member is MethodInfo called && called.ReturnType == typeof(bool);
                if (site != null)
                {
                    injected.Add(new CodeInstruction(OpCodes.Ldc_I4, site.Id));
                    injected.Add(new CodeInstruction(__originalMethod.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0));
                    injected.Add(new CodeInstruction(OpCodes.Ldloc, locals[site.ReceiverArgument]));
                    Type recipientType = types[site.ReceiverArgument];
                    if (recipientType.IsValueType) injected.Add(new CodeInstruction(OpCodes.Box, recipientType));
                    injected.Add(new CodeInstruction(OpCodes.Call, gate));
                    injected.Add(new CodeInstruction(OpCodes.Brtrue, returnsFlag ? blocked : done));
                }
                for (int i = 0; i < locals.Length; i++) injected.Add(new CodeInstruction(OpCodes.Ldloc, locals[i]));
                var end = new CodeInstruction(OpCodes.Nop); end.labels.Add(done);
                end.blocks.AddRange(line.blocks); line.blocks.Clear();
                foreach (CodeInstruction added in injected) yield return added;
                yield return line;
                if (site != null && returnsFlag)
                {
                    yield return new CodeInstruction(OpCodes.Br, done);
                    var denied = new CodeInstruction(OpCodes.Ldc_I4_0);
                    denied.labels.Add(blocked);
                    yield return denied;
                }
                yield return end;
                if (site != null) site.Installed = true;
            }
        }
    }
}
