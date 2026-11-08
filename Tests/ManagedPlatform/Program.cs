using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using LihuatLib.Mechanisms;

// Dependency-free plumbing tests, not a substitute for the ABI-11 backend integration suite.
internal static class Program
{
    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr address, nuint length, int protection, int flags, int fd, nint offset);
    [DllImport("libc", SetLastError = true)]
    private static extern int mprotect(IntPtr address, nuint length, int protection);
    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr address, nuint length);

    private static bool restrictedReads;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Main()
    {
        NativePlatform.RequireSupportedProcess();
        if (OperatingSystem.IsLinux()) DiagnosticTransportTests.Run().GetAwaiter().GetResult();
        Check(!NativePlatform.TryReadPointer(IntPtr.Zero, out _), "Null pointer must fail.");
        IntPtr memory = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(memory, new IntPtr(12345));
            CheckRead(memory, new IntPtr(12345));
        }
        finally { Marshal.FreeHGlobal(memory); }
        if (OperatingSystem.IsLinux()) TestLinux();
        Console.WriteLine(restrictedReads
            ? "Managed platform smoke tests completed with explicit EPERM read skips; positive kernel reads are NOT validated."
            : "Managed platform smoke tests passed (not native backend integration).");
    }
    private static void CheckRead(IntPtr address, IntPtr expected)
    {
        bool read = NativePlatform.TryReadPointer(address, out IntPtr value);
        int error = Marshal.GetLastPInvokeError();
        if (!read && OperatingSystem.IsLinux() && error == 1
            && Environment.GetEnvironmentVariable("LIHUAT_ALLOW_RESTRICTED_TESTS") == "1")
        {
            restrictedReads = true;
            Console.WriteLine("SKIP positive process_vm_readv assertion: EPERM in explicitly allowed restricted test environment.");
            return;
        }
        Check(read && value == expected, "Self read failed; errno=" + error);
    }
    private static string MappingPermissions(IntPtr address)
    {
        ulong value = unchecked((ulong)address.ToInt64());
        foreach (string line in File.ReadLines("/proc/self/maps"))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] range = fields[0].Split('-');
            ulong low = ulong.Parse(range[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ulong high = ulong.Parse(range[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (value >= low && value < high) return fields[1];
        }
        throw new InvalidOperationException("Test mapping disappeared.");
    }
    private static void TestLinux()
    {
        Check(NativePlatform.LibraryName == "libLihuatNative.so", "Wrong ELF filename.");
        string endpoint = ClrProfilerAttach.LinuxDiagnosticEndpoint();
        Check(Path.IsPathFullyQualified(endpoint)
            && Path.GetFileName(endpoint).StartsWith("dotnet-diagnostic-" + Environment.ProcessId + "-", StringComparison.Ordinal)
            && endpoint.EndsWith("-socket", StringComparison.Ordinal), "Wrong diagnostic endpoint.");
        string oldTemp = Environment.GetEnvironmentVariable("TMPDIR");
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", "relative");
            bool rejected = false;
            try { ClrProfilerAttach.LinuxDiagnosticEndpoint(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Relative diagnostic path must fail.");
        }
        finally { Environment.SetEnvironmentVariable("TMPDIR", oldTemp); }
        nuint size = (nuint)Environment.SystemPageSize;
        IntPtr page = mmap(IntPtr.Zero, size, 3, 0x22, -1, 0); // private anonymous RW
        Check(page != new IntPtr(-1), "mmap failed.");
        try
        {
            Marshal.WriteIntPtr(page, new IntPtr(42));
            Check(mprotect(page, size, 1) == 0, "Read-only setup failed.");
            var original = NativePlatform.MakeSlotWritable(page);
            Marshal.WriteIntPtr(page, new IntPtr(43));
            Check(original.Restore(), "Restore failed.");
            Check(MappingPermissions(page) == "r--p", "Read-only permissions were not restored.");
            CheckRead(page, new IntPtr(43));
            // A second writable/restore cycle must preserve the read-only map, too.
            var again = NativePlatform.MakeSlotWritable(page);
            Check(again.Restore(), "Second restore failed.");
            Check(MappingPermissions(page) == "r--p", "Second restore changed permissions.");
            Check(mprotect(page, size, 0) == 0, "No-access setup failed.");
            Check(!NativePlatform.TryReadPointer(page, out _), "No-access read must fail safely.");
            bool rejected = false;
            try { NativePlatform.MakeSlotWritable(page); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "No-access mapping must not become writable.");
            Check(mprotect(page, size, 5) == 0, "Executable setup failed.");
            rejected = false;
            try { NativePlatform.MakeSlotWritable(page); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Executable mapping must not become writable.");
        }
        finally { Check(munmap(page, size) == 0, "munmap failed."); }
        Check(!NativePlatform.TryReadPointer(page, out _), "Unmapped read must fail safely.");
    }
}

namespace LihuatLib.Mechanisms
{
    // The transport source references Bind, but these tests never attach a profiler.
    internal static class NativeModule
    {
        internal static T Bind<T>(string name) where T : Delegate => throw new NotSupportedException("No native backend in plumbing tests.");
    }
}
