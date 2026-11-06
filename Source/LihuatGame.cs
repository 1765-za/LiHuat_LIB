using System.Collections.Generic;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib
{
    public sealed partial class LihuatGame : GameComponent
    {
        internal readonly StarBloodRoster Subjects = new StarBloodRoster();
        public readonly LifecycleChannel Lifecycle = new LifecycleChannel();
        public readonly Endings.GameEndingChannel Endings = new();
        public readonly Core.RegenerationFlow Regeneration;
        public readonly Core.PawnPresenceRegistry Presence;
        public readonly Core.RetirementSubjects RetiredSubjects = new();
        public readonly Core.StatLimitChannel StatLimits = new();
        public readonly Core.DamagePreparationChannel DamagePreparation = new();
        public readonly AstralArts.AstralArtsService AstralArts = new AstralArts.AstralArtsService();
        public readonly StarPaths.StarPathService StarPaths = new StarPaths.StarPathService();
        public readonly WorldArts.WorldArtService WorldArts = new();
        internal readonly Api.ExtensionHost Extensions;
        private Characters.CharacterWorldStore characterStore;
        public Characters.CharacterManager Characters => characterStore?.Manager;
        internal void BindCharacters(Characters.CharacterWorldStore storage) => characterStore = storage;
        public LihuatGame(Game game)
        {
            Extensions = new Api.ExtensionHost(game, this);
            Regeneration = new Core.RegenerationFlow(Lifecycle);
            Presence = new Core.PawnPresenceRegistry(Lifecycle);
            RetainedGameComponents.Register(game, this);
            if (LibraryCapabilities.Has(LibraryCapability.Lifecycle)) Lifecycle.Register(new LifecycleHandler
            { Id = "lihuat.lib.presence", Order = 5, Registered = Presence.Registered, Completed = Presence.Completed });
            if (LibraryCapabilities.Has(LibraryCapability.StarBlood)) Lifecycle.Register(new LifecycleHandler
            {
                Id = "lihuat.lib.continuity", Order = 0, BlocksComponentRemoval = PawnContinuity.Protects,
                AllowsLord = PawnLordControl.Allows, BeforeWorldDeparture = PawnLordControl.BeforeWorldDeparture
            });
            if (LibraryCapabilities.Has(LibraryCapability.Regeneration)) Lifecycle.Register(new LifecycleHandler { Id = "lihuat.lib.regeneration", Order = 10,
                AllowsRegistration = Regeneration.Admit, Generated = Regeneration.Created, Registered = Regeneration.Registered });
            if (LibraryCapabilities.Has(LibraryCapability.Domains)) Lifecycle.Register(new LifecycleHandler {
                Id = "lihuat.lib.domain", Order = 20, Block = Domains.BlocksDeparture,
                BlockThing = global::LihuatLib.Domains.DomainShellDepartureRules.Blocks,
                BlocksGeneratedMutation = global::LihuatLib.Domains.DomainShellDepartureRules.BlocksGeneratedMutation,
                BlocksMapRemoval = Domains.BlocksMapRemoval,
                Preparing = Domains.Barriers.ObserveDeparture });
        }
        public static LihuatGame Current => RetainedGameComponents.Find<LihuatGame>(Verse.Current.Game);
        public override void GameComponentUpdate()
        {
            Integrations.MonoModAssemblyIntegration.Pump();
            Mechanisms.NativeRuntime.FlushCompiled();
            if (LibraryCapabilities.NeedsAnalysis) Mechanisms.LifeAnalysisCoordinator.Shared?.Pump();
            if (LibraryCapabilities.Has(LibraryCapability.HediffProtection)) Mechanisms.HediffMutationGuard.Update();
            if (LibraryCapabilities.Has(LibraryCapability.Regeneration)) Mechanisms.RegenerationGuard.Update();
            if (LibraryCapabilities.Has(LibraryCapability.StarBlood))
            { Mechanisms.LifeAvailabilityGuard.Update(); Subjects.Update(); }
            if (LibraryCapabilities.Has(LibraryCapability.Domains)) UpdateDomains();
        }
        public override void GameComponentTick()
        {
            if (LibraryCapabilities.Has(LibraryCapability.Domains)) TickDomains();
            if (LibraryCapabilities.Has(LibraryCapability.AstralArts)) AstralArts.Tick(Find.TickManager.TicksGame);
            if (LibraryCapabilities.Has(LibraryCapability.WorldArts)) WorldArts.Tick(Find.TickManager.TicksGame);
        }
        public override void FinalizeInit()
        {
            Presence.Initialize();
            Mechanisms.NativeRuntime.FlushCompiled();
            characterStore = Find.World.GetComponent<Characters.CharacterWorldStore>();
            if (LibraryCapabilities.Has(LibraryCapability.Domains)) InitializeDomains();
            Extensions.Schedule();
        }
        public override void ExposeData() { ExposeDomains(); Regeneration.ExposeData(); AstralArts.ExposeData(); StarPaths.ExposeData(); WorldArts.ExposeData(); }
        partial void InitializeDomains();
        partial void TickDomains();
        partial void UpdateDomains();
        partial void ExposeDomains();
    }
}
