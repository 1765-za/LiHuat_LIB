using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeValueSource
    {
        public int Index;
        public bool Local;
        public Type Type;
        public FieldInfo[] Fields;

        public object Read(object value)
        {
            foreach (FieldInfo field in Fields)
            {
                if (value == null || !field.DeclaringType.IsInstanceOfType(value)) return null;
                value = field.GetValue(value);
            }
            return value;
        }

        public IEnumerable<CodeInstruction> Load()
        {
            yield return Local ? CodeInstruction.LoadLocal(Index) : CodeInstruction.LoadArgument(Index);
            if (Type.IsValueType) yield return new CodeInstruction(OpCodes.Box, Type);
        }

        public static LifeValueSource ResolveOwner(MethodInfo method, LifeObjectPath[] owners, LifeFlowValue[] args,
            Dictionary<int, LifeFlowValue> locals, Type[] variables)
        {
            LifeObjectPath[] identities = owners.Select(p => p.PawnIdentity()).Distinct().ToArray();
            if (identities.Length != 1) return null;
            Type[] types = (method.IsStatic ? Enumerable.Empty<Type>() : new[] { method.DeclaringType })
                .Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
            for (int i = 0; i < args.Length; i++)
            {
                LifeValueSource source = Resolve(identities[0], args[i], types[i], i, false);
                if (source != null) return source;
            }
            if (variables == null) return null;
            foreach (KeyValuePair<int, LifeFlowValue> local in locals)
            {
                if (local.Key >= variables.Length) continue;
                LifeValueSource source = Resolve(identities[0], local.Value, variables[local.Key], local.Key, true);
                if (source != null) return source;
            }
            return null;
        }

        private static LifeValueSource Resolve(LifeObjectPath owner, LifeFlowValue value, Type type, int index, bool local)
        {
            if (type.IsByRef || type.IsPointer || value.Paths.Length != 1 || !owner.StartsWith(value.Paths[0])) return null;
            FieldInfo[] fields = owner.Fields.Skip(value.Paths[0].Fields.Length).ToArray();
            Type cursor = type;
            foreach (FieldInfo field in fields)
            {
                if (field.IsStatic || !field.DeclaringType.IsAssignableFrom(cursor) || field.FieldType.IsPointer) return null;
                cursor = field.FieldType;
            }
            if (!typeof(Thing).IsAssignableFrom(cursor) && cursor != typeof(Pawn_HealthTracker)) return null;
            return new LifeValueSource { Index = index, Local = local, Type = type, Fields = fields };
        }
    }

    internal sealed class LifeDeferredSite
    {
        public MethodInfo Method;
        public MemberInfo Operation;
        public int Occurrence;
        public int DelegateArgument;
        public LifeValueSource Owner;
        public List<CodeInstruction> OriginalCode;
        public int Id;
        public bool Installed;
        public bool SameLocation(LifeDeferredSite other) => Method == other.Method && Operation == other.Operation
            && Occurrence == other.Occurrence && DelegateArgument == other.DelegateArgument;

        public static Type[] OperandTypes(MemberInfo operation)
        {
            Type[] operands;
            if (operation is MethodInfo method)
                operands = (method.IsStatic ? Enumerable.Empty<Type>() : new[] { method.DeclaringType })
                    .Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
            else if (operation is FieldInfo field)
                operands = field.IsStatic ? new[] { field.FieldType } : new[] { field.DeclaringType, field.FieldType };
            else return null;
            return operands.Any(t => t.IsPointer || t.IsByRef || t.ContainsGenericParameters) ? null : operands;
        }
    }
}
