using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal static class NativeModule
    {
        internal static readonly string Path = System.IO.Path.Combine(
            Directory.GetParent(System.IO.Path.GetDirectoryName(typeof(NativeModule).Assembly.Location)).FullName,
            "Native", NativePlatform.LibraryName);
        internal static IntPtr Handle { get; private set; }
        internal static ulong Authority { get; private set; }
        private static ExceptionDispatchInfo failure;

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern bool FreeLibrary(IntPtr module);

        // Only image loading and native-only initialization run while the CLR holds the module initialization lock.
        [ModuleInitializer]
        [SuppressMessage("Usage", "CA2255", Justification = "The native image must be pinned before the library's mod or protection code executes.")]
        internal static unsafe void Preload()
        {
            if (Handle != IntPtr.Zero || failure != null) return;
            IntPtr loaded = IntPtr.Zero;
            string stage = "image loading";
            try
            {
                NativePlatform.RequireSupportedProcess();
                if (OperatingSystem.IsWindows())
                {
                    // Preserve the original Win32 dependency-search policy without CLR loader fallbacks.
                    loaded = LoadLibraryExW(Path, IntPtr.Zero, 0x00000100 | 0x00000800);
                    if (loaded == IntPtr.Zero)
                        throw new DllNotFoundException("Cannot load " + Path + "; Win32=" + Marshal.GetLastPInvokeError());
                }
                else
                {
                    // Linux requires the real ABI-11 backend, not the standalone platform test library.
                    loaded = NativeLibrary.Load(Path);
                }
                stage = "ABI initialization";
                var initialize = (delegate* unmanaged[Cdecl]<uint, int>)Export(loaded, "LihuatNative_Initialize");
                if (initialize(11) != 1) throw new InvalidOperationException("Native entry runtime or ABI does not match.");
                stage = "authority acquisition";
                ulong authority = 0;
                var claim = (delegate* unmanaged[Cdecl]<ulong*, int>)Export(loaded, "LihuatNative_Claim");
                if (claim(&authority) != 1 || authority == 0) throw new InvalidOperationException("Native mutation authority is already owned.");
                Authority = authority;
                Handle = loaded;
            }
            catch (Exception error)
            {
                if (loaded != IntPtr.Zero)
                {
                    if (OperatingSystem.IsWindows()) FreeLibrary(loaded);
                    else NativeLibrary.Free(loaded);
                }
                failure = ExceptionDispatchInfo.Capture(new InvalidOperationException("Native startup failed during " + stage + ".", error));
            }
        }

        internal static void Require()
        {
            failure?.Throw();
            if (Handle == IntPtr.Zero) throw new InvalidOperationException("Native module preparation did not complete.");
        }

        internal static T Bind<T>(string name) where T : Delegate
        {
            Require();
            return Marshal.GetDelegateForFunctionPointer<T>(Export(Handle, name));
        }

        internal static IntPtr Export(IntPtr module, string name)
        {
            if (!OperatingSystem.IsWindows()) return NativeLibrary.GetExport(module, name);
            IntPtr entry = GetProcAddress(module, name);
            if (entry == IntPtr.Zero) throw new EntryPointNotFoundException(name + "; Win32=" + Marshal.GetLastPInvokeError());
            return entry;
        }
    }
}
