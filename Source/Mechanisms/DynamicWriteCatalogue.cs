using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LihuatLib.Core;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicWriteCatalogue
    {
        internal sealed class Entry
        {
            internal readonly bool Trusted;
            internal readonly bool OwnedEmission;
            internal readonly HashSet<FieldInfo> Fields = new HashSet<FieldInfo>();
            internal readonly HashSet<DynamicMethod> Calls = new();
            internal bool Destructive;
            internal bool ChangesAuthority;
            internal DynamicCallReceivers Receivers;
            internal DynamicAddressWrites Addresses;
            internal ILGenerator Generator;
            internal Entry(Assembly creator)
            {
                OwnedEmission = InfrastructureAssemblies.Contains(creator);
                Trusted = OwnedEmission || creator == typeof(Pawn).Assembly;
            }
        }
        private static readonly ConditionalWeakTable<DynamicMethod, Entry> methods = new ConditionalWeakTable<DynamicMethod, Entry>();
        private static readonly ConditionalWeakTable<ILGenerator, Entry> generators = new();
        internal static readonly Type GeneratorType = typeof(ILGenerator).Assembly.GetType("System.Reflection.Emit.DynamicILGenerator", true);
        private static readonly FieldInfo owner = AccessTools.Field(GeneratorType, "m_methodBuilder");
        internal static bool Ready;
        [ThreadStatic] internal static bool Building;

        internal static void Observe(ILGenerator generator, OpCode opcode, FieldInfo field)
        {
            if (!Ready || Building || field == null) return;
            bool publication = opcode == OpCodes.Ldsfld && RuntimeDelegateBody.SourceOf(field) is MethodInfo original
                && ReverseLifecycle.Destructive(original);
            bool address = opcode == OpCodes.Ldflda || opcode == OpCodes.Ldsflda;
            if (!publication && !address && opcode != OpCodes.Stfld && opcode != OpCodes.Stsfld) return;
            if (!GeneratorType.IsInstanceOfType(generator)) return;
            if (!publication && !GeneratedMethodCatalogue.Sensitive(field)) return;
            Entry entry = For(generator);
            if (entry.Trusted) return;
            if (address && !ReturnsReference(generator))
            {
                (entry.Addresses ??= new DynamicAddressWrites(generator)).Observe(field);
                return;
            }
            lock (entry.Fields)
            {
                if (publication)
                {
                    entry.Destructive = true;
                    (entry.Receivers ??= new DynamicCallReceivers(generator)).Observe(field);
                }
                else entry.Fields.Add(field);
            }
        }
        internal static void ObserveCall(ILGenerator generator, OpCode opcode, MethodInfo called)
        {
            if (!Ready || Building || called == null || opcode != OpCodes.Call && opcode != OpCodes.Callvirt
                || !GeneratorType.IsInstanceOfType(generator)) return;
            if (generators.TryGetValue(generator, out Entry observed)) observed.Addresses?.Observe(called);
            bool destructive = ReverseLifecycle.Destructive(called);
            bool authority = RuntimeAuthorityCalls.Changes(called);
            bool invocation = RuntimePublicationCalls.IsInvocation(called);
            if (!destructive && !authority && !invocation && called is not DynamicMethod) return;
            Entry entry = For(generator);
            if (entry.Trusted) return;
            lock (entry.Fields)
            {
                entry.Destructive |= destructive;
                entry.ChangesAuthority |= authority;
                if (destructive || invocation) (entry.Receivers ??= new DynamicCallReceivers(generator)).Observe(called);
                if (called is DynamicMethod dependency) entry.Calls.Add(dependency);
            }
        }
        internal static void ObserveConstructor(ILGenerator generator, OpCode opcode, ConstructorInfo constructor)
        {
            if (!Ready || Building || constructor == null || opcode != OpCodes.Newobj && opcode != OpCodes.Call
                || !GeneratorType.IsInstanceOfType(generator)) return;
            if (generators.TryGetValue(generator, out Entry observed)) observed.Addresses?.Observe(constructor);
            if (!RuntimeAuthorityCalls.Changes(constructor)) return;
            Entry entry = For(generator);
            if (!entry.Trusted) lock (entry.Fields) entry.ChangesAuthority = true;
        }
        internal static Entry For(ILGenerator generator) => generators.GetValue(generator, RegisterGenerator);
        internal static bool ReturnsReference(ILGenerator generator) => AddressResult(((DynamicMethod)owner.GetValue(generator)).ReturnType);
        internal static bool AddressResult(Type type) => type.IsByRef || type.IsPointer || type.IsByRefLike || type == typeof(IntPtr) || type == typeof(UIntPtr);
        internal static void Prepare(DynamicMethod method)
        {
            if (!Ready || Building || !methods.TryGetValue(method, out Entry entry) || entry.Trusted) return;
            PrepareDependencies(method, new HashSet<DynamicMethod>());
        }
        private static void PrepareDependencies(DynamicMethod method, HashSet<DynamicMethod> seen)
        {
            if (!seen.Add(method) || PatchProvenance.ReplacesInfrastructure(method)
                || !methods.TryGetValue(method, out Entry entry) || entry.Trusted) return;
            entry.Addresses?.Prepare();
            DynamicMethod[] calls;
            lock (entry.Fields) calls = entry.Calls.ToArray();
            foreach (DynamicMethod called in calls) PrepareDependencies(called, seen);
        }
        private static Entry RegisterGenerator(ILGenerator generator)
        {
            Entry entry = Register((DynamicMethod)owner.GetValue(generator));
            entry.Generator = generator;
            return entry;
        }
        internal static bool OwnsEmission(ILGenerator generator) => GeneratorType.IsInstanceOfType(generator) && For(generator).OwnedEmission;
        internal static bool OwnsMethod(DynamicMethod method) => Register(method).OwnedEmission;
        internal static Entry Register(DynamicMethod method)
        {
            if (methods.TryGetValue(method, out Entry entry)) return entry;
            Assembly creator = Creator();
            return methods.GetValue(method, _ => new Entry(creator));
        }
        internal static bool TryPolicy(DynamicMethod method, out FieldInfo[] fields, out bool changesAuthority)
        {
            changesAuthority = false;
            if (PatchProvenance.ReplacesInfrastructure(method))
            { fields = Array.Empty<FieldInfo>(); return false; }
            DynamicIlCatalogue.Analyze(method);
            if (!methods.TryGetValue(method, out Entry entry) || entry.Trusted)
            { fields = Array.Empty<FieldInfo>(); return false; }
            var seen = new HashSet<DynamicMethod>();
            var writes = new HashSet<FieldInfo>();
            bool destructive = Collect(method, seen, writes, ref changesAuthority);
            fields = writes.ToArray();
            return destructive || changesAuthority || fields.Length > 0;
        }
        private static bool Collect(DynamicMethod method, HashSet<DynamicMethod> seen, HashSet<FieldInfo> writes, ref bool changesAuthority)
        {
            DynamicIlCatalogue.Analyze(method);
            if (!seen.Add(method) || !methods.TryGetValue(method, out Entry entry) || entry.Trusted) return false;
            bool destructive;
            DynamicMethod[] calls;
            lock (entry.Fields)
            {
                writes.UnionWith(entry.Fields);
                changesAuthority |= entry.ChangesAuthority;
                destructive = entry.Destructive && (entry.Receivers?.RequiresProtection(method) ?? true);
                calls = entry.Calls.ToArray();
            }
            foreach (DynamicMethod called in calls) destructive |= Collect(called, seen, writes, ref changesAuthority);
            return destructive;
        }
        internal static bool TryGet(DynamicMethod method, out Entry entry) => methods.TryGetValue(method, out entry);
        internal static bool Vital(FieldInfo field) => field.DeclaringType == typeof(Pawn_HealthTracker) && (field.Name == "healthState" || field.Name == "pawn" || field.Name == "hediffSet")
            || field.DeclaringType == typeof(Thing) && (field.Name == "mapIndexOrState" || field.Name == "def" || field.Name == "thingIDNumber")
            || field.DeclaringType == typeof(Pawn) && (field.Name == "markedForDiscard" || field.Name == "health" || field.Name == "kindDef")
            || PawnOwnedStructures.Backlink(field);
        private static Assembly Creator()
        {
            foreach (StackFrame frame in new StackTrace(false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                MethodBase method = frame.GetMethod();
                MethodBase source = PatchProvenance.InfrastructureSource(method);
                if (source != null) return source.Module.Assembly;
                Type type = method?.DeclaringType;
                if (type == null || type == typeof(DynamicWriteCatalogue) || type == typeof(DynamicWriteBoundary) || type == typeof(DynamicIlCatalogue)) continue;
                Assembly assembly = type.Assembly;
                if (assembly == typeof(object).Assembly || assembly == typeof(Harmony).Assembly) continue;
                return assembly;
            }
            return null;
        }
    }
}
