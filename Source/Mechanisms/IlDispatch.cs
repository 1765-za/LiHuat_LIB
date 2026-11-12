using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    public sealed class IlRegistration : IDisposable
    {
        public MethodInfo Method { get; }
        public string Identity { get; }
        public int Order { get; }
        public bool Active { get; private set; } = true;
        public ProtectionRoute? Route => NativeIlGuards.RouteFor(Method);
        internal IlRegistration(MethodInfo method, string identity, int order)
        { Method = method; Identity = identity; Order = order; }
        public void Dispose()
        {
            if (!Active) return;
            NativeIlGuards.RemoveTransform(Method, Identity);
            Active = false;
        }
    }

    public static class IlDispatch
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IlRegistration Register(MethodInfo method, string owner, string key, int order, Func<MethodInfo, DynamicMethod> build, bool bypassDeveloper = false)
        {
            LibraryCapabilities.Require(LibraryCapability.IlDispatch);
            if (!InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly()))
                throw new InvalidOperationException("Register the consuming assembly before declaring IL handlers.");
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(key) || owner.Contains(":") || key.Contains(":"))
                throw new ArgumentException("An IL handler needs separate non-empty owner and key values without colons.");
            string identity = owner + ":" + key;
            NativeIlGuards.RegisterLayer(method, identity, order, build, bypassDeveloper);
            return new IlRegistration(method, identity, order);
        }
    }
}
