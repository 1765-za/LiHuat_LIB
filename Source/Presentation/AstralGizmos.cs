using System.Collections.Generic;
using LihuatLib.AstralArts;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    [StaticConstructorOnStartup]
    internal static class AstralGizmos
    {
        private static readonly Texture2D ArtsIcon = ContentFinder<Texture2D>.Get("Lihuat/AstralArts/Arts");
        private static readonly Texture2D RepairIcon = ContentFinder<Texture2D>.Get("Lihuat/AstralArts/Repair");
        private static readonly Texture2D IsolationIcon = ContentFinder<Texture2D>.Get("Lihuat/AstralArts/Isolation");

        internal static IEnumerable<Gizmo> Add(Pawn pawn, IEnumerable<Gizmo> original)
        {
            if (original != null) foreach (Gizmo gizmo in original) yield return gizmo;
            if (pawn.Dead || pawn.Faction != Faction.OfPlayer) yield break;
            AstralArtsService service = LihuatGame.Current?.AstralArts;
            AstralEntry entry = service?.Find(pawn);
            if (entry == null || entry.Arts == AstralArt.None) yield break;
            AstralGizmoState state = Current.Game.GetComponent<AstralGizmoState>();
            bool expanded = state.IsExpanded(entry);
            yield return new Command_Action
            {
                defaultLabel = "Lihuat_AstralTitle".Translate(), icon = ArtsIcon, Order = -98f, groupable = false,
                defaultDesc = "Lihuat_AstralSkillOpen".Translate(entry.Skills.AvailablePoints) + "\n\n" + AstralArtDescriptions.Energy(entry),
                action = () => Find.WindowStack.Add(new StarPathWindow(entry))
            };
            yield return new Command_Action {
                defaultLabel = "Lihuat_AstralQuickbar".Translate(), icon = ArtsIcon, Order = -97.95f, groupable = false,
                defaultDesc = (expanded ? "Lihuat_AstralCollapse" : "Lihuat_AstralExpand").Translate(), action = () => state.Toggle(entry)
            };
            foreach (Gizmo spell in AstralSpellGizmos.Add(service, entry)) yield return spell;
            if (!expanded) yield break;
            yield return new AstralEnergyGizmo(entry);
            bool available = service.Available(entry);
            if (entry.Allows(AstralArt.Repair))
            {
                var repair = new Command_Toggle
                {
                    defaultLabel = "Lihuat_AstralRepair".Translate(), icon = RepairIcon, Order = -97.8f, groupable = false,
                    defaultDesc = AstralArtDescriptions.Repair(entry), isActive = () => entry.Account.RepairEnabled,
                    toggleAction = () => service.SetRepair(entry, !entry.Account.RepairEnabled)
                };
                if (!available && !entry.Account.RepairEnabled) repair.Disable("Lihuat_AstralSuspended".Translate());
                yield return repair;
            }
            if (entry.Allows(AstralArt.Heal))
            {
                var heal = new Command_Action
                {
                    defaultLabel = "Lihuat_AstralHeal".Translate(), icon = RepairIcon, Order = -97.75f, groupable = false,
                    defaultDesc = "Lihuat_AstralHealDesc".Translate(AstralHealing.Cost(entry).ToString("F2")),
                    action = () => Find.WindowStack.Add(new AstralHealWindow(entry))
                };
                if (!AstralHealing.CanBegin(entry)) heal.Disable("Lihuat_AstralHealUnavailable".Translate());
                yield return heal;
            }
            if (entry.Allows(AstralArt.Enhance))
            {
                var enhance = new Command_Toggle
                {
                    defaultLabel = AstralArtDescriptions.EnhanceKey(entry).Translate(), icon = IsolationIcon, Order = -97.72f, groupable = false,
                    defaultDesc = AstralArtDescriptions.Enhance(entry), isActive = () => entry.Account.EnhanceEnabled,
                    toggleAction = () => service.SetEnhance(entry, !entry.Account.EnhanceEnabled)
                };
                if (!entry.Account.EnhanceEnabled)
                {
                    if (!available) enhance.Disable("Lihuat_AstralSuspended".Translate());
                    else if (entry.Account.AvailableEnergy < entry.Account.EnhancementCost(entry, Find.TickManager.TicksGame))
                        enhance.Disable("Lihuat_AstralWaitEnergy".Translate());
                }
                yield return enhance;
            }
            if (!entry.Allows(AstralArt.Isolation)) yield break;
            var isolation = new Command_Toggle
            {
                defaultLabel = "Lihuat_AstralIsolation".Translate(), icon = IsolationIcon, Order = -97.7f, groupable = false,
                defaultDesc = AstralArtDescriptions.Isolation(entry), isActive = () => entry.Account.IsolationEnabled,
                toggleAction = () => service.SetIsolation(entry, !entry.Account.IsolationEnabled)
            };
            if (!available && !entry.Account.IsolationEnabled) isolation.Disable("Lihuat_AstralSuspended".Translate());
            yield return isolation;
            var multiplier = new Command_Action
            {
                defaultLabel = "Lihuat_AstralMultiplier".Translate(entry.Account.Multiplier), icon = IsolationIcon,
                Order = -97.6f, groupable = false, defaultDesc = "Lihuat_AstralCycleMultiplier".Translate()
                    + "\n\n" + AstralArtDescriptions.Isolation(entry),
                action = () => service.SetMultiplier(entry, entry.Account.Multiplier % AstralAccount.MaximumMultiplier + 1)
            };
            if (!available) multiplier.Disable("Lihuat_AstralSuspended".Translate());
            yield return multiplier;
        }
    }
}
