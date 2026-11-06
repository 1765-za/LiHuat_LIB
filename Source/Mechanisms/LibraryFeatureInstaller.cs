using HarmonyLib;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    internal static class LibraryFeatureInstaller
    {
        internal static void Install()
        {
            if (!LibraryCapabilities.Has(LibraryCapability.IlDispatch)) return;
            PatchIntegrity.Install();
            RuntimeBodyBoundary.Install();
            ExecutableWriteProtection.Install();
            FieldWriteProtection.Install();
            DynamicWriteBoundary.Install();
            if (LibraryCapabilities.Has(LibraryCapability.StarPaths)) Integrations.StarPathHitBoundary.Install();
            if (!LibraryCapabilities.Has(LibraryCapability.Lifecycle)) return;
            Integrations.HarmonyWorkflowCalls.Install();
            Integrations.HarmonyLifecycleCopies.Install();
            LifecycleEntryBoundary.Install();
            PointerAccessProtection.Install();
            ComponentIntegrity.Install();
            NativeAdmissionGuards.Install();
            DepartureBoundary.Install();
            WorldTransferBoundary.Install();
            Integrations.GravshipTransferBoundary.Install();
            LordControlBoundary.Install();
            MapRemovalBoundary.Install();
            ForcedUnspawnBoundary.Install();
            ContainerTransferBoundary.Install();
            KidnapBoundary.Install();
            RestorationBoundary.Install();
            Presentation.RenderLifecycleBoundary.Install();
            if (LibraryCapabilities.Has(LibraryCapability.Domains) || LibraryCapabilities.Has(LibraryCapability.StarBlood)) MovementBoundary.Install();
            if (LibraryCapabilities.Has(LibraryCapability.Domains))
            {
                ThingRemovalBoundary.Install();
                FieldWriteProtection.Register(AccessTools.Method(typeof(DomainConfinementBoundary), nameof(DomainConfinementBoundary.AllowsLocationWrite)));
                PawnFlightBoundary.Install();
                Integrations.MiliraFlightIntegration.Install();
            }
            if (LibraryCapabilities.Has(LibraryCapability.StarBlood))
            {
                Presentation.PawnSelectionBoundary.Install();
                StarBlood.PawnTickBoundary.Install();
            }
        }
    }
}
