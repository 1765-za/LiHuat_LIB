using System;
using System.Reflection;
using HarmonyLib;

namespace LihuatLib.Dispatching
{
    public sealed class DispatchMethod
    {
        internal readonly MethodInfo Method;
        public int priority = 400;
        public int order;
        public DispatchMethod(MethodInfo method) { Method = method ?? throw new ArgumentNullException(nameof(method)); }
        public DispatchMethod(Type owner, string name) : this(AccessTools.DeclaredMethod(owner, name)) { }
    }

    internal enum DispatchPhase { Prefix, Postfix, Transpiler, Finalizer }

    internal sealed class DispatchHandler
    {
        internal MethodInfo Method;
        internal DispatchPhase Phase;
        internal int Priority, Order;
        internal Type StateOwner => Method.DeclaringType;
        internal string Identity => Method.Module.ModuleVersionId + ":" + Method.MetadataToken + ":" + Phase;
    }
}
