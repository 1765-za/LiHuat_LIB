using LihuatLib.Configuration;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class HighMechanismSettings
    {
        internal static HighMechanism Draw(Listing_Standard listing, HighMechanism disabled)
        {
            listing.Label("Lihuat_HighMechanismsTitle".Translate());
            Option(listing, HighMechanism.Native, ref disabled);
            Option(listing, HighMechanism.Scanning, ref disabled);
            Option(listing, HighMechanism.AstralArts, ref disabled);
            Option(listing, HighMechanism.Maintenance, ref disabled);
            if (disabled != HighMechanismPolicy.Disabled)
                listing.Label("Lihuat_HighMechanismsRestart".Translate());
            return disabled;
        }

        private static void Option(Listing_Standard listing, HighMechanism mechanism, ref HighMechanism disabled)
        {
            bool off = (disabled & mechanism) != 0;
            string key = "Lihuat_HighMechanism" + mechanism;
            listing.CheckboxLabeled(key.Translate(), ref off, (key + "Tip").Translate());
            disabled = off ? disabled | mechanism : disabled & ~mechanism;
        }
    }
}
