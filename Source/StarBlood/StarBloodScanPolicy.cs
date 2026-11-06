namespace LihuatLib.StarBlood
{
    public static class StarBloodScanPolicy
    {
        private static bool requested;
        private static bool frozen;
        public static bool CacheOnly { get; private set; }
        internal static bool StartupCacheOnly => frozen ? CacheOnly : requested;

        public static void ConfigureForStartup(bool cacheOnly)
        {
            if (frozen) return;
            requested = cacheOnly;
        }

        internal static void Freeze()
        { if (!frozen) { CacheOnly = requested; frozen = true; } }
    }
}
