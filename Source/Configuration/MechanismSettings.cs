using Verse;
using System;

namespace LihuatLib.Configuration
{
    public sealed class MechanismSettings : ModSettings
    {
        private GameplayMechanic disabled;
        private AnalysisScanner scanner;
        public AnalysisScanner Scanner => scanner;
        internal void SelectScanner(AnalysisScanner value)
        {
            if (value != AnalysisScanner.Normal && value != AnalysisScanner.Lab)
                throw new ArgumentOutOfRangeException(nameof(value));
            scanner = value;
        }
        public event Action<GameplayMechanic> Changed;
        public GameplayMechanic Enabled => GameplayMechanic.All & ~disabled;
        public bool Allows(GameplayMechanic mechanic) => (Enabled & mechanic) == mechanic;
        internal void Apply(GameplayMechanic value) => disabled = GameplayMechanic.All & ~value;
        internal void NotifyChanged(GameplayMechanic changed) => Changed?.Invoke(changed);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref disabled, "disabledMechanics", GameplayMechanic.None);
            Scribe_Values.Look(ref scanner, "analysisScanner", AnalysisScanner.Normal);
        }
    }
}
