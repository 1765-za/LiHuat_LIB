using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using LihuatLib.Dispatching;

namespace LihuatLib.Mechanisms
{
    public static class HediffMutationGuard
    {
        private static readonly HashSet<Assembly> examined = new HashSet<Assembly>();
        private static readonly Queue<MethodInfo> pending = new Queue<MethodInfo>();
        private static readonly HashSet<MethodInfo> selected = new();
        private static readonly LifeIlReader reader = new LifeIlReader();
        private static readonly Dictionary<MethodInfo, MethodInfo> calls = new Dictionary<MethodInfo, MethodInfo>();
        private static readonly FieldInfo listField = AccessTools.Field(typeof(HediffSet), "hediffs");
        private static readonly MethodInfo replace = AccessTools.Method(typeof(PinnedHediffs), nameof(PinnedHediffs.ReplaceList));
        private static volatile bool changed;
        private static bool active;
        public static int Installed { get; private set; }
        public static int Pending => pending.Count;
        public static int Rejected { get; private set; }

        static HediffMutationGuard()
        {
            foreach (string name in new[] { "Remove", "RemoveAt", "Clear", "RemoveAll", "RemoveRange", "set_Item" })
            {
                MethodInfo method = AccessTools.Method(typeof(List<Hediff>), name);
                calls[method] = AccessTools.Method(typeof(PinnedHediffs), name == "set_Item" ? "ReplaceItem" : name);
            }
            AppDomain.CurrentDomain.AssemblyLoad += (_, __) => changed = true;
        }
        private static bool Related(Type type) => typeof(Thing).IsAssignableFrom(type) || typeof(Hediff).IsAssignableFrom(type)
            || type == typeof(HediffSet) || type == typeof(Pawn_HealthTracker) || type == typeof(List<Hediff>);

        internal static void Discover()
        {
            if (!LibraryCapabilities.Has(LibraryCapability.HediffProtection)) return;
            if (active && !changed) return;
            active = true; changed = false;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || !examined.Add(assembly) || InfrastructureAssemblies.Contains(assembly)) continue;
                Type[] types = LifeDiscoveryMetadata.Types(assembly);
                foreach (Type type in types)
                {
                    if (type == null || type.IsValueType) continue;
                    if (assembly == typeof(Pawn).Assembly && type != typeof(HediffSet) && type != typeof(Pawn_HealthTracker)) continue;
                    if (assembly != typeof(Pawn).Assembly && !LifeResourceEvidence.IsCustom(type)) continue;
                    foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (!method.IsAbstract && !PatchProvenance.IsReplacement(method) && (Related(type) || Related(method.ReturnType)
                            || method.GetParameters().Any(p => Related(p.ParameterType))) && selected.Add(method)) pending.Enqueue(method);
                }
            }
        }
        internal static void DiscoverCallers()
        {
            if (!LibraryCapabilities.Has(LibraryCapability.HediffProtection) || LifeAnalysisCoordinator.Shared == null) return;
            foreach (MethodInfo target in HediffListCalls.Methods)
                foreach (LifeCallEdge edge in LifeAnalysisCoordinator.Shared.CallGraph.Callers(target))
                {
                    MethodInfo method = edge.Caller;
                    if (method.Module.Assembly != typeof(Pawn).Assembly && !InfrastructureAssemblies.Contains(method.Module.Assembly)
                        && !PatchProvenance.IsReplacement(method) && selected.Add(method)) pending.Enqueue(method);
                }
        }
        internal static void Update()
        {
            if (!active) return;
            if (changed) Discover();
            if (pending.Count == 0) return;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 24 && pending.Count > 0 && watch.ElapsedMilliseconds < 3; i++)
            {
                MethodInfo method = pending.Dequeue();
                try
                {
                    List<CodeInstruction> code = reader.Read(method);
                    bool insertion = method.Module.Assembly != typeof(Pawn).Assembly;
                    if (code == null || !code.Any(line => Replacement(line, insertion) != null)) continue;
                    NativeDispatcher.Register(method, transpiler: new DispatchMethod(typeof(HediffMutationGuard), nameof(Transpile)) { priority = int.MinValue, order = 200 });
                    Installed++;
                }
                catch (Exception) { Rejected++; }
            }
        }
        private static MethodInfo Replacement(CodeInstruction line, bool insertion)
        {
            if (insertion && line.operand is MethodInfo insertionCall && (line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt
                || line.opcode == OpCodes.Ldftn || line.opcode == OpCodes.Ldvirtftn)
                && HediffListCalls.Replacement(insertionCall) is MethodInfo admission) return admission;
            if ((line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt) && line.operand is MethodInfo method
                && calls.TryGetValue(method, out MethodInfo replacement)) return replacement;
            return line.opcode == OpCodes.Stfld && Equals(line.operand, listField) ? replace : null;
        }
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            bool insertion = __originalMethod.Module.Assembly != typeof(Pawn).Assembly;
            foreach (CodeInstruction line in instructions)
            {
                MethodInfo replacement = Replacement(line, insertion);
                if (replacement != null)
                {
                    bool pointer = line.opcode == OpCodes.Ldftn || line.opcode == OpCodes.Ldvirtftn;
                    if (line.opcode == OpCodes.Ldvirtftn)
                    {
                        var check = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HediffListCalls), nameof(HediffListCalls.RequireReceiver)));
                        check.labels.AddRange(line.labels); line.labels.Clear();
                        check.blocks.AddRange(line.blocks); line.blocks.Clear();
                        yield return check;
                    }
                    line.opcode = pointer ? OpCodes.Ldftn : OpCodes.Call; line.operand = replacement;
                }
                yield return line;
            }
        }
    }
}
