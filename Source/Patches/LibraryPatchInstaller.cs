using System;
using LihuatLib.Dispatching;
using LihuatLib.Core;

namespace LihuatLib.Patches
{
    internal static class LibraryPatchInstaller
    {
        internal static void Install()
        {
            Integrations.MissingPartCompatibility.Install();
            Integrations.OberoniaIncidentCompatibility.Install();
            Integrations.BackstoryAssignmentCompatibility.Install();
            Integrations.RatkinWatchCompatibility.Install();
            if (LibraryCapabilities.Has(LibraryCapability.WorldArts)) WorldArts.WorldArtBoundary.Install();
            if (LibraryPreparation.Required) Patch(typeof(PreparationProgressPatch));
            if (LibraryCapabilities.Has(LibraryCapability.IlDispatch)) FixedPawnGenerationPatch.Install();
            if (LibraryCapabilities.Has(LibraryCapability.Lifecycle))
                Patch(typeof(PawnGenerationStepPatch), typeof(DeveloperActionPatch), typeof(DeveloperToolPatch), typeof(DeveloperWindowCreationPatch),
                typeof(DeveloperWindowActionPatch), typeof(DeveloperQueuedActionPatch),
                typeof(ShouldDieEntryPatch), typeof(RuntimeComponentRemovalPatch), typeof(ColonistMoodPatch),
                typeof(EndingWindowPatch), typeof(EndingCreditsPatch), typeof(EndingGameOverPatch));
            bool domains = LibraryCapabilities.Has(LibraryCapability.Domains);
            if (domains) Integrations.QuestDomainCompatibility.Install();
            bool starBlood = LibraryCapabilities.Has(LibraryCapability.StarBlood);
            bool hediffs = LibraryCapabilities.Has(LibraryCapability.HediffProtection);
            bool astral = LibraryCapabilities.Has(LibraryCapability.AstralArts);
            bool starPaths = LibraryCapabilities.Has(LibraryCapability.StarPaths);
            bool combat = LibraryCapabilities.Has(LibraryCapability.Combat);
            if (domains || starBlood || astral || starPaths || combat) Patch(typeof(StarBloodDamagePatch));
            if (domains || starBlood)
                Patch(typeof(RegistrationRemovalPatch), typeof(ListedPawnRemovalPatch),
                    typeof(GriddedPawnRemovalPatch), typeof(TooltipArrivalPatch), typeof(TooltipDeparturePatch),
                    typeof(MapSelectionPatch));
            if (starBlood || astral || starPaths || combat) Patch(typeof(StarBloodHediffPatch));
            if (starBlood)
                Patch(typeof(LivingCorpsePatch), typeof(CorpseOccupantPatch));
            if (astral || starPaths) Patch(typeof(AstralInjuryPatch), typeof(AstralGizmosPatch));
            if (domains || astral || starPaths || combat) Patch(typeof(CombatStatPatch));
            if (domains)
                Patch(typeof(ShellCapacityPatch), typeof(DomainShellDamagePatch), typeof(DomainSpawnPatch),
                    typeof(CounterCastPatch), typeof(DomainPositionPatch), typeof(DomainTeleportPatch),
                    typeof(DomainShellOverlayPatch), typeof(DomainShellSelectionPatch), typeof(DomainShellLabelPatch),
                    typeof(DomainScalePatch), typeof(CounterBurstPatch),
                    typeof(DomainShellPlacementPatch), typeof(DomainShellVisionPatch), typeof(StageEffectPatch), typeof(ChangedHediffEffectPatch));
            if (domains) Patch(typeof(DomainBarrierProjectilePatch), typeof(DomainBarrierExplosionPatch),
                typeof(DomainBarrierLightningPatch), typeof(DomainBarrierFalloutPatch), typeof(DomainBarrierToxicPatch),
                typeof(DomainBarrierPrecipitationPatch), typeof(DomainBarrierTemperaturePatch), typeof(DomainBarrierRainPatch));
            if (hediffs) Patch(typeof(PinnedHediffClearPatch));
            if (domains || hediffs) Patch(typeof(RemovedHediffEffectPatch));
        }

        private static void Patch(params Type[] adapters)
        {
            NativeDispatcher.Install(adapters);
        }
    }
}
