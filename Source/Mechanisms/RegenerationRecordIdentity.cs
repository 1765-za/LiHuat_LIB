using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RegenerationRecordIdentity
    {
        private static readonly Dictionary<Type, FieldInfo> fields = new Dictionary<Type, FieldInfo>();
        private static readonly HashSet<Assembly> indexed = new HashSet<Assembly>();
        private static readonly HashSet<FieldInfo> loadIds = new();
        internal static bool IsLoadId(FieldInfo field) => loadIds.Contains(field);
        internal static FieldInfo Find(Type record)
        {
            if (indexed.Add(record.Assembly)) Index(record.Assembly);
            return fields.TryGetValue(record, out FieldInfo known) ? known : null;
        }
        private static void Index(Assembly assembly)
        {
            Type[] types = LifeDiscoveryMetadata.Types(assembly);
            var found = new Dictionary<Type, HashSet<FieldInfo>>();
            var reader = new LifeIlReader(method => method.GetMethodBody()?.GetILAsByteArray(),
                method => method.GetMethodBody()?.LocalVariables.Select(v => v.LocalType).ToArray() ?? Type.EmptyTypes);
            foreach (Type owner in types)
            {
                if (owner == null || owner.ContainsGenericParameters || !LifeResourceEvidence.IsCustom(owner)) continue;
                foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(owner, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    Collect(method, assembly, reader, found);
                foreach (ConstructorInfo constructor in owner.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    Collect(constructor, assembly, reader, found);
            }
            foreach (var entry in found) fields[entry.Key] = entry.Value.Count == 1 ? entry.Value.Single() : null;
        }
        private static void Collect(MethodBase method, Assembly assembly, LifeIlReader reader, Dictionary<Type, HashSet<FieldInfo>> found)
        {
            if (method is MethodInfo info && !LifeIlReader.HasMetadataBody(info)) return;
            var parameters = method.GetParameters();
            if (!parameters.Any(p => typeof(Pawn).IsAssignableFrom(p.ParameterType))) return;
            Type[] records = parameters.Select(p => p.ParameterType).Append(method is MethodInfo returned ? returned.ReturnType : typeof(void))
                .Append(method.IsStatic ? typeof(void) : method.DeclaringType).Where(t => t.Assembly == assembly
                && t.IsClass && !t.ContainsGenericParameters && LifeResourceEvidence.IsCustom(t)).ToArray();
            if (records.Length == 0) return;
            List<CodeInstruction> code = method is MethodInfo body ? reader.Read(body) : LifeIlReader.Decode(method, method.GetMethodBody()?.GetILAsByteArray());
            if (code == null) return;
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Stfld || !(code[i].operand is FieldInfo field)
                    || !records.Contains(field.DeclaringType) || field.FieldType != typeof(string)) continue;
                int previous = i - 1;
                while (previous >= 0 && code[previous].opcode == OpCodes.Nop) previous--;
                if (previous >= 0 && (code[previous].opcode == OpCodes.Call || code[previous].opcode == OpCodes.Callvirt)
                    && code[previous].operand is MethodInfo source && source.DeclaringType == typeof(Thing)
                    && (source.Name == "get_ThingID" || source.Name == nameof(Thing.GetUniqueLoadID)))
                {
                    if (source.Name == nameof(Thing.GetUniqueLoadID)) loadIds.Add(field);
                    if (!found.TryGetValue(field.DeclaringType, out HashSet<FieldInfo> identities))
                        found.Add(field.DeclaringType, identities = new HashSet<FieldInfo>());
                    identities.Add(field);
                }
            }
        }
    }
}
