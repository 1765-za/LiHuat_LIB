using LihuatLib.Configuration;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal sealed class MechanismSettingsWindow : Window
    {
        private readonly LibraryMod owner;
        private GameplayMechanic draft;
        private bool militaryDraft;
        public override Vector2 InitialSize => new Vector2(620f, Mathf.Min(650f, UI.screenHeight - 60f));

        internal MechanismSettingsWindow(LibraryMod owner)
        {
            this.owner = owner;
            draft = LibraryMod.Mechanics.Enabled;
            militaryDraft = LibraryMod.Mechanics.MilitaryMode;
            forcePause = true;
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width - 30f, 35f), "Lihuat_MechanismsTitle".Translate());
            Text.Font = GameFont.Small;
            var listing = new Listing_Standard();
            listing.Begin(new Rect(0f, 45f, rect.width, rect.height - 95f));
            try
            {
                listing.Label("Lihuat_MechanismsDescription".Translate());
                listing.GapLine();
                Option(listing, GameplayMechanic.GuaranteedHit);
                Option(listing, GameplayMechanic.TrueDamage);
                Option(listing, GameplayMechanic.PercentageDamage);
                Option(listing, GameplayMechanic.BlessedErasure);
                listing.GapLine();
                Option(listing, GameplayMechanic.StarShield);
                Option(listing, GameplayMechanic.ParryDeflection);
                Option(listing, GameplayMechanic.InstantMovement);
                listing.GapLine();
                militaryDraft = MilitaryModeSettings.Draw(listing, militaryDraft);
            }
            finally { listing.End(); }
            if (Widgets.ButtonText(new Rect(0f, rect.height - 35f, 150f, 35f), "Lihuat_MechanismsDefaults".Translate()))
            { draft = GameplayMechanic.All; militaryDraft = false; }
            if (Widgets.ButtonText(new Rect(rect.width - 180f, rect.height - 35f, 180f, 35f), "Lihuat_MechanismsApply".Translate()))
            {
                owner.ApplyMechanisms(draft);
                owner.SelectMilitaryMode(militaryDraft);
                Close();
            }
        }

        private void Option(Listing_Standard listing, GameplayMechanic mechanic)
        {
            bool enabled = (draft & mechanic) != 0;
            string key = "Lihuat_Mechanism" + mechanic;
            listing.CheckboxLabeled(key.Translate(), ref enabled, (key + "Tip").Translate());
            draft = enabled ? draft | mechanic : draft & ~mechanic;
        }
    }
}
