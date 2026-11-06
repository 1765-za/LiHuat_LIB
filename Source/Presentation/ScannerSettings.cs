using LihuatLib.Configuration;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class ScannerSettings
    {
        internal static void Draw(Listing_Standard listing, LibraryMod owner)
        {
            listing.Label("Lihuat_ScannerTitle".Translate());
            AnalysisScanner selected = LibraryMod.Mechanics.Scanner;
            if (listing.RadioButton("Lihuat_ScannerNormal".Translate(), selected == AnalysisScanner.Normal))
                owner.SelectScanner(AnalysisScanner.Normal);
            if (listing.RadioButton("Lihuat_ScannerLab".Translate(), selected == AnalysisScanner.Lab))
                owner.SelectScanner(AnalysisScanner.Lab);
            listing.Label(("Lihuat_Scanner" + LibraryMod.Mechanics.Scanner + "Description").Translate());
            if (LibraryMod.Mechanics.Scanner != LibraryMod.ActiveScanner)
                listing.Label("Lihuat_ScannerRestart".Translate());
        }
    }
}
