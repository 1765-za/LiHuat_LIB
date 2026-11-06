using LihuatLib.AstralArts;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class AstralArtDescriptions
    {
        internal static string EnhanceStatus(AstralEntry entry) => (!entry.Account.EnhanceEnabled ? "Lihuat_AstralOff"
            : !LihuatGame.Current.AstralArts.Available(entry) ? "Lihuat_AstralSuspended"
            : entry.Account.Enhanced(Find.TickManager.TicksGame) ? "Lihuat_AstralActive" : "Lihuat_AstralWaitEnergy").Translate();
        internal static string EnhanceKey(AstralEntry entry) => AstralEnhancement.Advanced(entry) ? "Lihuat_AstralEnhanceII" : "Lihuat_AstralEnhance";
        internal static string Enhance(AstralEntry entry) => (AstralEnhancement.Advanced(entry)
            ? "Lihuat_AstralSkillEnhanceIIDesc" : "Lihuat_AstralSkillEnhanceDesc").Translate(
            AstralEnhancement.Upkeep(entry).ToString("0.##"), (AstralEnhancement.PositiveFactor(entry) - 1f).ToStringPercent(),
            (1f - AstralEnhancement.IncomingFactor(entry)).ToStringPercent(), AstralEnhancement.Reduction(entry).ToStringPercent());
        internal static string RepairStatus(AstralEntry entry) => (!entry.Account.RepairEnabled ? "Lihuat_AstralOff"
            : !LihuatGame.Current.AstralArts.Available(entry) ? "Lihuat_AstralSuspended"
            : entry.Account.AvailableEnergy < entry.RepairCost * .0001f ? "Lihuat_AstralWaitEnergy" : "Lihuat_AstralAuto").Translate();
        internal static string IsolationStatus(AstralEntry entry) => (!entry.Account.IsolationEnabled ? "Lihuat_AstralOff"
            : !LihuatGame.Current.AstralArts.Available(entry) ? "Lihuat_AstralSuspended"
            : entry.Account.Powered(Find.TickManager.TicksGame) ? "Lihuat_AstralActive" : "Lihuat_AstralWaitEnergy").Translate();
        internal static string Energy(AstralEntry entry) => "Lihuat_AstralEnergy".Translate(
            entry.Account.Energy.ToString("F1"), entry.Capacity.ToString("F0"), entry.Strength.ToString("P0"))
            + "\n" + "Lihuat_AstralRecovery".Translate(entry.Recovery(false).ToString("F2"),
                entry.Recovery(true).ToString("F2"), (entry.Recovery(false) / entry.Capacity).ToString("P1"))
            + (entry.Account.AvailableEnergy > entry.Account.Energy + .001f ? "\n" + "Lihuat_AstralAvailableEnergy".Translate(entry.Account.AvailableEnergy.ToString("F1")).ToString() : "")
            + "\n\n" + "Lihuat_AstralStrengthFlowDesc".Translate();
        internal static string Repair(AstralEntry entry)
        {
            string cost = entry.RepairCost.ToString("F2");
            string description = AstralRepair.Uncapped(entry) ? "Lihuat_AstralUncappedRepairDesc".Translate(cost)
                : "Lihuat_AstralRegenerativeRepairDesc".Translate(entry.RepairLimit.ToString("F2"),
                    cost, AstralRepair.UncappedCapacity.ToString("F0"));
            return "Lihuat_AstralRepairState".Translate(RepairStatus(entry)) + "\n\n" + description;
        }
        internal static string Isolation(AstralEntry entry) => "Lihuat_AstralIsolationState".Translate(IsolationStatus(entry))
            + "\n\n" + "Lihuat_AstralSkillIsolationDesc".Translate(
                (entry.Profile.isolationUpkeep * entry.Account.Multiplier).ToString("F2"),
                AstralIsolation.MinimumCost(entry).ToString("0.###"), AstralIsolation.DamageCost(entry).ToString("0.####"),
                (AstralAccount.MaximumReduction * entry.Account.Multiplier / AstralAccount.MaximumMultiplier + entry.SkillEffects.IsolationReduction).ToString("P1"),
                AstralIsolation.MaximumCost(entry).ToString("0.##"));
    }
}
