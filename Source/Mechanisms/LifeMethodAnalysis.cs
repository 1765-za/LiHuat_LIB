using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using State = LihuatLib.Mechanisms.LifeFlowState;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeMethodAnalysis
    {
        private sealed class CallSummary
        {
            public LifeFlowValue Returned;
            public LifeDeathEffect[] Deaths;
            public LifeDependency[] Dependencies;
            public HashSet<MethodInfo> Context;
            public int Incomplete;
        }
        private readonly Type rootType;
        private readonly LifeIlReader reader;
        private readonly LifeObjectPath recordOwner;
        private readonly LifeObjectPath componentOwner;
        private readonly LifeCallRelevance relevance;
        private readonly Dictionary<MethodInfo, LifeControlFlow> methods = new Dictionary<MethodInfo, LifeControlFlow>();
        private readonly HashSet<MethodInfo> executionRoots = new HashSet<MethodInfo>();
        private readonly Dictionary<MethodInfo, Dictionary<LifeFlowMemo, CallSummary>> summaries = new Dictionary<MethodInfo, Dictionary<LifeFlowMemo, CallSummary>>();
        private readonly Dictionary<MethodInfo, Dictionary<LifeFlowMemo, List<CallSummary>>> recursiveSummaries = new Dictionary<MethodInfo, Dictionary<LifeFlowMemo, List<CallSummary>>>();
        private int incompletePaths;
        private bool boundCallback;
        public int Unreadable;
        public int Limited;
        internal MethodInfo CurrentMethod;
        internal int CurrentInstruction = -1;
        internal long VisitedStates;
        internal int GraphMethods => relevance.VisitedMethods;
        public readonly List<LifeDependency> Dependencies = new List<LifeDependency>();
        public readonly List<LifeExecutionSite> ExecutionSites = new List<LifeExecutionSite>();
        public readonly List<LifeDeferredSite> DeferredSites = new List<LifeDeferredSite>();
        public IEnumerable<MethodInfo> ReadMethods => executionRoots;
        public LifeMethodAnalysis(Type rootType, LifeIlReader reader)
        {
            this.rootType = rootType; this.reader = reader;
            relevance = new LifeCallRelevance(rootType, reader);
            recordOwner = LifeOwnerPath.ForRecord(rootType); componentOwner = LifeOwnerPath.ForComponent(rootType);
        }

        public void Inspect(MethodInfo method, bool boundCallback = false)
        {
            this.boundCallback = boundCallback;
            CurrentMethod = method; CurrentInstruction = -1;
            if (!relevance.HasEffects(method)) return;
            var args = new LifeFlowValue[method.GetParameters().Length + (method.IsStatic ? 0 : 1)];
            for (int i = 0; i < args.Length; i++) args[i] = new LifeFlowValue(paths: new[] { LifeObjectPath.Symbol(Tuple.Create(method, i)) });
            if (!method.IsStatic) args[0] = new LifeFlowValue(paths: new[] { new LifeObjectPath() });
            Walk(method, args, LifeFlowValue.Empty, new HashSet<MethodInfo>(), new List<LifeDeathEffect>());
            Limited = incompletePaths;
        }

        private LifeControlFlow Read(MethodInfo method)
        {
            if (methods.TryGetValue(method, out LifeControlFlow result)) return result;
            methods[method] = null;
            try
            {
                List<CodeInstruction> instructions = reader.Read(method);
                if (instructions == null) { Unreadable++; return null; }
                if (instructions.Any(IsExecutionEntry)) executionRoots.Add(method);
                result = new LifeControlFlow(instructions);
                methods[method] = result;
                return result;
            }
            catch (Exception) { Unreadable++; return null; }
        }

        private static bool IsExecutionEntry(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo field)
                return field.DeclaringType == typeof(Pawn_HealthTracker) && field.Name == "healthState";
            if (!(instruction.operand is MethodInfo method)) return false;
            if (instruction.opcode == OpCodes.Ldftn || instruction.opcode == OpCodes.Ldvirtftn) return true;
            return IsDeath(method) || typeof(Delegate).IsAssignableFrom(method.DeclaringType) && method.Name == "Invoke"
                || typeof(MethodBase).IsAssignableFrom(method.DeclaringType) && method.Name == "Invoke"
                || typeof(FieldInfo).IsAssignableFrom(method.DeclaringType) && method.Name == "SetValue";
        }

        private LifeFlowValue Walk(MethodInfo method, LifeFlowValue[] args, LifeFlowValue inherited, HashSet<MethodInfo> callers,
            List<LifeDeathEffect> deaths, Dictionary<LifeObjectPath, LifeFlowValue> captures = null)
        {
            if (callers.Contains(method)) { incompletePaths++; return LifeFlowValue.Empty; }
            Dictionary<LifeFlowMemo, CallSummary> group = null;
            LifeFlowMemo callKey = null;
            if (callers.Count > 0)
            {
                if (!summaries.TryGetValue(method, out group)) summaries[method] = group = new Dictionary<LifeFlowMemo, CallSummary>();
                callKey = new LifeFlowMemo(0, args, new Dictionary<int, LifeFlowValue>(), new HashSet<int>(),
                    new[] { new KeyValuePair<long, LifeFlowValue>(0, inherited) }, captures);
                if (group != null && group.TryGetValue(callKey, out CallSummary cached))
                {
                    deaths.AddRange(cached.Deaths); Dependencies.AddRange(cached.Dependencies);
                    return cached.Returned;
                }
                if (recursiveSummaries.TryGetValue(method, out var recursive) && recursive.TryGetValue(callKey, out var candidates))
                    foreach (CallSummary candidate in candidates)
                        if (candidate.Context.SetEquals(callers))
                        {
                            incompletePaths += candidate.Incomplete;
                            deaths.AddRange(candidate.Deaths); Dependencies.AddRange(candidate.Dependencies);
                            return candidate.Returned;
                        }
            }
            int dependencyStart = Dependencies.Count, incompleteStart = incompletePaths;
            LifeControlFlow flow = Read(method);
            if (flow == null) { incompletePaths++; return LifeFlowValue.Empty; }
            HashSet<MethodInfo> parentCallers = callers;
            callers = new HashSet<MethodInfo>(callers) { method };
            var pending = new Stack<State>(); pending.Push(new State { Captures = LifeClosureFlow.Copy(captures) });
            var reached = new State[flow.CheckpointCount];
            var visited = new LifeFlowMemo[flow.CheckpointCount];
            LifeFlowValue returned = LifeFlowValue.Empty;
            while (pending.Count > 0)
            {
                State state = pending.Pop();
                while (state.Position < flow.Code.Count)
                {
                    int pc = state.Position;
                    CurrentMethod = method; CurrentInstruction = pc; VisitedStates++;
                    state.EndGuards(pc);
                    int checkpoint = flow.Checkpoint[pc];
                    if (checkpoint >= 0)
                    {
                        if (reached[checkpoint] != null) state = State.Merge(reached[checkpoint], state, flow.LiveLocals[pc]);
                        var memo = new LifeFlowMemo(pc, state.Stack, state.Locals, flow.LiveLocals[pc],
                            state.Guards.Select(g => new KeyValuePair<long, LifeFlowValue>(((long)g.Start << 32) | (uint)g.End, g.Condition)), state.Captures);
                        if (memo.Equals(visited[checkpoint])) break;
                        visited[checkpoint] = memo;
                        reached[checkpoint] = state.Fork(pc);
                    }
                    LifeFlowValue condition = state.Condition(inherited);
                    CodeInstruction line = flow.Code[pc];
                    string op = line.opcode.Name;
                    if (op.StartsWith("ldarg")) { int i = Index(line, method); state.Stack.Add(i < args.Length ? args[i] : LifeFlowValue.Empty); }
                    else if (op.StartsWith("ldloca"))
                    {
                        int i = Index(line, method);
                        LifeFlowValue v = state.Locals.TryGetValue(i, out LifeFlowValue local) ? local : LifeFlowValue.Empty;
                        state.Stack.Add(new LifeFlowValue(v.Fields, v.Paths, v.Constant, v.CounterOwners, v.IndexedOwners, i, v.StateFields));
                    }
                    else if (op.StartsWith("ldloc")) { int i = Index(line, method); state.Stack.Add(state.Locals.TryGetValue(i, out LifeFlowValue v) ? v : LifeFlowValue.Empty); }
                    else if (op.StartsWith("stloc")) state.Locals[Index(line, method)] = state.Pop();
                    else if (op == "ldfld" || op == "ldflda")
                    {
                        FieldInfo field = (FieldInfo)line.operand;
                        LifeFlowValue receiver = state.Pop();
                        bool ownField = field.DeclaringType.IsAssignableFrom(rootType) && receiver.Paths.Any(p => p.Root == null && p.Fields.Length == 0);
                        bool identity = field.DeclaringType == typeof(Thing) && field.Name == "thingIDNumber";
                        state.Stack.Add(typeof(Corpse).IsAssignableFrom(field.FieldType) ? LifePostMortemFlow.CorpseValue
                            : LifeClosureFlow.Read(state.Captures, receiver, field, out LifeFlowValue capture) ? capture
                            : LifeResourceEvidence.ScalarState(field.FieldType) && !identity
                            ? new LifeFlowValue(ownField && LifeResourceEvidence.Numeric(field.FieldType) ? new[] { field } : null,
                                counterOwners: receiver.IndexedOwners, stateFields: ownField ? new[] { field } : null)
                            : new LifeFlowValue(paths: identity ? receiver.Paths : receiver.Paths.Select(p => p.Append(field)), indexedOwners: receiver.IndexedOwners));
                    }
                    else if (op == "stfld")
                    {
                        if (callers.Count == 1 && !boundCallback) AddDeferredSite(method, flow, pc, condition, args, state.Locals);
                        LifeFlowValue stored = state.Pop(), receiver = state.Pop();
                        LifeClosureFlow.Store(ref state.Captures, receiver, line.operand as FieldInfo, stored);
                        if (IsDeadState(line.operand as FieldInfo, stored.Constant))
                        {
                            deaths.Add(new LifeDeathEffect(receiver.Paths));
                            if (condition.Fields.Count > 0) Dependencies.Add(new LifeDependency(condition.Fields, receiver.Paths));
                            if (callers.Count == 1) AddSite(method, flow, pc, condition, receiver.Paths, new[] { receiver, stored });
                        }
                    }
                    else if (op == "ldstr" || op == "ldtoken") state.Stack.Add(new LifeFlowValue(constant: line.operand));
                    else if (op == "ldftn" || op == "ldvirtftn")
                    {
                        if (op == "ldvirtftn") state.Pop();
                        if (line.operand is MethodInfo callback && LifeResourceEvidence.IsCustom(callback.DeclaringType)) Read(callback);
                        state.Stack.Add(new LifeFlowValue(constant: line.operand));
                    }
                    else if (op.StartsWith("ldc.i4"))
                    {
                        object number = op == "ldc.i4.m1" ? -1 : line.operand ?? (object)(op[op.Length - 1] - '0');
                        state.Stack.Add(new LifeFlowValue(constant: Convert.ToInt32(number)));
                    }
                    else if (op == "ldsfld" || op == "ldsflda") state.Stack.Add(new LifeFlowValue(paths: new[] { LifeObjectPath.Symbol(line.operand) }));
                    else if (op == "ldnull" || op.StartsWith("ldc.")) state.Stack.Add(LifeFlowValue.Empty);
                    else if (op == "dup") { LifeFlowValue value = state.Pop(); state.Stack.Add(value); state.Stack.Add(value); }
                    else if (op == "stsfld")
                    {
                        if (callers.Count == 1 && !boundCallback) AddDeferredSite(method, flow, pc, condition, args, state.Locals);
                        state.Pop();
                    }
                    else if (op == "pop" || op == "initobj") state.Pop();
                    else if (op == "call" || op == "callvirt" || op == "newobj")
                    {
                        MethodBase called = (MethodBase)line.operand;
                        var values = new LifeFlowValue[called.GetParameters().Length + (!called.IsStatic && op != "newobj" ? 1 : 0)];
                        for (int i = values.Length - 1; i >= 0; i--) values[i] = state.Pop();
                        if (callers.Count == 1 && !boundCallback && op != "newobj")
                            AddDeferredSite(method, flow, pc, condition, args, state.Locals);
                        LifeFlowValue value = LifeFlowValue.Empty;
                        if (called is MethodInfo callee)
                        {
                            if (IsDeath(callee) && values.Length > 0 && !LifePostMortemFlow.IsCorpse(values[0]))
                            {
                                deaths.Add(new LifeDeathEffect(values[0].Paths));
                                if (condition.Fields.Count > 0) Dependencies.Add(new LifeDependency(condition.Fields, values[0].Paths));
                                if (callers.Count == 1) AddSite(method, flow, pc, condition, values[0].Paths, values);
                            }
                            else if (callee.DeclaringType == typeof(Type) && callee.Name == "GetTypeFromHandle" && values.Length == 1)
                                value = values[0];
                            else if ((callee.DeclaringType == typeof(Type) && callee.Name == "GetField"
                                || callee.DeclaringType == typeof(AccessTools) && callee.Name == "Field")
                                && values.Length >= 2 && values[0].Constant is Type reflectedType && values[1].Constant is string fieldName)
                                value = new LifeFlowValue(constant: reflectedType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
                            else if (typeof(FieldInfo).IsAssignableFrom(callee.DeclaringType) && callee.Name == "SetValue"
                                && values.Length >= 3 && IsDeadState(values[0].Constant as FieldInfo, values[2].Constant))
                            {
                                deaths.Add(new LifeDeathEffect(values[1].Paths));
                                if (condition.Fields.Count > 0) Dependencies.Add(new LifeDependency(condition.Fields, values[1].Paths));
                                if (callers.Count == 1) AddSite(method, flow, pc, condition, values[1].Paths, values);
                            }
                            else if (callee.DeclaringType == typeof(Hediff) && callee.Name == "get_Severity"
                                && values.Length > 0 && values[0].Paths.Any(p => p.Root == null && p.Fields.Length == 0))
                                value = new LifeFlowValue(new[] { AccessTools.Field(typeof(Hediff), "severityInt") });
                            else if (callee.DeclaringType == typeof(HediffComp) && callee.Name == "get_Pawn" && values.Length > 0)
                                value = new LifeFlowValue(paths: values[0].Paths.Select(p => p.Append(AccessTools.Field(typeof(HediffComp), "parent")).Append(AccessTools.Field(typeof(Hediff), "pawn"))));
                            else if (callee.DeclaringType == typeof(Thing) && (callee.Name == "get_ThingID" || callee.Name == "GetUniqueLoadID") && values.Length > 0)
                                value = new LifeFlowValue(paths: values[0].Paths.Select(p => p.PawnIdentity()));
                            else if (DictionaryRead(callee, values, state, out LifeFlowValue indexed)) value = indexed;
                            else if (typeof(Delegate).IsAssignableFrom(callee.DeclaringType) && callee.Name == "Invoke"
                                && values.Length > 0 && values[0].Constant is LifeDelegateCall work)
                            {
                                LifeFlowValue[] input = work.Method.IsStatic ? values.Skip(1).ToArray()
                                    : new[] { work.Target }.Concat(values.Skip(1)).ToArray();
                                var effects = new List<LifeDeathEffect>();
                                value = Walk(work.Method, input, condition, callers, effects, state.Captures);
                                deaths.AddRange(effects);
                            }
                            else if (LifeResourceEvidence.IsCustom(callee.DeclaringType) && !callee.ContainsGenericParameters && relevance.Needed(callee))
                            {
                                var effects = new List<LifeDeathEffect>();
                                int incomplete = incompletePaths;
                                value = Walk(callee, values, condition, callers, effects, state.Captures);
                                deaths.AddRange(effects);
                                if (callers.Count == 1 && incompletePaths == incomplete && effects.Count > 0 && effects.All(e => e.Receivers.Length == 1))
                                {
                                    LifeObjectPath[] recipients = effects.SelectMany(e => e.Receivers).Distinct().ToArray();
                                    if (recipients.Select(p => p.PawnIdentity()).Distinct().Count() == 1)
                                        AddSite(method, flow, pc, condition, recipients.OrderBy(p => p.Fields.Length).Take(1).ToArray(), values);
                                }
                            }
                            else if (LifePlatformAssemblies.Contains(callee.Module.Assembly))
                                value = LifeFlowValue.Dependencies(values);
                            if (callee.ReturnType != typeof(void)) state.Stack.Add(LifePostMortemFlow.ReturnsCorpse(callee)
                                ? LifePostMortemFlow.CorpseValue : value);
                        }
                        else if (op == "newobj") state.Stack.Add(LifeClosureFlow.Construct((ConstructorInfo)called, values, method, pc));
                    }
                    else if ((op == "isinst" || op == "castclass") && line.operand is Type castType && typeof(Corpse).IsAssignableFrom(castType))
                    {
                        state.Pop(); state.Stack.Add(LifePostMortemFlow.CorpseValue);
                    }
                    else if (line.opcode.FlowControl == FlowControl.Cond_Branch)
                    {
                        LifeFlowValue predicate = state.Pop();
                        if (line.opcode.StackBehaviourPop != StackBehaviour.Pop1 && line.opcode.StackBehaviourPop != StackBehaviour.Popi)
                            predicate = LifeFlowValue.Union(predicate, state.Pop());
                        if ((predicate.CounterOwners.Length > 0 || predicate.Fields.Count > 0 || predicate.StateFields.Count > 0)
                            && relevance.HasEffects(method)) executionRoots.Add(method);
                        state.Branch(pc, flow.Join[pc], predicate);
                        foreach (int next in flow.Next[pc]) pending.Push(state.Fork(next));
                        break;
                    }
                    else if (line.opcode.FlowControl == FlowControl.Return)
                    {
                        if (method.ReturnType != typeof(void)) returned = LifeFlowValue.Return(returned, state.Pop(), condition);
                        break;
                    }
                    else if (line.opcode.FlowControl == FlowControl.Throw) break;
                    else if (line.opcode.StackBehaviourPush != StackBehaviour.Push0 && (op == "ceq" || op.StartsWith("cgt") || op.StartsWith("clt")
                        || line.opcode.StackBehaviourPop == StackBehaviour.Pop1_pop1 || line.opcode.StackBehaviourPop == StackBehaviour.Popi_popi))
                    {
                        LifeFlowValue operands = LifeFlowValue.Union(state.Pop(), state.Pop());
                        state.Stack.Add(new LifeFlowValue(operands.Fields, counterOwners: operands.CounterOwners, stateFields: operands.StateFields));
                    }
                    else if (op == "leave" || op == "leave.s") state.Stack.Clear();
                    else if (!LifeStackEffects.Apply(state, line.opcode)) { Unreadable++; incompletePaths++; break; }
                    state.Position = flow.Next[pc][0];
                }
            }
            if (group != null)
            {
                var summary = new CallSummary { Returned = returned, Deaths = deaths.ToArray(), Dependencies = Dependencies.Skip(dependencyStart).ToArray(),
                    Incomplete = incompletePaths - incompleteStart };
                if (summary.Incomplete == 0) group[callKey] = summary;
                else
                {
                    // Recursion depends on the active call set; reuse only within the same context.
                    summary.Context = parentCallers;
                    if (!recursiveSummaries.TryGetValue(method, out var recursive))
                        recursiveSummaries[method] = recursive = new Dictionary<LifeFlowMemo, List<CallSummary>>();
                    if (!recursive.TryGetValue(callKey, out var candidates)) recursive[callKey] = candidates = new List<CallSummary>();
                    candidates.Add(summary);
                }
            }
            return returned;
        }

        private void AddSite(MethodInfo method, LifeControlFlow flow, int pc, LifeFlowValue condition, LifeObjectPath[] receivers, LifeFlowValue[] args)
        {
            if (receivers.Length == 0 || receivers.Select(p => p.PawnIdentity()).Distinct().Count() != 1) return;
            bool indexed = condition.CounterOwners.Any(key => key.PawnIdentity().Equals(receivers[0].PawnIdentity()));
            bool stateOwned = !boundCallback && condition.StateFields.Count > 0 && !method.IsStatic && LifeOwnerPath.Matches(recordOwner, receivers);
            if (!indexed && !stateOwned && ((!boundCallback && condition.Fields.Count == 0) || method.IsStatic || receivers.Any(p => p.Root != null))) return;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Paths.Length != 1 || !receivers.All(p => p.StartsWith(args[i].Paths[0]))) continue;
                var site = new LifeExecutionSite
                {
                    Method = method, Operation = (MemberInfo)flow.Code[pc].operand,
                    Occurrence = flow.Code.Take(pc).Count(c => Equals(c.operand, flow.Code[pc].operand)),
                    ReceiverArgument = i, ReceiverFields = receivers[0].Fields.Skip(args[i].Paths[0].Fields.Length).ToArray(),
                    Receivers = receivers, Indexed = indexed, StateOwned = stateOwned, OriginalCode = flow.Code
                };
                if (!ExecutionSites.Any(s => s.SameLocation(site))) ExecutionSites.Add(site);
                break;
            }
        }

        private void AddDeferredSite(MethodInfo method, LifeControlFlow flow, int pc, LifeFlowValue condition,
            LifeFlowValue[] args, Dictionary<int, LifeFlowValue> locals)
        {
            if (!(flow.Code[pc].operand is MemberInfo called)) return;
            Type[] operands = LifeDeferredSite.OperandTypes(called);
            if (operands == null || !operands.Any(t => typeof(Delegate).IsAssignableFrom(t))) return;
            LifeObjectPath[] owners = condition.CounterOwners;
            if (owners.Length == 0 && !method.IsStatic)
            {
                LifeObjectPath own = condition.StateFields.Count > 0 ? recordOwner : null;
                if (own == null && condition.Fields.Count > 0) own = componentOwner;
                if (own != null) owners = new[] { own };
            }
            LifeValueSource owner = LifeValueSource.ResolveOwner(method, owners, args, locals, reader.Locals(method));
            if (owner == null) return;
            for (int i = 0; i < operands.Length; i++)
            {
                if (!typeof(Delegate).IsAssignableFrom(operands[i])) continue;
                var site = new LifeDeferredSite { Method = method, Operation = called,
                    Occurrence = flow.Code.Take(pc).Count(c => Equals(c.operand, called)), DelegateArgument = i,
                    Owner = owner, OriginalCode = flow.Code };
                if (!DeferredSites.Any(s => s.SameLocation(site))) DeferredSites.Add(site);
            }
        }

        private static bool DictionaryRead(MethodInfo method, LifeFlowValue[] args, State state, out LifeFlowValue value)
        {
            value = LifeFlowValue.Empty;
            Type type = method.DeclaringType;
            if (!type.IsGenericType) return false;
            Type generic = type.GetGenericTypeDefinition();
            if (generic == typeof(Dictionary<,>) || generic == typeof(IDictionary<,>))
            {
                if (method.Name == "GetEnumerator") { value = new LifeFlowValue(paths: args[0].Paths); return true; }
                if ((method.Name != "get_Item" && method.Name != "TryGetValue") || args.Length < 2) return false;
                LifeObjectPath[] owners = args[1].Paths.Select(p => p.PawnIdentity()).ToArray();
                Type item = type.GetGenericArguments()[1];
                var read = LifeResourceEvidence.ScalarState(item) ? new LifeFlowValue(counterOwners: owners)
                    : new LifeFlowValue(indexedOwners: owners);
                if (method.Name == "get_Item") value = read;
                else if (args.Length == 3 && args[2].Address >= 0) state.Locals[args[2].Address] = read;
                return true;
            }
            if (generic == typeof(KeyValuePair<,>) && args.Length == 1)
            {
                FieldInfo key = AccessTools.Field(type, "key");
                if (key == null) return false;
                LifeObjectPath[] keys = args[0].Paths.Select(p => p.Append(key)).ToArray();
                if (method.Name == "get_Key") value = new LifeFlowValue(paths: keys);
                else if (method.Name == "get_Value") value = LifeResourceEvidence.ScalarState(type.GetGenericArguments()[1])
                    ? new LifeFlowValue(counterOwners: keys) : new LifeFlowValue(indexedOwners: keys);
                else return false;
                return true;
            }
            if (method.Name == "get_Current" && method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                if (args.Length == 0 || args[0].Paths.Length != 1) return false;
                value = new LifeFlowValue(paths: new[] { LifeObjectPath.Symbol(Tuple.Create(method, args[0].Paths[0])) });
                return true;
            }
            return false;
        }

        private static int Index(CodeInstruction line, MethodInfo method)
        {
            string op = line.opcode.Name;
            char last = op[op.Length - 1];
            if (last >= '0' && last <= '3') return last - '0';
            if (line.operand is LocalBuilder local) return local.LocalIndex;
            if (line.operand is LocalVariableInfo variable) return variable.LocalIndex;
            if (line.operand is ParameterInfo parameter) return parameter.Position + (method.IsStatic ? 0 : 1);
            return Convert.ToInt32(line.operand);
        }
        private static bool IsDeath(MethodInfo method) =>
            typeof(Entity).IsAssignableFrom(method.DeclaringType) && (method.Name == "Kill" || method.Name == "Destroy" || method.Name == "Discard" || method.Name == "DeSpawn")
            || method.DeclaringType == typeof(Pawn_HealthTracker) && method.Name == "SetDead";
        private static bool IsDeadState(FieldInfo field, object value) => field?.DeclaringType == typeof(Pawn_HealthTracker)
            && field.Name == "healthState" && value is int state && state == (int)PawnHealthState.Dead;
    }
}
