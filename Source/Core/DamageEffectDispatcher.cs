using Verse;

namespace LihuatLib.Core
{
    internal static class DamageEffectDispatcher
    {
        internal static StarPaths.StarPathAttackScope Begin(Thing target, DamageInfo info) =>
            LibraryCapabilities.Has(LibraryCapability.StarPaths) ? StarPaths.StarPathCombat.Begin(target, info) : null;
        internal static DamageWorker.DamageResult Apply(DamageWorker worker, DamageInfo info, Thing target) =>
            LibraryCapabilities.Has(LibraryCapability.StarPaths) ? StarPaths.StarPathCombat.Apply(worker, info, target) : Ordinary(worker, info, target);
        internal static DamageWorker.DamageResult Ordinary(DamageWorker worker, DamageInfo info, Thing target) =>
            LibraryCapabilities.Has(LibraryCapability.AstralArts) ? AstralArts.AstralSpellDefense.Apply(worker, info, target) : worker.Apply(info, target);
    }
}
