using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class VanillaEntryBoundary
    {
        internal static void Install(LifeIlReader reader)
        {
            Integrations.AnomalyTransferIntegration.Install();
            foreach (Type type in new[] { typeof(Building_SubcoreScanner), typeof(Building_GeneExtractor), typeof(Building_GrowthVat) })
                Scope(AccessTools.DeclaredMethod(type, nameof(Building_Enterable.TryAcceptPawn)), nameof(BeginBuilding), 0, 1);
            Scope(AccessTools.Method(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.TryAcceptPawn),
                new[] { typeof(Pawn), typeof(CompBiosculpterPod_Cycle) }), nameof(BeginPod), 0, 1);
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(JobDriver_EnterPortal)))
                if (!method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0
                    && reader.Read(method)?.Any(i => i.operand is MethodInfo called && called.DeclaringType == typeof(Thing)
                        && called.Name == nameof(Thing.DeSpawnOrDeselect)) == true)
                    Scope(method, nameof(BeginPortal), 0);
        }

        private static void Scope(MethodInfo method, string begin, params int[] arguments) =>
            NativeIlGuards.Scoped(method, AccessTools.Method(typeof(VanillaEntryBoundary), begin), arguments,
                AccessTools.Method(typeof(VanillaEntryBoundary), nameof(End)), Array.Empty<int>());

        private static bool BeginBuilding(Building_Enterable building, Pawn pawn, out ContainerTransferScope scope)
            => Begin(pawn, building.GetDirectlyHeldThings(), out scope);
        private static bool BeginPod(CompBiosculpterPod pod, Pawn pawn, out ContainerTransferScope scope, ref bool result)
            => Begin(pawn, pod.GetDirectlyHeldThings(), out scope);
        private static bool Begin(Pawn pawn, ThingOwner destination, out ContainerTransferScope scope)
        {
            scope = null;
            if (!ContainerTransferScope.AllowsEnter(pawn, destination)) return false;
            scope = ContainerTransferScope.Enter(pawn, destination);
            return true;
        }
        private static bool BeginPortal(JobDriver_EnterPortal driver, out ContainerTransferScope scope)
        {
            scope = null;
            Map destination = driver.MapPortal.GetOtherMap();
            if (!ContainerTransferScope.AllowsLeave(driver.pawn, destination)) return false;
            scope = ContainerTransferScope.ToMap(driver.pawn, destination);
            return true;
        }
        private static void End(ContainerTransferScope scope) => scope?.Dispose();
    }
}
