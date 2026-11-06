using Verse;

namespace LihuatLib.AstralArts
{
    public sealed partial class AstralArtsService
    {
        public bool EnhancementActive(Pawn pawn) => ActiveEnhancement(pawn) != null;

        internal AstralEntry ActiveEnhancement(Pawn pawn)
        {
            AstralEntry entry = Find(pawn);
            return entry?.Account.Enhanced(Verse.Find.TickManager.TicksGame) == true
                && entry.Allows(AstralArt.Enhance) && Available(entry) ? entry : null;
        }

        public float EnhancementIncomingFactor(Pawn pawn) => ActiveEnhancement(pawn) is AstralEntry entry ? AstralEnhancement.IncomingFactor(entry) : 1f;
        public float EnhancementPositiveFactor(Pawn pawn) => ActiveEnhancement(pawn) is AstralEntry entry ? AstralEnhancement.PositiveFactor(entry) : 1f;
        public float EnhancementReduction(Pawn pawn) => ActiveEnhancement(pawn) is AstralEntry entry ? AstralEnhancement.Reduction(entry) : 0f;

        public bool SetEnhance(AstralEntry entry, bool enabled)
        {
            if (entry == null || Find(entry.Pawn) != entry || enabled && (!entry.Allows(AstralArt.Enhance) || !Available(entry))) return false;
            entry.Account.SetEnhance(enabled, entry, Verse.Find.TickManager.TicksGame);
            RefreshEnhancement(entry);
            return entry.Account.EnhanceEnabled == enabled;
        }

        public void UseProviderEnhancementReduction(AstralEntry entry)
        {
            if (entry != null && Find(entry.Pawn) == entry) entry.ProviderSettlesEnhancement = true;
        }

        private void RefreshEnhancement(AstralEntry entry)
        {
            int tier = EnhancementActive(entry.Pawn) ? AstralEnhancement.Advanced(entry) ? 2 : 1 : 0;
            if (tier == entry.EnhancementStatTier) return;
            entry.EnhancementStatTier = tier;
            AstralEnhancementStats.Invalidate(entry.Pawn);
        }
    }
}
