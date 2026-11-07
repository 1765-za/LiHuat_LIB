using LihuatLib.Presentation;
using LihuatLib.Configuration;
using UnityEngine;
using Verse;

namespace LihuatLib
{
    public sealed class LibraryMod : Mod
    {
        public static MechanismSettings Mechanics { get; private set; } = new MechanismSettings();
        public static AnalysisScanner ActiveScanner { get; private set; }
        private static LibraryMod instance;
        public LibraryMod(ModContentPack content) : base(content)
        {
            Core.LibraryCapabilities.Enable(Core.LibraryCapability.IlDispatch | Core.LibraryCapability.RuntimeIl);
            instance = this;
            Mechanics = GetSettings<MechanismSettings>();
            if (Mechanics.MilitaryMode) Mechanics.Apply(GameplayMechanic.All);
            HighMechanismPolicy.Initialize(Mechanics.DisabledHighMechanisms);
            Mechanisms.NativeEntryBackend.Initialize();
            Mechanisms.PatchIntegrity.Install();
            ActiveScanner = Mechanics.Scanner;
            Patches.AnalysisLoadingPatch.Install();
        }
        public override string SettingsCategory() => Content.Name;
        public static void OpenMechanismSettings() => Find.WindowStack.Add(new MechanismSettingsWindow(instance));
        internal void SelectMilitaryMode(bool enabled)
        {
            if (Mechanics.MilitaryMode == enabled) return;
            Mechanics.SelectMilitaryMode(enabled);
            WriteSettings();
        }
        internal void SelectScanner(AnalysisScanner scanner)
        {
            if (Mechanics.Scanner == scanner) return;
            Mechanics.SelectScanner(scanner);
            WriteSettings();
        }
        public void ApplyMechanisms(GameplayMechanic enabled)
        {
            enabled &= GameplayMechanic.All;
            if (enabled == Mechanics.Enabled) return;
            GameplayMechanic changed = enabled ^ Mechanics.Enabled;
            Mechanics.Apply(enabled);
            if ((changed & GameplayMechanic.TrueDamage) != 0)
                LihuatGame.Current?.StarPaths.RefreshMechanisms();
            Mechanics.NotifyChanged(changed);
            WriteSettings();
        }
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            try
            {
                SelectMilitaryMode(MilitaryModeSettings.Draw(listing, Mechanics.MilitaryMode));
                listing.GapLine();
                ScannerSettings.Draw(listing, this);
                listing.GapLine();
                if (listing.ButtonText("Lihuat_MechanismsTitle".Translate())) OpenMechanismSettings();
                listing.GapLine();
                listing.Label("Lihuat_CharactersDescription".Translate());
                listing.Gap();
                if (!LibraryCapabilities.Has(LibraryCapability.CharacterManagement))
                    listing.Label("Lihuat_CharactersCapabilityRequired".Translate());
                else if (Current.ProgramState != ProgramState.Playing || LihuatGame.Current?.Characters == null)
                    listing.Label("Lihuat_CharactersRequiresGame".Translate());
                else if (listing.ButtonText("Lihuat_CharactersTitle".Translate()))
                    CharacterManagerWindow.OpenFromSettings();
            }
            finally { listing.End(); }
        }
    }
}
