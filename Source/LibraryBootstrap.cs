using Verse;
using LihuatLib.Core;

namespace LihuatLib
{
    [StaticConstructorOnStartup]
    public static class LibraryBootstrap
    {
        static LibraryBootstrap()
        {
            LibraryCapabilities.Freeze();
            StarBlood.StarBloodScanPolicy.Freeze();
            Log.Message("[LihuatLib] Capabilities: " + LibraryCapabilities.Enabled);
            if (LibraryCapabilities.Enabled == LibraryCapability.None) return;
            Core.ConsumerAssemblyRegistration.Initialize(LoadedModManager.RunningModsListForReading);
            Mechanisms.LibraryFeatureInstaller.Install();
            Patches.LibraryPatchInstaller.Install();
            Log.Message("[LihuatLib.Protection] startup native=" + Mechanisms.NativeIlGuards.NativeCount
                + " managed=" + Mechanisms.NativeIlGuards.ManagedCount
                + (Mechanisms.NativeIlGuards.ManagedCount > 0 ? " reason=" + Mechanisms.NativeIlGuards.LastFailure : ""));
            if (LibraryPreparation.Required) LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.QueueLongEvent(
                LibraryPreparation.Prepare(), StarBlood.StarBloodScanPolicy.CacheOnly
                    ? "Lihuat_LoadingMechanismCache" : "Lihuat_PreparingMechanisms", showExtraUIInfo: false));
        }
    }
}
