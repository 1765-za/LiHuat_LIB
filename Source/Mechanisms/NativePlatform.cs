using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    // Platform plumbing only. This does not implement the native protection/JIT ABI.
    internal static class NativePlatform
    {
        internal static string LibraryName => OperatingSystem.IsLinux() ? "libLihuatNative.so" : "LihuatNative.dll";

        internal static void RequireSupportedProcess()
        {
            if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("Native entries require Windows or Linux x64 CoreCLR.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoVector { internal IntPtr Address; internal nuint Length; }
        [DllImport("libc", SetLastError = true)]
        private static extern unsafe nint process_vm_readv(int pid, IoVector* local, nuint localCount,
            IoVector* remote, nuint remoteCount, nuint flags);
        [DllImport("libc", SetLastError = true)]
        private static extern int mprotect(IntPtr address, nuint length, int protection);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, out IntPtr value, nuint bytes, out nuint read);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint previous);

        internal static unsafe bool TryReadPointer(IntPtr address, out IntPtr value)
        {
            value = IntPtr.Zero;
            if (address == IntPtr.Zero) return false;
            if (OperatingSystem.IsWindows())
                return ReadProcessMemory(GetCurrentProcess(), address, out value, (nuint)IntPtr.Size, out nuint read)
                    && read == (nuint)IntPtr.Size;
            if (!OperatingSystem.IsLinux()) return false;
            IntPtr copy = IntPtr.Zero;
            var local = new IoVector { Address = (IntPtr)(&copy), Length = (nuint)IntPtr.Size };
            var remote = new IoVector { Address = address, Length = (nuint)IntPtr.Size };
            // Kernel-mediated reads fail on inaccessible/unmapped memory instead of faulting the CLR.
            if (process_vm_readv(Environment.ProcessId, &local, 1, &remote, 1, 0) != IntPtr.Size) return false;
            value = copy;
            return true;
        }

        internal readonly struct SlotProtection
        {
            private readonly IntPtr address;
            private readonly nuint length;
            private readonly uint previous;
            internal SlotProtection(IntPtr address, nuint length, uint previous)
            { this.address = address; this.length = length; this.previous = previous; }
            internal bool Restore() => OperatingSystem.IsWindows()
                ? VirtualProtect(address, length, previous, out _)
                : OperatingSystem.IsLinux() && mprotect(address, length, (int)previous) == 0;
        }

        internal static SlotProtection MakeSlotWritable(IntPtr address)
        {
            RequireSupportedProcess();
            ulong start = unchecked((ulong)address.ToInt64());
            if (start == 0 || start % (uint)IntPtr.Size != 0)
                throw new ArgumentException("JIT slot must be a non-null aligned pointer.", nameof(address));
            if (OperatingSystem.IsWindows())
            {
                if (!VirtualProtect(address, (nuint)IntPtr.Size, 0x04, out uint previous))
                    throw new InvalidOperationException("Cannot make JIT slot writable; Win32=" + Marshal.GetLastPInvokeError());
                return new SlotProtection(address, (nuint)IntPtr.Size, previous);
            }
            ulong pageSize = (ulong)Environment.SystemPageSize;
            ulong page = start - start % pageSize;
            ulong pageEnd = checked(page + pageSize);
            if (checked(start + (uint)IntPtr.Size) > pageEnd)
                throw new InvalidOperationException("JIT slot spans pages.");
            foreach (string line in File.ReadLines("/proc/self/maps"))
            {
                string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2) continue;
                string[] range = fields[0].Split('-');
                if (range.Length != 2
                    || !ulong.TryParse(range[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong low)
                    || !ulong.TryParse(range[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong high)
                    || page < low || pageEnd > high) continue;
                string permissions = fields[1];
                // Only data slots are supported: never convert code pages to writable/executable pages.
                if (permissions.Length < 4 || permissions[0] != 'r' || permissions[2] != '-' || permissions[3] != 'p')
                    throw new InvalidOperationException("JIT slot is not in a readable, private, non-executable mapping.");
                int previous = 1 | (permissions[1] == 'w' ? 2 : 0);
                IntPtr pageAddress = new IntPtr(unchecked((long)page));
                if (mprotect(pageAddress, (nuint)pageSize, previous | 2) != 0)
                    throw new InvalidOperationException("Cannot make JIT slot writable; errno=" + Marshal.GetLastPInvokeError());
                return new SlotProtection(pageAddress, (nuint)pageSize, (uint)previous);
            }
            throw new InvalidOperationException("Cannot find the JIT slot mapping in /proc/self/maps.");
        }
    }
}
