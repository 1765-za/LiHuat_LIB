using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class GeneratedMethodCatalogue
    {
        internal sealed class Policy
        {
            internal readonly FieldInfo[] Fields;
            internal readonly bool Authority;
            internal readonly bool Indirect;
            internal readonly bool Collections;
            internal bool EntryRequired => Fields.Length != 0 || Authority || Indirect;
            internal bool Required => EntryRequired || Collections;
            internal Policy(FieldInfo[] fields, bool authority, bool indirect, bool collections)
            { Fields = fields; Authority = authority; Indirect = indirect; Collections = collections; }
        }

        private static readonly ConditionalWeakTable<MethodInfo, Policy> policies = new();
        private static readonly ConditionalWeakTable<Assembly, Eligibility> assemblies = new();
        private sealed class Eligibility
        {
            internal readonly bool Generated;
            internal Eligibility(Assembly assembly) => Generated = assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location);
        }

        internal static bool TryGet(MethodInfo method, out Policy policy, bool generated = false, bool definition = false)
        {
            policy = null;
            if (!DynamicWriteCatalogue.Ready || DynamicWriteCatalogue.Building || method == null || method is DynamicMethod) return false;
            if (!generated && !(method.ReturnType.IsByRef && LifeResourceEvidence.IsCustom(method.DeclaringType))
                && !assemblies.GetValue(method.Module.Assembly, assembly => new Eligibility(assembly)).Generated) return false;
            if (method.IsAbstract || method.ContainsGenericParameters && !definition || GeneratedIntegrity.Owns(method)
                || PatchProvenance.ReplacesInfrastructure(method)) return false;
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try { policy = policies.GetValue(method, Analyze); return policy.Required; }
            finally { DynamicWriteCatalogue.Building = previous; }
        }

        internal static bool Sensitive(FieldInfo field) => DynamicWriteCatalogue.Vital(field) || GeneratedIntegrity.Owns(field)
            || PawnRuntimeComponents.IsRoot(field) || field.DeclaringType == typeof(HediffSet) && field.Name == nameof(HediffSet.pawn);
        internal static void Invalidate() => policies.Clear();

        private static Policy Analyze(MethodInfo root)
        {
            var fields = new HashSet<FieldInfo>();
            var visited = new HashSet<MethodInfo>();
            var pending = new Stack<MethodInfo>();
            pending.Push(root);
            bool authority = false, indirect = false, collections = false;
            while (pending.Count != 0)
            {
                MethodInfo method = pending.Pop();
                if (!visited.Add(method) || method.IsAbstract) continue;
                FieldInfo accessor = UnsafeFieldMetadata.Resolve(method);
                if (accessor != null && Sensitive(accessor)) fields.Add(accessor);
                MethodBody body = NativeRuntime.SourceBody(method);
                if (body == null) continue;
                List<CodeInstruction> lines = LifeIlReader.Decode(method, CilRewrite.ReadSource(method, body));
                if (lines == null) throw new BadImageFormatException("Generated method body could not be decoded: " + method);
                foreach (CodeInstruction line in lines)
                {
                    if ((line.opcode == OpCodes.Stfld || line.opcode == OpCodes.Stsfld
                        || DynamicWriteCatalogue.AddressResult(method.ReturnType) && (line.opcode == OpCodes.Ldflda || line.opcode == OpCodes.Ldsflda))
                        && line.operand is FieldInfo field && Sensitive(field))
                        fields.Add(field);
                    if (line.operand is not MethodBase called || line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt
                        && line.opcode != OpCodes.Newobj && line.opcode != OpCodes.Ldftn) continue;
                    authority |= RuntimeAuthorityCalls.Changes(called);
                    collections |= called is MethodInfo collection && HediffListCalls.Known(collection);
                    if (called is MethodInfo helper && (helper.Module.Assembly == root.Module.Assembly
                        || FieldReferenceConsumer.Special(helper))) pending.Push(helper);
                }
                indirect |= IndirectFieldMutation.Writes(lines, Sensitive);
            }
            return new Policy(fields.ToArray(), authority, indirect, collections);
        }
    }
}
