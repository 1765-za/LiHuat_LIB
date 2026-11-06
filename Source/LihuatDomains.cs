using LihuatLib.Domains;
using LihuatLib.Presentation;
using Verse;

namespace LihuatLib
{
    public sealed partial class LihuatGame
    {
        public readonly HediffEffectRules Effects = new HediffEffectRules();
        public readonly DomainCoordinator Domains = new DomainCoordinator();
        public readonly DomainCounterService Counters = new DomainCounterService();
        private DomainAudioController domainAudio;
        partial void InitializeDomains()
        {
            Domains.PressureChanged += Effects.Refresh;
            Domains.Initialize();
            if (domainAudio == null) domainAudio = new DomainAudioController(Verse.Current.Game);
        }
        partial void TickDomains() { Counters.Tick(); Domains.Tick(Find.TickManager.TicksGame); }
        partial void UpdateDomains()
        {
            Domains.Update();
            domainAudio?.Update(Domains.HasAreas);
        }
        partial void ExposeDomains() { Domains.ExposeData(); Counters.ExposeData(); }
    }
}
