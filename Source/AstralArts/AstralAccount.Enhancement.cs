namespace LihuatLib.AstralArts
{
    public sealed partial class AstralAccount
    {
        private bool enhanceEnabled;
        private int enhancedUntil;
        public bool EnhanceEnabled => enhanceEnabled;
        public bool Enhanced(int now) => enhanceEnabled && now < enhancedUntil;

        public float EnhancementCost(AstralEntry entry, int now) => now < enhancedUntil ? 0f
            : AstralEnhancement.Upkeep(entry) * (Interval - now % Interval) / Interval;

        internal void SetEnhance(bool enabled, AstralEntry entry, int now)
        {
            enhanceEnabled = enabled;
            if (enabled && now >= enhancedUntil) PayEnhancement(entry, now);
        }

        private void AdvanceEnhancement(AstralEntry entry, int now, bool permitted)
        {
            if (enhanceEnabled && permitted && now >= enhancedUntil) PayEnhancement(entry, now);
        }

        private void PayEnhancement(AstralEntry entry, int now)
        {
            if (TrySpend(EnhancementCost(entry, now))) enhancedUntil = now - now % Interval + Interval;
            else enhanceEnabled = false;
        }
    }
}
