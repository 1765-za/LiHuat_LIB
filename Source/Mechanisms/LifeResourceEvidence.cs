using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    public sealed class LifeResourceEvidence
    {
        private readonly Dictionary<Type, LifeAnalysisResult> observed = new Dictionary<Type, LifeAnalysisResult>();
        private readonly Dictionary<MethodInfo, LifeAnalysisResult> callbacks = new Dictionary<MethodInfo, LifeAnalysisResult>();
        public static int PreparedTypes => LifeAnalysisCoordinator.Shared?.PreparedTypes ?? 0;
        public static int PreparedCounters => LifeAnalysisCoordinator.Shared?.PreparedCounters ?? 0;
        public int CallbackMethods => callbacks.Count;
        public int UnreadableMethods { get; private set; }
        public int LimitedAnalyses { get; private set; }
        internal static void PrepareType(Type type)
        {
            if (IsCustom(type)) LifeAnalysisCoordinator.Shared?.Request(new LifeAnalysisKey(LifeAnalysisKind.Type, type));
        }
        private LifeAnalysisResult ObserveType(Type type)
        {
            if (!Configuration.HighMechanismPolicy.Scanning || !IsCustom(type)) return null;
            LifeAnalysisResult result = LifeAnalysisCoordinator.Shared.Resolve(new LifeAnalysisKey(LifeAnalysisKind.Type, type));
            if (result == null || observed.TryGetValue(type, out LifeAnalysisResult previous) && previous == result) return result;
            observed[type] = result; UnreadableMethods += result.Unreadable; LimitedAnalyses += result.Limited;
            return result;
        }
        internal LifeAnalysisResult Observe(object value, Pawn owner, HashSet<FieldInfo> fields)
        {
            LifeAnalysisResult prepared = ObserveType(value.GetType());
            if (fields != null) CollectFields(value, owner, fields, prepared);
            if (prepared?.Sites.Length > 0) LifeExecutionGuard.Register(prepared.Sites, value, owner);
            return prepared;
        }
        private static LifeAnalysisResult TypeResult(Type type) => Configuration.HighMechanismPolicy.Scanning && IsCustom(type)
            ? LifeAnalysisCoordinator.Shared?.Find(new LifeAnalysisKey(LifeAnalysisKind.Type, type)) : null;
        public HashSet<FieldInfo> Fields(object value, Pawn owner)
        {
            var fields = new HashSet<FieldInfo>();
            CollectFields(value, owner, fields, TypeResult(value.GetType()));
            return fields;
        }
        private static void CollectFields(object value, Pawn owner, HashSet<FieldInfo> fields, LifeAnalysisResult prepared)
        {
            fields.Clear();
            Type type = value.GetType();
            if (prepared == null) return;
            foreach (LifeDependency dependency in prepared.Dependencies)
            {
                bool applies = dependency.Receivers.Length > 0;
                foreach (var path in dependency.Receivers)
                    if (!path.ReachesOwner(value, owner)) { applies = false; break; }
                if (!applies) continue;
                foreach (FieldInfo field in dependency.Fields)
                    if (field.DeclaringType.IsAssignableFrom(type)) fields.Add(field);
            }
        }
        internal void BindDeferred(Delegate work, Pawn owner, bool enabled)
        {
            if (!Configuration.HighMechanismPolicy.ScannedProtection) return;
            Delegate[] calls = work.GetInvocationList();
            foreach (Delegate call in calls)
            {
                MethodInfo method = call.Method;
                if (call.Target == null || !IsCustom(method.DeclaringType) || method.IsStatic || method.ContainsGenericParameters) continue;
                var key = new LifeAnalysisKey(LifeAnalysisKind.Callback, method);
                if (!callbacks.TryGetValue(method, out LifeAnalysisResult result)
                    || result != LifeAnalysisCoordinator.Shared.Find(key))
                {
                    if (!enabled) continue;
                    result = LifeAnalysisCoordinator.Shared.Resolve(key);
                    callbacks[method] = result;
                    UnreadableMethods += result.Unreadable; LimitedAnalyses += result.Limited;
                }
                LifeExecutionGuard.Unbind(result.Sites, call.Target);
                if (enabled) LifeExecutionGuard.Register(result.Sites, call.Target, owner);
            }
        }
        internal bool TryInspectCounterMethod(MethodInfo method, out MethodInfo[] methods)
        {
            if (!Configuration.HighMechanismPolicy.Scanning) { methods = Array.Empty<MethodInfo>(); return true; }
            var key = new LifeAnalysisKey(LifeAnalysisKind.Counter, method);
            if (!LifeAnalysisCoordinator.Shared.Request(key)) { methods = Array.Empty<MethodInfo>(); LimitedAnalyses++; return true; }
            LifeAnalysisResult prepared = LifeAnalysisCoordinator.Shared.Find(key);
            methods = prepared?.Methods;
            if (prepared == null) return false;
            UnreadableMethods += prepared.Unreadable; LimitedAnalyses += prepared.Limited;
            return true;
        }
        internal static bool CounterPrepared(MethodInfo method) => LifeAnalysisCoordinator.Shared?.Find(new LifeAnalysisKey(LifeAnalysisKind.Counter, method)) != null;
        public static bool IsCustom(Type type) => type != null && type.Assembly != typeof(Pawn).Assembly
            && !LifePlatformAssemblies.Contains(type.Assembly)
            && type.Assembly != typeof(object).Assembly && !typeof(Def).IsAssignableFrom(type)
            && type.Assembly != typeof(HashSet<>).Assembly && type.Assembly != typeof(Uri).Assembly
            && type.Assembly != typeof(System.Xml.XmlReader).Assembly && type.Assembly != typeof(System.Xml.Linq.XElement).Assembly
            && type.Assembly != typeof(UnityEngine.Mathf).Assembly && type.Assembly != typeof(Harmony).Assembly
            && !typeof(UnityEngine.Object).IsAssignableFrom(type) && !typeof(Delegate).IsAssignableFrom(type)
            && !InfrastructureAssemblies.Contains(type.Assembly);

        public static bool Numeric(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int)
            || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong);
        internal static bool ScalarState(Type type) => Numeric(type) || type == typeof(bool) || type.IsEnum;

        internal static bool ExternalRecord(Type type) => IsCustom(type) && !typeof(Thing).IsAssignableFrom(type)
            && !typeof(ThingComp).IsAssignableFrom(type) && !typeof(Hediff).IsAssignableFrom(type)
            && !typeof(HediffComp).IsAssignableFrom(type) && !typeof(GameComponent).IsAssignableFrom(type)
            && !typeof(MapComponent).IsAssignableFrom(type) && !typeof(RimWorld.Planet.WorldComponent).IsAssignableFrom(type);
    }
}
