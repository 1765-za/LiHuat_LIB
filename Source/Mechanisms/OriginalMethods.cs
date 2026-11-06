using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class OriginalMethods
    {
        private sealed class VirtualRoute
        {
            public MethodInfo Method;
            public byte Graph;
            public Type[] Parameters;
            public Type DelegateType;
            public readonly ConcurrentDictionary<Type, Delegate> Targets = new ConcurrentDictionary<Type, Delegate>();
        }

        private static readonly Dictionary<Tuple<MethodInfo, byte>, DynamicMethod> methods = new Dictionary<Tuple<MethodInfo, byte>, DynamicMethod>();
        private static readonly Dictionary<DynamicMethod, MethodInfo> publications = new();
        private static readonly Dictionary<Tuple<MethodInfo, byte>, DynamicMethod> virtualWrappers = new Dictionary<Tuple<MethodInfo, byte>, DynamicMethod>();
        private static readonly List<VirtualRoute> virtualRoutes = new List<VirtualRoute>();
        private static readonly HashSet<string> terminalNames = new HashSet<string>
        {
            "Kill", "SetDead", "Destroy", "DeSpawn", "Discard", "DeSpawnOrDeselect", "MakeCorpse",
            "get_Dead", "get_Destroyed", "get_Spawned", "get_Map", "get_MapHeld", "get_PositionHeld", "ShouldBeDead", "ShouldBeDeadFromRequiredCapacity",
            "ShouldBeDeadFromLethalDamageThreshold", "CheckForStateChange", "RemoveFromHoldingContainer",
            "DoKillSideEffects", "PreDeathPawnModifications", "DropBeforeDying", "DropAndForbidEverything",
            "RemoveComponentsOnKilled", "RemoveComponentsOnDespawned", "PostCorpseDestroy", "DeregisterInAcquiredColonistsHistory",
            "DeRegisterPawn", "DeregisterPawn", "DeRegister", "Deregister", "RemovePawn", "RemoveFromWorldPawns", "Remove",
            "set_InnerPawn", "get_InnerPawn", "TryAdd", "NotifyAdded", "GetCountCanAccept",
            "Notify_PawnDied", "get_HealthScale", "get_SummaryHealthPercent", "GetPartHealth",
            "DeRegisterDrawable", "DeRegisterAllTickabilityFor"
        };
        private static readonly HashSet<string> behaviorNames = new HashSet<string>
        {
            "Tick", "TickInterval", "TickRare", "TickLong", "get_Suspended", "PatherTick", "JobTrackerTick",
            "JobTrackerTickInterval", "DriverTick", "DriverTickInterval", "StanceTrackerTick", "StunHandlerTick",
            "HealthTick", "HealthTickInterval", "MindStateTickInterval", "NeedsTrackerTickInterval", "VerbsTick",
            "NativeVerbsTick", "EquipmentTrackerTick", "AbilitiesTick", "InventoryTrackerTick", "ApparelTrackerTickInterval"
        };
        private static readonly HashSet<string> spawnNames = new HashSet<string>
        {
            "Spawn", "SpawnSetup", "GeneratePawn", "GenerateOrRedressPawnInternal", "GenerateNewPawnInternal", "TryGenerateNewPawnInternal",
            "MakeThing", "CreateInitialComponents", "AddComponentsForSpawn", "InValidState",
            "get_Dead", "get_Downed", "get_Destroyed", "get_Spawned", "get_Map", "get_Active", "set_Position", "set_Rotation",
            "RegisterPawn", "Register", "RegisterInCell", "RegisterInRegions", "RegisterAllTickabilityFor", "RegisterDrawable", "Add", "TryAdd"
        };
        private static readonly HashSet<string> transferNames = new HashSet<string>(spawnNames)
        {
            "DeSpawn", "DeSpawnOrDeselect", "DeRegisterPawn", "DeregisterPawn", "DeRegister", "Deregister",
            "DeregisterInRegions", "DeRegisterAllTickabilityFor", "DeRegisterDrawable", "Remove", "RemoveAt", "TryRemove",
            "Contains", "RemovePawn", "RemoveFromWorldPawns", "RemoveComponentsOnDespawned", "NotifyRemoved", "NotifyAdded", "DeregisterThing", "DeregisterInCell"
        };

        public static T Create<T>(MethodInfo method, bool deathGraph = false) where T : Delegate =>
            Publish<T>(method, Clone(method, deathGraph ? (byte)1 : (byte)0));
        public static T CreateBehavior<T>(MethodInfo method) where T : Delegate => Publish<T>(method, Clone(method, 2));
        public static T CreateSpawn<T>(MethodInfo method) where T : Delegate => Publish<T>(method, Clone(method, 3));
        public static T CreateTransfer<T>(MethodInfo method) where T : Delegate =>
            Publish<T>(method, method.IsVirtual ? VirtualWrapper(method, 4) : Clone(method, 4));
        public static T CreateInjury<T>(MethodInfo method) where T : Delegate => Publish<T>(method, Clone(method, 5));

        private static T Publish<T>(MethodInfo original, DynamicMethod body) where T : Delegate
        {
            lock (methods)
            {
                if (!publications.TryGetValue(body, out MethodInfo published))
                    publications.Add(body, published = ReverseLifecycle.Wrap(body, original));
                return (T)published.CreateDelegate(typeof(T));
            }
        }

        private static bool InjuryEntry(MethodInfo method) =>
            method.DeclaringType == typeof(Pawn_HealthTracker) && method.Name == nameof(Pawn_HealthTracker.AddHediff)
            || method.DeclaringType == typeof(HediffSet) && method.Name == nameof(HediffSet.AddDirect);

        private static Type[] Parameters(MethodInfo method)
        {
            List<Type> parameters = new List<Type>();
            if (!method.IsStatic) parameters.Add(method.DeclaringType.IsValueType ? method.DeclaringType.MakeByRefType() : method.DeclaringType);
            parameters.AddRange(method.GetParameters().Select(p => p.ParameterType));
            return parameters.ToArray();
        }

        private static DynamicMethod Clone(MethodInfo original, byte graph)
        {
            lock (methods)
            {
                Tuple<MethodInfo, byte> key = Tuple.Create(original, graph);
                if (methods.TryGetValue(key, out DynamicMethod existing)) return existing;
                MethodBody body = NativeRuntime.SourceBody(original) ?? throw new InvalidOperationException("Missing metadata IL: " + original);
                DynamicMethod method = new DynamicMethod("LihuatOriginal_" + original.DeclaringType.Name + "_" + original.Name,
                    original.ReturnType, Parameters(original), original.Module, true) { InitLocals = body.InitLocals };
                methods.Add(key, method);
                HashSet<string> selectedNames = graph == 1 ? terminalNames : graph == 3 ? spawnNames : graph == 4 ? transferNames : behaviorNames;
                Func<MethodInfo, bool, MethodInfo> resolver = graph == 0 ? null : new Func<MethodInfo, bool, MethodInfo>((called, virtualCall) =>
                {
                    if (called.DeclaringType == typeof(LongEventHandler) && called.Name == nameof(LongEventHandler.ExecuteWhenFinished))
                        return AccessTools.Method(typeof(DeferredCallbacks), nameof(DeferredCallbacks.Enqueue));
                    if (graph == 1 && DeathPlacementGraph.StartsAt(original, called)) return Clone(called, 6);
                    if (graph == 1 && called.DeclaringType == typeof(RimWorld.PawnComponentsUtility))
                    {
                        if (called.Name == nameof(RimWorld.PawnComponentsUtility.RemoveComponentsOnDespawned))
                            return AccessTools.Method(typeof(LifecycleComponentRelease), nameof(LifecycleComponentRelease.Despawned));
                        if (called.Name == nameof(RimWorld.PawnComponentsUtility.RemoveComponentsOnKilled))
                            return AccessTools.Method(typeof(LifecycleComponentRelease), nameof(LifecycleComponentRelease.Killed));
                    }
                    if (graph == 1 && original.DeclaringType == typeof(Pawn) && original.Name == nameof(Pawn.DeSpawn)
                        && called.DeclaringType == typeof(MapPawns) && called.Name == nameof(MapPawns.DeRegisterPawn))
                        return AccessTools.Method(typeof(LifecycleComponentRelease), nameof(LifecycleComponentRelease.Deregister));
                    bool targetRegistration = (graph == 1 || graph == 3 || graph == 4) && called.DeclaringType == typeof(Verse.AI.AttackTargetsCache)
                        && (called.Name == "Notify_ThingSpawned" || called.Name == "Notify_ThingDespawned" || called.Name == "RegisterTarget" || called.Name == "DeregisterTarget");
                    bool selected = OriginalGraphSelection.PrivateHelper(original, called)
                        || graph == 1 && DeathWorldRegistrationGraph.Includes(called) || (graph == 6 ? DeathPlacementGraph.Includes(called)
                        : graph == 5 ? InjuryEntry(called) : selectedNames.Contains(called.Name));
                    if (called.Module.Assembly != typeof(Pawn).Assembly || !selected && !targetRegistration || called.ContainsGenericParameters) return called;
                    if (virtualCall && called.IsVirtual && !called.IsFinal && !called.DeclaringType.IsValueType) return VirtualWrapper(called, graph);
                    return called.IsAbstract || called.GetMethodBody() == null ? called : Clone(called, graph);
                });
                if (!PortableIlEmitter.TryEmit(original, body, CilRewrite.ReadSource(original, body), method.GetILGenerator(), out string reason, resolver,
                    StarBlood.TickRuntimeRecovery.IndependentRewrite(original, graph, resolver)))
                {
                    methods.Remove(key);
                    throw new InvalidOperationException("Original IL emission failed for " + original + ": " + reason);
                }
                return method;
            }
        }

        private static DynamicMethod VirtualWrapper(MethodInfo called, byte graph)
        {
            Tuple<MethodInfo, byte> key = Tuple.Create(called, graph);
            if (virtualWrappers.TryGetValue(key, out DynamicMethod existing)) return existing;
            Type[] parameters = Parameters(called);
            VirtualRoute route = new VirtualRoute { Method = called, Graph = graph, Parameters = parameters,
                DelegateType = Expression.GetDelegateType(parameters.Concat(new[] { called.ReturnType }).ToArray()) };
            int id = virtualRoutes.Count;
            virtualRoutes.Add(route);
            DynamicMethod wrapper = new DynamicMethod("LihuatDispatch_" + called.Name, called.ReturnType, parameters, called.Module, true);
            ILGenerator il = wrapper.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, id);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(OriginalMethods), nameof(ResolveVirtual)));
            il.Emit(OpCodes.Castclass, route.DelegateType);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Callvirt, route.DelegateType.GetMethod("Invoke"));
            il.Emit(OpCodes.Ret);
            virtualWrappers.Add(key, wrapper);
            return wrapper;
        }

        private static Delegate ResolveVirtual(int routeId, object receiver)
        {
            if (receiver == null) throw new NullReferenceException();
            VirtualRoute route = virtualRoutes[routeId];
            Type type = receiver.GetType();
            if (route.Targets.TryGetValue(type, out Delegate cached)) return cached;
            lock (methods)
            {
                if (route.Targets.TryGetValue(type, out cached)) return cached;
                MethodInfo implementation = route.Method;
                Type[] arguments = route.Method.GetParameters().Select(p => p.ParameterType).ToArray();
                MethodInfo root = route.Method.GetBaseDefinition();
                for (Type current = type; current != null; current = current.BaseType)
                {
                    MethodInfo candidate = current.GetMethod(route.Method.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, arguments, null);
                    if (candidate != null && candidate.Module.Assembly == typeof(Pawn).Assembly && !candidate.IsAbstract && candidate.GetBaseDefinition() == root)
                    {
                        implementation = candidate;
                        break;
                    }
                }
                DynamicMethod target = Clone(implementation, route.Graph);
                DynamicMethod adapter = new DynamicMethod("LihuatTarget_" + type.Name + "_" + route.Method.Name,
                    route.Method.ReturnType, route.Parameters, route.Method.Module, true);
                ILGenerator il = adapter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, implementation.DeclaringType);
                for (int i = 1; i < route.Parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
                il.Emit(OpCodes.Call, target);
                il.Emit(OpCodes.Ret);
                Delegate result = adapter.CreateDelegate(route.DelegateType);
                route.Targets.TryAdd(type, result);
                return result;
            }
        }

        public static readonly Action<Pawn, DamageInfo?, Hediff> Kill = Create<Action<Pawn, DamageInfo?, Hediff>>(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)), true);
        public static readonly Action<Pawn_HealthTracker> SetDead = Create<Action<Pawn_HealthTracker>>(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.SetDead)), true);
        public static readonly Action<Pawn_HealthTracker, DamageInfo?, Hediff> CheckState = Create<Action<Pawn_HealthTracker, DamageInfo?, Hediff>>(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange)), true);
        public static readonly Func<Pawn_HealthTracker, bool> ShouldDie = Create<Func<Pawn_HealthTracker, bool>>(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDead)), true);
    }
}

