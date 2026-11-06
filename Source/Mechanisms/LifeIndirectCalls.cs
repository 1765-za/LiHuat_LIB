using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeIndirectCalls
    {
        private readonly LifeIlReader reader;
        private readonly LifeFieldSetterEvidence setters;
        private readonly Dictionary<FieldInfo, object> fields = new();
        private readonly HashSet<Type> initialized = new();
        private readonly Dictionary<MethodInfo, object> returns = new();
        private readonly HashSet<MethodInfo> forwarding = new();
        private int expansions;
        internal readonly HashSet<CodeInstruction> RemovalSites = new();
        internal readonly HashSet<CodeInstruction> StateCommits = new();
        internal readonly HashSet<MethodInfo> InvokedCallbacks = new();
        internal int Unresolved { get; private set; }

        internal LifeIndirectCalls(LifeIlReader reader) { this.reader = reader; setters = new LifeFieldSetterEvidence(reader); }

        internal Dictionary<CodeInstruction, MethodInfo> Find(List<CodeInstruction> code)
        {
            var result = new Dictionary<CodeInstruction, MethodInfo>();
            InvokedCallbacks.Clear();
            if (code.Any(line => line.opcode == OpCodes.Ldftn || line.opcode == OpCodes.Ldvirtftn
                || line.operand is MethodInfo call && (Invokes(call) || WritesField(call) || setters.Matches(call)))) Walk(code, result);
            return result;
        }

        internal static bool Invokes(MethodInfo method) => !method.ReturnType.IsByRef && (method.Name == "Invoke"
            && (typeof(Delegate).IsAssignableFrom(method.DeclaringType) || typeof(MethodBase).IsAssignableFrom(method.DeclaringType))
            || method.Name == nameof(Delegate.DynamicInvoke) && typeof(Delegate).IsAssignableFrom(method.DeclaringType));
        private static bool WritesField(MethodInfo method) => method.Name == nameof(FieldInfo.SetValue)
            && typeof(FieldInfo).IsAssignableFrom(method.DeclaringType);

        private object Read(FieldInfo field)
        {
            if (!field.IsStatic || !field.IsInitOnly || !ConstantType(field.FieldType)) return null;
            if (fields.TryGetValue(field, out object value)) return value;
            Type type = field.DeclaringType;
            if (!initialized.Add(type) || ++expansions > 128) return null;
            ConstructorInfo constructor = type.TypeInitializer;
            if (constructor == null) return null;
            try
            {
                List<CodeInstruction> code = LifeIlReader.Decode(constructor, constructor.GetMethodBody()?.GetILAsByteArray());
                if (code != null) Walk(code, null);
            }
            catch (Exception error) when (LifeDiscoveryMetadata.ResolutionFailure(error) || error is ArgumentException || error is BadImageFormatException)
            { Unresolved++; }
            return fields.TryGetValue(field, out value) ? value : null;
        }

        private object Return(MethodInfo method)
        {
            if (returns.TryGetValue(method, out object value)) return value;
            if (!method.IsStatic || !ConstantType(method.ReturnType) || method.GetParameters().Length != 0 || !LifeResourceEvidence.IsCustom(method.DeclaringType)
                || ++expansions > 128) return null;
            returns[method] = null;
            List<CodeInstruction> code = reader.Read(method);
            if (code != null) returns[method] = Walk(code, null);
            return returns[method];
        }

        private static bool ConstantType(Type type) => typeof(MethodBase).IsAssignableFrom(type) || typeof(FieldInfo).IsAssignableFrom(type)
            || typeof(Delegate).IsAssignableFrom(type)
            || type == typeof(Type) || type == typeof(Type[]) || type == typeof(string) || type == typeof(BindingFlags);

        private object Walk(List<CodeInstruction> code, Dictionary<CodeInstruction, MethodInfo> invoked, object[] arguments = null)
        {
            var stack = new List<object>();
            var locals = new Dictionary<int, object>();
            object returned = null;
            bool hasReturn = false;
            foreach (CodeInstruction line in code)
            {
                OpCode op = line.opcode;
                if (line.labels.Count != 0) stack.Clear();
                if (op.Name.StartsWith("ldarg", StringComparison.Ordinal) && op != OpCodes.Ldarga && op != OpCodes.Ldarga_S)
                {
                    int index = line.operand != null ? Convert.ToInt32(line.operand) : op.Name[^1] - '0';
                    stack.Add(arguments != null && index >= 0 && index < arguments.Length ? arguments[index] : null);
                    continue;
                }
                if (op == OpCodes.Ldtoken || op == OpCodes.Ldstr || op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
                { if (op == OpCodes.Ldvirtftn) Pop(stack); stack.Add(line.operand); continue; }
                if (op.Name.StartsWith("ldc.i4", StringComparison.Ordinal))
                { stack.Add(op == OpCodes.Ldc_I4_M1 ? -1 : line.operand != null ? Convert.ToInt32(line.operand) : op.Name[^1] - '0'); continue; }
                if (op == OpCodes.Ldsfld)
                { stack.Add(Read((FieldInfo)line.operand)); continue; }
                if (op == OpCodes.Stsfld)
                { Store(fields, (FieldInfo)line.operand, Pop(stack)); continue; }
                if (op == OpCodes.Dup) { stack.Add(stack.Count == 0 ? null : stack[^1]); continue; }
                if (op.Name.StartsWith("stloc", StringComparison.Ordinal))
                { Store(locals, Local(line), Pop(stack)); continue; }
                if (op.Name.StartsWith("ldloc", StringComparison.Ordinal) && op != OpCodes.Ldloca && op != OpCodes.Ldloca_S)
                { stack.Add(locals.TryGetValue(Local(line), out object value) ? value : null); continue; }
                if (op == OpCodes.Newarr)
                { object count = Pop(stack); stack.Add(count is int size && size >= 0 && size <= 32 ? new object[size] : null); continue; }
                if (op == OpCodes.Stelem_Ref)
                {
                    object value = Pop(stack), index = Pop(stack), array = Pop(stack);
                    if (array is object[] elements && index is int at && at >= 0 && at < elements.Length) elements[at] = value;
                    continue;
                }
                if (op == OpCodes.Castclass || op == OpCodes.Isinst || op == OpCodes.Box || op == OpCodes.Unbox_Any) continue;
                if (op == OpCodes.Conv_I1 || op == OpCodes.Conv_I2 || op == OpCodes.Conv_I4 || op == OpCodes.Conv_I8) continue;
                if (line.operand is MethodBase call && (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj))
                {
                    int count = call.GetParameters().Length + (!call.IsStatic && op != OpCodes.Newobj ? 1 : 0);
                    var values = new object[count];
                    for (int i = count - 1; i >= 0; i--) values[i] = Pop(stack);
                    object value = null;
                    if (op == OpCodes.Newobj && typeof(Delegate).IsAssignableFrom(call.DeclaringType))
                        value = values.LastOrDefault() as MethodInfo;
                    else if (call is MethodInfo method)
                    {
                        if (Invokes(method))
                        {
                            if (values.FirstOrDefault() is MethodInfo target)
                            { if (invoked != null) { invoked[line] = target; InvokedCallbacks.Add(target); } }
                            else Unresolved++;
                        }
                        if (invoked != null && (WritesField(method) || setters.Matches(method)))
                        {
                            if (values.Length >= 3 && values[0] is FieldInfo field)
                            {
                                if (DestructiveValue(field, values[2]))
                                { RemovalSites.Add(line); StateCommits.Add(line); }
                                else if (values[2] == null && DynamicWriteCatalogue.Vital(field)) Unresolved++;
                            }
                            else Unresolved++;
                        }
                        if (invoked != null) FollowDelegates(method, values, invoked);
                        value = Evaluate(method, values);
                    }
                    if (op == OpCodes.Newobj || call is MethodInfo info && info.ReturnType != typeof(void)) stack.Add(value);
                    continue;
                }
                if (op == OpCodes.Ret)
                {
                    object value = Pop(stack);
                    returned = !hasReturn || Equals(returned, value) ? value : null;
                    hasReturn = true; stack.Clear(); continue;
                }
                if (op == OpCodes.Ldfld || op == OpCodes.Ldflda) { Pop(stack); stack.Add(null); continue; }
                int pops = PopCount(op.StackBehaviourPop);
                if (pops < 0) stack.Clear(); else for (int i = 0; i < pops; i++) Pop(stack);
                int pushes = op.StackBehaviourPush == StackBehaviour.Push0 ? 0 : op.StackBehaviourPush == StackBehaviour.Push1_push1 ? 2 : 1;
                for (int i = 0; i < pushes; i++) stack.Add(null);
                if (op.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw) stack.Clear();
            }
            return returned;
        }

        private void FollowDelegates(MethodInfo method, object[] values, Dictionary<CodeInstruction, MethodInfo> invoked)
        {
            if (!LifeResourceEvidence.IsCustom(method.DeclaringType) || method.ContainsGenericParameters) return;
            ParameterInfo[] parameters = method.GetParameters();
            int offset = method.IsStatic ? 0 : 1;
            bool known = false;
            for (int i = 0; i < parameters.Length; i++)
                known |= typeof(Delegate).IsAssignableFrom(parameters[i].ParameterType) && values[i + offset] is MethodInfo;
            if (!known || !forwarding.Add(method)) return;
            try
            {
                if (++expansions > 128) { Unresolved++; return; }
                List<CodeInstruction> code = reader.Read(method);
                if (code != null) Walk(code, invoked, values);
            }
            finally { forwarding.Remove(method); }
        }

        private object Evaluate(MethodInfo method, object[] values)
        {
            if (method.DeclaringType == typeof(Type) && method.Name == nameof(Type.GetTypeFromHandle)) return values.FirstOrDefault();
            if (method.DeclaringType == typeof(MethodBase) && method.Name == nameof(MethodBase.GetMethodFromHandle)) return values.FirstOrDefault();
            if ((method.DeclaringType == typeof(Type) && method.Name == nameof(Type.GetField)
                || method.DeclaringType == typeof(AccessTools) && method.Name == nameof(AccessTools.Field))
                && values.Length >= 2 && values[0] is Type fieldOwner && values[1] is string fieldName)
                return fieldOwner.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if ((method.DeclaringType == typeof(Type) && method.Name == nameof(Type.GetMethod)
                || method.DeclaringType == typeof(AccessTools) && (method.Name == nameof(AccessTools.Method) || method.Name == nameof(AccessTools.DeclaredMethod)))
                && values.Length >= 2 && values[0] is Type owner && values[1] is string name)
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                if (method.Name == nameof(AccessTools.DeclaredMethod)) flags |= BindingFlags.DeclaredOnly;
                Type[] signature = null;
                ParameterInfo[] parameters = method.GetParameters();
                int offset = method.IsStatic ? 0 : 1;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType == typeof(BindingFlags) && values[i + offset] is int mode) flags = (BindingFlags)mode;
                    if (parameters[i].ParameterType == typeof(Type[]) && values[i + offset] is object[] types && types.All(t => t is Type))
                        signature = types.Cast<Type>().ToArray();
                }
                MethodInfo result = null;
                foreach (MethodInfo candidate in LifeDiscoveryMetadata.Methods(owner, flags))
                {
                    if (candidate.Name != name || signature != null && !candidate.GetParameters().Select(p => p.ParameterType).SequenceEqual(signature)) continue;
                    if (result != null) return null;
                    result = candidate;
                }
                return result;
            }
            if (method.Name == nameof(MethodInfo.CreateDelegate)
                && (typeof(MethodInfo).IsAssignableFrom(method.DeclaringType) || method.DeclaringType == typeof(Delegate)))
                return values.OfType<MethodInfo>().FirstOrDefault();
            if (method.DeclaringType == typeof(AccessTools) && method.Name == nameof(AccessTools.MethodDelegate))
                return values.OfType<MethodInfo>().FirstOrDefault();
            return Return(method);
        }

        private static bool DestructiveValue(FieldInfo field, object value)
        {
            if (value is not int number) return false;
            return field.DeclaringType == typeof(Pawn_HealthTracker) && field.Name == "healthState" && number == (int)PawnHealthState.Dead
                || field.DeclaringType == typeof(Thing) && field.Name == "mapIndexOrState" && number < 0
                || field.DeclaringType == typeof(Pawn) && field.Name == "markedForDiscard" && number != 0;
        }

        private static object Pop(List<object> stack)
        { if (stack.Count == 0) return null; object value = stack[^1]; stack.RemoveAt(stack.Count - 1); return value; }
        private static void Store<TKey>(Dictionary<TKey, object> values, TKey key, object value) =>
            values[key] = values.TryGetValue(key, out object before) && !Equals(before, value) ? null : value;
        private static int Local(CodeInstruction line) => line.operand is LocalBuilder local ? local.LocalIndex
            : line.operand != null ? Convert.ToInt32(line.operand) : line.opcode.Name[^1] - '0';
        private static int PopCount(StackBehaviour value) => value switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1 or StackBehaviour.Popref_popi_popi
                or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8
                or StackBehaviour.Popref_popi_popref => 3,
            _ => -1
        };
    }
}
