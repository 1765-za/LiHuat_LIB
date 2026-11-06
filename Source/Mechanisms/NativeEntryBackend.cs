using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace LihuatLib.Mechanisms
{
    internal static class NativeEntryBackend
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Snapshot { internal uint State, Publications, Sites, Intact; internal IntPtr Current, Control; }
        private static readonly uint snapshotSize = (uint)Marshal.SizeOf<Snapshot>();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CodeCall(IntPtr precode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OverlapsCall(IntPtr address, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RegisterCall(ulong authority, IntPtr key, IntPtr precode, IntPtr previous, long expected, IntPtr control);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EntryCall(ulong authority, IntPtr key);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PublishCall(ulong authority, IntPtr key, IntPtr previous, long expected, IntPtr control);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RetainCall(ulong authority, IntPtr key, IntPtr address, long expected);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PublicationCall(ulong authority, IntPtr key, out IntPtr previous);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ObserveCall(ulong authority, IntPtr continuation);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadyCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong NotificationsCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatusCall(IntPtr key, out Snapshot status, uint size);
        private static IntPtr module;
        private static ulong authority;
        private static readonly object initialization = new object();
        private static ExceptionDispatchInfo failure;
        private static CodeCall code;
        private static OverlapsCall overlaps;
        private static RegisterCall register;
        private static EntryCall remove, flush;
        private static PublishCall publish;
        private static RetainCall retain;
        private static PublicationCall publication;
        private static NotificationsCall notifications;
        private static StatusCall status;
        private static ObserveCall observe;
        internal static bool Initialized => Volatile.Read(ref module) != IntPtr.Zero;
        internal static string Path => NativeModule.Path;

        internal static void Initialize()
        {
            if (Initialized) return;
            lock (initialization)
            {
                if (Initialized) return;
                failure?.Throw();
                InitializeSession();
            }
        }

        private static void InitializeSession()
        {
            string stage = "module preparation";
            try
            {
                NativeModule.Require();
                stage = "Harmony JIT preparation";
                Integrations.HarmonyJitSetup.Install();
                stage = "CLR suspension initialization";
                var ready = NativeModule.Bind<ReadyCall>("LihuatNative_SuspensionReady");
                if (ready() == 0) ClrProfilerAttach.Attach(Path);
                if (ready() == 0) throw new InvalidOperationException("CLR code-write suspension is unavailable.");
                stage = "native export binding";
                var nextRegister = NativeModule.Bind<RegisterCall>("LihuatNative_Register");
                var nextCode = NativeModule.Bind<CodeCall>("LihuatNative_Code");
                var nextOverlaps = NativeModule.Bind<OverlapsCall>("LihuatNative_Overlaps");
                var nextPublish = NativeModule.Bind<PublishCall>("LihuatNative_Publish");
                var nextRetain = NativeModule.Bind<RetainCall>("LihuatNative_Retain");
                var nextRemove = NativeModule.Bind<EntryCall>("LihuatNative_Remove");
                var nextStatus = NativeModule.Bind<StatusCall>("LihuatNative_Status");
                var nextPublication = NativeModule.Bind<PublicationCall>("LihuatNative_Publication");
                var nextNotifications = NativeModule.Bind<NotificationsCall>("LihuatNative_CompilationNotifications");
                var nextFlush = NativeModule.Bind<EntryCall>("LihuatNative_Flush");
                var nextObserve = NativeModule.Bind<ObserveCall>("LihuatNative_ObserveCompilations");
                stage = "JIT observation initialization";
                if (nextObserve(NativeModule.Authority, Integrations.HarmonyJitSetup.Address) != 1)
                    throw new InvalidOperationException("CoreCLR JIT compilation boundary does not match this runtime.");
                stage = "live startup verification";
                if (ready() != 1) throw new InvalidOperationException("CLR code-write suspension was lost during native startup.");
                NativeStartupProof.Verify();
                register = nextRegister; code = nextCode; overlaps = nextOverlaps;
                publish = nextPublish; retain = nextRetain; remove = nextRemove;
                status = nextStatus; publication = nextPublication; notifications = nextNotifications;
                flush = nextFlush; observe = nextObserve;
                authority = NativeModule.Authority;
                Volatile.Write(ref module, NativeModule.Handle);
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(new InvalidOperationException("Native startup failed during " + stage + ".", error));
                failure.Throw();
            }
        }

        internal static void Register(IntPtr key, IntPtr precode, IntPtr previous, long expected, IntPtr control)
        {
            int result = register(authority, key, precode, previous, expected, control);
            if (result != 1)
                throw new InvalidOperationException("Native entry registration rejected its code identity or publication: code=" + result
                    + " previous=" + previous.ToString("X") + " control=" + control.ToString("X"));
        }

        internal static void Publish(IntPtr key, IntPtr previous, long expected, IntPtr control)
        {
            if (publish(authority, key, previous, expected, control) != 1) throw new InvalidOperationException("Native entry publication lost its code identity.");
        }

        internal static void Retain(IntPtr key, IntPtr address, long expected)
        {
            int result = retain(authority, key, address, expected);
            if (result != 1)
            {
                Snapshot snapshot = Status(key);
                throw new InvalidOperationException("Retained entry rejected: result=" + result + " state=" + snapshot.State
                    + " sites=" + snapshot.Sites + " publications=" + snapshot.Publications
                    + " address=" + address.ToString("X") + " expected=" + expected.ToString("X16"));
            }
        }

        internal static Snapshot Status(IntPtr key) => Initialized
            && status(key, out Snapshot value, snapshotSize) == 1 ? value : default;

        internal static bool Remove(IntPtr key) => remove(authority, key) == 1;
        internal static IntPtr Publication(IntPtr key)
        {
            if (publication(authority, key, out IntPtr previous) != 1)
                throw new InvalidOperationException("Native publication authority was rejected.");
            return previous;
        }
        internal static ulong CompilationNotifications => notifications();
        internal static void Flush(IntPtr key)
        {
            if (Initialized && flush(authority, key) < 0)
                throw new InvalidOperationException("CoreCLR JIT slot ownership could not be restored.");
        }
        internal static void EnsureCompilationObserver()
        {
            if (observe(authority, IntPtr.Zero) != 1) throw new InvalidOperationException("CoreCLR JIT slot ownership could not be restored.");
        }
        internal static IntPtr Code(IntPtr precode) => code(precode);
        internal static bool Overlaps(IntPtr address, int length) => Initialized && length > 0
            && overlaps(address, (uint)length) != 0;
    }
}
