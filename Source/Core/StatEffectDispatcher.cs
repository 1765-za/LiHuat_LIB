using RimWorld;

namespace LihuatLib.Core
{
    internal static class StatEffectDispatcher
    {
        internal static void Apply(StatDef stat, StatRequest request, ref float value)
        {
            if (LibraryCapabilities.Has(LibraryCapability.AstralArts)) AstralArts.AstralEnhancementStats.Apply(stat, request, ref value);
            if (LibraryCapabilities.Has(LibraryCapability.AstralArts)) AstralArts.AstralBuffStats.Apply(stat, request, ref value);
            if (LibraryCapabilities.Has(LibraryCapability.StarPaths)) StarPaths.StarPathStats.Apply(stat, request, ref value);
            if (LibraryCapabilities.Has(LibraryCapability.Domains)) Domains.DomainStatRules.Apply(stat, request, ref value);
            LihuatGame.Current?.StatLimits.Apply(stat, request.Thing as Verse.Pawn, ref value);
        }
    }
}
