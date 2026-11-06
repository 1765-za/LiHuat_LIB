using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class RegenerationSource
    {
        private static readonly Dictionary<Type, RegenerationSource> shapes = new Dictionary<Type, RegenerationSource>();
        internal readonly int Argument;
        internal readonly bool Snapshot;
        internal readonly Type RecordType;
        private readonly Func<object, Pawn> pawn;
        private readonly Func<object, string> serializedIdentity;
        private readonly Func<object, object> record;
        private readonly RegenerationSource canonical;
        internal bool RecordedIdentity => serializedIdentity != null;
        internal RegenerationSource CanonicalRecord => canonical ?? this;
        internal object Record(object input) => input == null ? null : record == null ? input : record(input);
        internal string Identity(object input) => input == null ? null : serializedIdentity?.Invoke(input);
        private RegenerationSource(int argument, Func<object, Pawn> pawn, Type recordType = null)
        {
            Argument = argument; this.pawn = pawn; RecordType = recordType; Snapshot = recordType != null;
        }
        private RegenerationSource(int argument, RegenerationSource shape)
        { Argument = argument; Snapshot = shape.Snapshot; RecordType = shape.RecordType; pawn = shape.pawn; serializedIdentity = shape.serializedIdentity; record = shape.record; canonical = shape.CanonicalRecord; }
        private RegenerationSource(int argument, Type carrier, FieldInfo field, RegenerationSource child)
        {
            Argument = argument; Snapshot = true;
            Func<object, object> read = Read<object>(carrier, new[] { field });
            pawn = child.pawn == null ? null : input => child.Resolve(read(input));
            serializedIdentity = child.RecordedIdentity ? input => child.Identity(read(input)) : null;
            if (child.Snapshot)
            {
                RecordType = child.RecordType; canonical = child.CanonicalRecord;
                record = input => child.Record(read(input));
            }
            else RecordType = carrier;
        }
        private RegenerationSource(int argument, Type record, FieldInfo id)
        {
            Argument = argument; Snapshot = true; RecordType = record;
            var read = Read<string>(record, new[] { id });
            serializedIdentity = RegenerationRecordIdentity.IsLoadId(id) ? input =>
            {
                string value = read(input);
                return value?.StartsWith("Thing_", StringComparison.Ordinal) == true ? value.Substring(6) : value;
            } : read;
        }
        internal Pawn Resolve(object input)
        {
            if (input == null) return null;
            return pawn?.Invoke(input);
        }
        internal static RegenerationSource Find(MethodInfo method)
        {
            Type[] inputs = IndependentIl.Parameters(method);
            ParameterInfo[] parameters = method.GetParameters();
            int offset = method.IsStatic ? 0 : 1;
            RegenerationSource found = null, erased = null;
            bool ambiguousErased = false;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (i >= offset && parameters[i - offset].IsOut) continue;
                Type input = inputs[i].IsByRef ? inputs[i].GetElementType() : inputs[i];
                if (!shapes.TryGetValue(input, out RegenerationSource shape)) shapes[input] = shape = ForType(input, -1, new HashSet<Type>(), 0);
                RegenerationSource candidate = shape == null ? null : new RegenerationSource(i, shape);
                if (candidate == null && i == 0 && !method.IsStatic && RegenerationReceiverSource.Find(method) is FieldInfo member)
                    candidate = new RegenerationSource(0, Read<Pawn>(input, new[] { member }));
                if (candidate == null && (input == typeof(Thing) || input == typeof(ThingWithComps)))
                {
                    if (erased != null) ambiguousErased = true;
                    else erased = new RegenerationSource(i, value => value as Pawn);
                }
                if (candidate == null) continue;
                if (found != null) return null;
                found = candidate;
            }
            return found ?? (ambiguousErased ? null : erased);
        }
        private static FieldInfo[] DirectPath(Type type)
        {
            if (typeof(Pawn).IsAssignableFrom(type)) return Array.Empty<FieldInfo>();
            if (typeof(Hediff).IsAssignableFrom(type)) return new[] { AccessTools.Field(typeof(Hediff), nameof(Hediff.pawn)) };
            if (typeof(HediffComp).IsAssignableFrom(type)) return new[] { AccessTools.Field(typeof(HediffComp), nameof(HediffComp.parent)), AccessTools.Field(typeof(Hediff), nameof(Hediff.pawn)) };
            if (typeof(ThingComp).IsAssignableFrom(type)) return new[] { AccessTools.Field(typeof(ThingComp), nameof(ThingComp.parent)) };
            if (typeof(Pawn_HealthTracker).IsAssignableFrom(type)) return new[] { AccessTools.Field(typeof(Pawn_HealthTracker), "pawn") };
            return null;
        }
        private static RegenerationSource ForType(Type type, int index, HashSet<Type> path, int depth)
        {
            if (shapes.TryGetValue(type, out RegenerationSource known)) return known;
            if (depth > 4 || !path.Add(type)) return null;
            try { return BuildShape(type, index, path, depth); }
            finally { path.Remove(type); }
        }
        private static RegenerationSource BuildShape(Type type, int index, HashSet<Type> path, int depth)
        {
            if (type.IsValueType || type.IsByRef || type.IsPointer || type.ContainsGenericParameters) return null;
            FieldInfo[] direct = DirectPath(type);
            if (direct != null)
            {
                if (!typeof(Pawn).IsAssignableFrom(type))
                    for (Type cursor = type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
                        if (cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            .Any(f => DirectPath(f.FieldType) != null)) return null;
                return new RegenerationSource(index, Read<Pawn>(type, direct));
            }
            if (!LifeResourceEvidence.IsCustom(type)) return null;
            var inherited = new List<FieldInfo>();
            for (Type cursor = type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
                inherited.AddRange(cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            FieldInfo[] fields = inherited.ToArray();
            FieldInfo[] pawns = fields.Where(f => typeof(Pawn).IsAssignableFrom(f.FieldType)).ToArray();
            FieldInfo[] kinds = fields.Where(f => f.FieldType == typeof(PawnKindDef)).ToArray();
            if (pawns.Length == 1 && kinds.Length == 1)
                return new RegenerationSource(index, Read<Pawn>(type, pawns), recordType: type);
            if (pawns.Length == 0 && RegenerationRecordIdentity.Find(type) is FieldInfo recordId)
                return new RegenerationSource(index, type, recordId);
            bool closure = type.IsDefined(typeof(CompilerGeneratedAttribute), false);
            RegenerationSource found = null;
            foreach (FieldInfo field in fields)
            {
                RegenerationSource child = ForType(field.FieldType, -1, path, depth + 1);
                if (child == null || !closure && !child.Snapshot) continue;
                if (found != null) return null;
                found = new RegenerationSource(index, type, field, child);
            }
            return found;
        }
        private static Func<object, T> Read<T>(Type input, FieldInfo[] fields)
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(T), new[] { typeof(object) }, typeof(RegenerationSource).Module, true);
            ILGenerator il = method.GetILGenerator();
            Label missing = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, input);
            foreach (FieldInfo field in fields)
            {
                il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brfalse, missing); il.Emit(OpCodes.Ldfld, field);
            }
            if (!typeof(T).IsValueType) il.Emit(OpCodes.Isinst, typeof(T));
            il.Emit(OpCodes.Ret);
            if (fields.Length > 0)
            {
                il.MarkLabel(missing); il.Emit(OpCodes.Pop);
                if (typeof(T).IsValueType) { LocalBuilder value = il.DeclareLocal(typeof(T)); il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Initobj, typeof(T)); il.Emit(OpCodes.Ldloc, value); }
                else il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
            return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
        }
    }
}
