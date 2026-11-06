using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Experimental.VirtualEntities
{
    public sealed class VmMethodRoute : IDisposable
    {
        private const int LayerOrder = NativeIlGuards.GateOrder + 50;
        private const string LayerId = "lihuat.lib.experimental.vm";
        private static readonly ConcurrentDictionary<int, VmMethodRoute> registry = new();
        private static int sequence;
        private readonly VmExperimentSession session;
        private readonly MethodInfo method, gate;
        private readonly int recipient, token;
        private readonly VmRouteKind kind;
        private long blocked;
        private bool disposed;
        public long BlockedCalls => Interlocked.Read(ref blocked);

        private VmMethodRoute(VmExperimentSession session, MethodInfo method, int recipient, VmRouteKind kind, bool address)
        {
            this.session = session; this.method = method; this.recipient = recipient; this.kind = kind;
            token = Interlocked.Increment(ref sequence);
            gate = typeof(VmMethodRoute).GetMethod(address ? nameof(AllowAddress) : nameof(AllowReference), BindingFlags.NonPublic | BindingFlags.Static);
        }

        internal static VmMethodRoute Install(VmExperimentSession session, MethodInfo entry, int recipient, VmRouteKind kind)
        {
            ArgumentNullException.ThrowIfNull(entry);
            MethodInfo method = NativeIlGuards.Definition(entry);
            NativeRuntime.Validate(method);
            if (method.Module.Assembly == typeof(Pawn).Assembly || method.Module.Assembly == typeof(VmMethodRoute).Assembly)
                throw new ArgumentException("VM routes must be explicit external entity entry points.", nameof(entry));
            Type[] args = IndependentIl.Parameters(method);
            if (method.ReturnType != typeof(void) || Array.Exists(args, t => t.IsByRef || t.IsPointer))
                throw new ArgumentException("A gated VM route must return void without by-reference or pointer parameters.", nameof(entry));
            if (recipient < 0 || recipient >= args.Length || args[recipient].IsValueType && args[recipient] != typeof(IntPtr))
                throw new ArgumentOutOfRangeException(nameof(recipient));
            var route = new VmMethodRoute(session, method, recipient, kind, args[recipient] == typeof(IntPtr));
            registry.TryAdd(route.token, route);
            try { NativeIlGuards.RegisterLayer(method, LayerId, LayerOrder, route.Build, false); }
            catch { registry.TryRemove(route.token, out _); throw; }
            return route;
        }

        private DynamicMethod Build(MethodInfo continuation) => IndependentIl.Guard(method, continuation, gate, new[] { recipient }, token);
        private static bool AllowReference(int token, object target) => !registry.TryGetValue(token, out var route)
            || route.Admit(route.session.Blocks(target, route.kind));
        private static bool AllowAddress(int token, IntPtr target) => !registry.TryGetValue(token, out var route)
            || route.Admit(route.session.Blocks(target, route.kind));
        private bool Admit(bool denied)
        { if (!denied) return true; Interlocked.Increment(ref blocked); return false; }

        public void Dispose()
        {
            if (disposed) return;
            session.RequireActive();
            NativeIlGuards.RemoveTransform(method, LayerId);
            registry.TryRemove(token, out _); session.Release(this); disposed = true;
        }
    }
}
