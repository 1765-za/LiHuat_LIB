using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class ExecutableWriteProtection
    {
        private static readonly SortedSet<long> entries = new SortedSet<long>();
        private static long[] published = Array.Empty<long>();
        [ThreadStatic] private static int writing;
        internal static long Rejected { get; private set; }

        internal readonly struct Scope : IDisposable
        {
            internal Scope(bool enter) { if (enter) writing++; }
            public void Dispose() { writing--; }
        }

        internal static void Install()
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            NativeIlGuards.Guard(
                AccessTools.Method(typeof(Marshal), nameof(Marshal.Copy), new[] { typeof(byte[]), typeof(int), typeof(IntPtr), typeof(int) }),
                AccessTools.Method(typeof(ExecutableWriteProtection), nameof(RequireWrite)), 2, 3);
            Integrations.HarmonyNativeDetourBoundary.Install();
        }

        internal static void Track(IntPtr address)
        {
            if (address == IntPtr.Zero) return;
            lock (entries)
            {
                if (!entries.Add(address.ToInt64())) return;
                var copy = new long[entries.Count]; entries.CopyTo(copy);
                Volatile.Write(ref published, copy);
            }
        }

        internal static void RequireWrite(IntPtr destination, int length)
        {
            if (writing != 0 || length <= 0 || destination == IntPtr.Zero) return;
            long start = destination.ToInt64();
            if (start < 0 || start > long.MaxValue - length) return;
            long end = start + length;
            long[] current = Volatile.Read(ref published);
            int low = 0, high = current.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (current[middle] < end) low = middle + 1;
                else high = middle;
            }
            if ((low == 0 || current[low - 1] + 16 <= start) && !NativeEntryBackend.Overlaps(destination, length)) return;
            Rejected++;
            throw new UnauthorizedAccessException("Cannot overwrite a protected executable entry at 0x" + start.ToString("X") + ".");
        }
    }
}
