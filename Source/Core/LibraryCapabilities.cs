using System;
using System.Threading;

namespace LihuatLib.Core
{
    [Flags]
    public enum LibraryCapability
    {
        None = 0,
        Lifecycle = 1,
        Domains = 2,
        StarBlood = 4,
        Regeneration = 8,
        Erasure = 16,
        HediffProtection = 32,
        IlDispatch = 64,
        RuntimeIl = 128,
        Profiling = 256,
        CharacterManagement = 512,
        AstralArts = 1024,
        StarPaths = 2048,
        Combat = 4096,
        WorldArts = 8192
    }

    public static class LibraryCapabilities
    {
        private const LibraryCapability known = LibraryCapability.Lifecycle | LibraryCapability.Domains | LibraryCapability.StarBlood
            | LibraryCapability.Regeneration | LibraryCapability.Erasure | LibraryCapability.HediffProtection
            | LibraryCapability.IlDispatch | LibraryCapability.RuntimeIl | LibraryCapability.Profiling | LibraryCapability.CharacterManagement | LibraryCapability.AstralArts | LibraryCapability.StarPaths | LibraryCapability.Combat | LibraryCapability.WorldArts;
        private static readonly object sync = new object();
        private static volatile LibraryCapability enabled;
        private static bool frozen;
        private static int publicationThread;
        public static LibraryCapability Enabled => enabled;
        internal static bool Frozen => frozen;
        public static bool Has(LibraryCapability capability) => (enabled & capability) == capability;
        internal static bool NeedsAnalysis => Configuration.HighMechanismPolicy.Scanning && (enabled & (LibraryCapability.StarBlood | LibraryCapability.Regeneration | LibraryCapability.Erasure)) != 0;

        internal static LibraryCapability Expand(LibraryCapability capabilities)
        {
            if ((capabilities & ~known) != 0) throw new ArgumentOutOfRangeException(nameof(capabilities));
            if ((capabilities & (LibraryCapability.Domains | LibraryCapability.StarBlood | LibraryCapability.Regeneration
                | LibraryCapability.Erasure | LibraryCapability.HediffProtection | LibraryCapability.CharacterManagement)) != 0) capabilities |= LibraryCapability.Lifecycle;
            if ((capabilities & (LibraryCapability.Lifecycle | LibraryCapability.StarPaths)) != 0) capabilities |= LibraryCapability.IlDispatch | LibraryCapability.RuntimeIl;
            return capabilities;
        }

        public static void Enable(LibraryCapability capabilities)
        {
            capabilities = Expand(capabilities);
            if (capabilities == LibraryCapability.None) return;
            lock (sync)
            {
                if (frozen && !Has(capabilities))
                    throw new InvalidOperationException("Declare library capabilities in the consuming Mod constructor, before startup initialization.");
                enabled |= capabilities;
                if (publicationThread == 0) publicationThread = Thread.CurrentThread.ManagedThreadId;
            }
        }

        public static void Require(LibraryCapability capability)
        {
            if (!Has(capability)) throw new InvalidOperationException("Library capability is not enabled: " + capability
                + ". Call LibraryCapabilities.Enable in the consuming Mod constructor.");
        }

        internal static void RequirePublicationThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != publicationThread)
                throw new InvalidOperationException("IL publication requires the library startup thread.");
        }
        internal static void Freeze() { lock (sync) { publicationThread = Thread.CurrentThread.ManagedThreadId; frozen = true; } }
    }
}
