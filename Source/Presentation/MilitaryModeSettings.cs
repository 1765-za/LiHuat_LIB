using LihuatLib.Configuration;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class MilitaryModeSettings
    {
        internal static bool Draw(Listing_Standard listing, bool selected)
        {
            listing.CheckboxLabeled("Lihuat_MilitaryMode".Translate(), ref selected, "Lihuat_MilitaryModeTip".Translate());
            bool active = HighMechanismPolicy.Disabled == HighMechanism.None;
            listing.Label((active ? "Lihuat_MilitaryModeActive" : "Lihuat_StandardModeActive").Translate());
            if (selected != active) listing.Label("Lihuat_MilitaryModeRestart".Translate());
            return selected;
        }
    }
}
