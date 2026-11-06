using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    public sealed class CilRewrite
    {
        internal static event Action<MethodInfo> Changed;
        private static readonly Dictionary<MethodInfo, CilRewrite> rewrites = new Dictionary<MethodInfo, CilRewrite>();
        private readonly MethodInfo method;
        private readonly byte[] replacement;
        public NativeIlStatus Status
        {
            get
            {
                lock (rewrites)
                    return rewrites.TryGetValue(method, out CilRewrite current) && current == this ? NativeRuntime.Status(method) : default;
            }
        }
        private CilRewrite(MethodInfo method, byte[] replacement) { this.method = method; this.replacement = replacement; }

        public static byte[] ReadBody(MethodInfo method)
        {
            NativeRuntime.Validate(method);
            MethodBody body = method.GetMethodBody() ?? throw new InvalidOperationException("Missing metadata IL: " + method);
            return (byte[])body.GetILAsByteArray().Clone();
        }

        internal static byte[] ReadSource(MethodBase method, MethodBody body)
        {
            lock (rewrites)
                if (method is MethodInfo info && rewrites.TryGetValue(info, out CilRewrite rewrite) && rewrite.Status.State == NativeIlState.Applied)
                    return (byte[])rewrite.replacement.Clone();
            return body.GetILAsByteArray();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static CilRewrite Apply(MethodInfo method, byte[] replacement)
        {
            LibraryCapabilities.Require(LibraryCapability.RuntimeIl);
            LibraryCapabilities.RequirePublicationThread();
            CodePublicationAuthority.RequireCaller();
            NativeRuntime.Validate(method);
            MethodBody body = method.GetMethodBody() ?? throw new InvalidOperationException("Missing metadata IL: " + method);
            byte[] expected = ReadBody(method);
            if (replacement == null || replacement.Length != expected.Length || replacement.Length == 0 || replacement.Length > 65536)
                throw new ArgumentException("Runtime CIL must preserve the metadata body length.", nameof(replacement));
            byte[] code = (byte[])replacement.Clone();
            DynamicMethod validation = IndependentIl.New(method);
            validation.InitLocals = body.InitLocals;
            if (!PortableIlEmitter.TryEmit(method, body, code, validation.GetILGenerator(), out string reason))
                throw new ArgumentException("Runtime CIL validation failed: " + reason, nameof(replacement));
            Delegate compiled = IndependentIl.Compile(validation);
            RuntimeMethodAddress.Get(validation);
            CilRewrite result;
            lock (rewrites)
            {
                if (rewrites.ContainsKey(method)) throw new InvalidOperationException("CIL rewrite already registered: " + method);
                result = new CilRewrite(method, code);
                rewrites.Add(method, result);
                try { NativeRuntime.Register(method, expected, code, null); }
                catch { rewrites.Remove(method); throw; }
            }
            GC.KeepAlive(compiled);
            Changed?.Invoke(method);
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool Revert()
        {
            CodePublicationAuthority.RequireCaller();
            LibraryCapabilities.RequirePublicationThread();
            lock (rewrites)
            {
                if (!rewrites.TryGetValue(method, out CilRewrite current) || current != this || !NativeRuntime.Remove(method)) return false;
                rewrites.Remove(method);
                Changed?.Invoke(method);
                return true;
            }
        }
    }
}
