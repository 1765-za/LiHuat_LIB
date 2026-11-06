using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    internal static class GenericBodyPublication
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Snapshot { internal uint Publications, Compilations, Rejected, Registered; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetBody(ulong authority, IntPtr owner, IntPtr method, byte[] code, uint size, byte[] previous, uint previousSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseBody(ulong authority, IntPtr method);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadStatus(IntPtr method, out Snapshot result, uint size);
        private static SetBody set;
        private static ReleaseBody release;
        private static ReadStatus read;
        private static readonly ConcurrentDictionary<MethodInfo, MethodBody> sources = new();

        private static void Initialize()
        {
            if (set != null) return;
            if (!NativeRuntime.TryInitialize()) throw new InvalidOperationException(NativeRuntime.Failure);
            release = NativeModule.Bind<ReleaseBody>("LihuatNative_GenericRelease");
            read = NativeModule.Bind<ReadStatus>("LihuatNative_GenericStatus");
            set = NativeModule.Bind<SetBody>("LihuatNative_GenericBody");
        }

        internal static MethodBody Source(MethodInfo method) => sources.TryGetValue(GenericMethodIdentity.Definition(method), out MethodBody body) ? body : null;

        internal static NativeIlStatus Status(MethodInfo method)
        {
            method = GenericMethodIdentity.Definition(method);
            if (read == null || !sources.ContainsKey(method)
                || read(method.MethodHandle.Value, out Snapshot result, (uint)Marshal.SizeOf<Snapshot>()) != 1) return default;
            return new NativeIlStatus(new NativeRuntime.Snapshot { State = (uint)(result.Registered == 1 ? NativeIlState.Applied : NativeIlState.Missing),
                Publications = result.Publications, Sites = result.Compilations, Intact = result.Registered });
        }

        internal static void Protect(MethodInfo method)
        {
            Initialize();
            method = GenericMethodIdentity.Definition(method);
            MethodBody body = method.GetMethodBody() ?? throw new InvalidOperationException("Missing generic metadata body: " + method);
            Register(method, body.GetILAsByteArray(), null);
            if (!sources.TryAdd(method, body)) throw new InvalidOperationException("Generic body is already protected: " + method);
            RuntimeBodyBoundary.Track(method);
        }

        internal static void Publish(MethodInfo method, byte[] body, byte[] previous)
        {
            Core.LibraryCapabilities.RequirePublicationThread();
            Initialize();
            method = GenericMethodIdentity.Definition(method);
            bool first = !sources.ContainsKey(method);
            if (first && !sources.TryAdd(method, method.GetMethodBody() ?? throw new InvalidOperationException("Missing generic definition: " + method)))
                throw new InvalidOperationException("Generic definition is already published: " + method);
            byte[] old = previous == null ? sources[method].GetILAsByteArray() : Code(previous);
            byte[] next = Code(body);
            bool registered = false, attempted = false;
            try
            {
                RuntimeBodyBoundary.Track(method);
                Register(method, next, old);
                registered = true; attempted = true;
                RuntimeMethodBodies.Replace(method, body);
                Register(method, next, null);
            }
            catch (Exception failure)
            {
                try
                {
                    if (attempted)
                    {
                        Register(method, old, next);
                        if (first) RuntimeMethodBodies.Revert(method);
                        else RuntimeMethodBodies.Replace(method, previous);
                    }
                    if (first)
                    {
                        if (registered) Release(method);
                        else { sources.TryRemove(method, out _); RuntimeBodyBoundary.Release(method); }
                    }
                    else Register(method, old, null);
                }
                catch (Exception rollback) { throw new AggregateException("Generic publication and rollback failed.", failure, rollback); }
                throw;
            }
        }

        private static byte[] Code(byte[] body)
        {
            int length = BitConverter.ToInt32(body, 4);
            var code = new byte[length];
            Buffer.BlockCopy(body, 12, code, 0, length);
            return code;
        }

        private static void Register(MethodInfo method, byte[] code, byte[] previous)
        {
            Type owner = method.DeclaringType;
            if (owner.IsGenericType) owner = owner.GetGenericTypeDefinition();
            int result = set(NativeModule.Authority, owner.TypeHandle.Value, method.MethodHandle.Value,
                code, (uint)code.Length, previous, (uint)(previous?.Length ?? 0));
            if (result != 1) throw new InvalidOperationException("Generic native publication was rejected: " + result);
        }

        internal static void Release(MethodInfo method)
        {
            method = GenericMethodIdentity.Definition(method);
            if (release(NativeModule.Authority, method.MethodHandle.Value) != 1)
                throw new InvalidOperationException("Generic native publication could not be released.");
            sources.TryRemove(method, out _);
            RuntimeBodyBoundary.Release(method);
        }
    }
}
