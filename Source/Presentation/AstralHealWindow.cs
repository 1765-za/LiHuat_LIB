using System;
using LihuatLib.AstralArts;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal sealed class AstralHealWindow : Window
    {
        private readonly AstralEntry entry;
        private int budget;
        private string buffer;
        public override Vector2 InitialSize => new Vector2(470f, 375f);

        internal AstralHealWindow(AstralEntry entry)
        {
            this.entry = entry;
            budget = Math.Max(1, Math.Min(entry.Account.HealingBudget, (int)entry.Capacity));
            buffer = budget.ToString();
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), "Lihuat_AstralHeal".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 44f, rect.width, 68f), "Lihuat_AstralHealBudgetDesc".Translate(
                entry.Account.AvailableEnergy.ToString("F1"), AstralHealing.Cost(entry).ToString("F2")));
            Widgets.Label(new Rect(0f, 122f, 270f, 30f), "Lihuat_AstralHealBudget".Translate());
            Widgets.TextFieldNumeric(new Rect(rect.width - 142f, 120f, 142f, 30f), ref budget, ref buffer, 1f, entry.Capacity);
            Widgets.Label(new Rect(0f, 164f, rect.width, 54f), "Lihuat_AstralHealEstimate".Translate(
                (Math.Min(budget, entry.Account.AvailableEnergy) / AstralHealing.Cost(entry)).ToString("F1")));
            Widgets.Label(new Rect(0f, 225f, rect.width, 60f), "Lihuat_AstralSkillHealRanges".Translate(
                AstralHealing.SelectionRadius(entry) * 2 + 1, AstralHealing.CastRadius(entry) * 2 + 1));
            if (Widgets.ButtonText(new Rect(0f, rect.height - 38f, 145f, 36f), "Cancel".Translate())) Close();
            bool wasEnabled = GUI.enabled;
            try
            {
                GUI.enabled = wasEnabled && AstralHealing.CanBegin(entry);
                if (Widgets.ButtonText(new Rect(rect.width - 190f, rect.height - 38f, 190f, 36f), "Lihuat_AstralHealSelect".Translate())
                    && LihuatGame.Current.AstralArts.SetHealingBudget(entry, budget))
                {
                    Close();
                    AstralHealTargeting.Begin(entry, budget);
                }
            }
            finally { GUI.enabled = wasEnabled; }
        }
    }
}
