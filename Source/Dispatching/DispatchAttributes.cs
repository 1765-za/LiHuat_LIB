using System;

namespace LihuatLib.Dispatching
{
    public enum DispatchMember { Method, Getter, Setter }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class DispatchTargetAttribute : Attribute
    {
        internal Type Type;
        internal string Name;
        internal Type[] Arguments;
        internal DispatchMember Member;
        public DispatchTargetAttribute() { }
        public DispatchTargetAttribute(Type type) { Type = type; }
        public DispatchTargetAttribute(Type type, string name) { Type = type; Name = name; }
        public DispatchTargetAttribute(Type type, string name, Type[] arguments) : this(type, name) { Arguments = arguments; }
        public DispatchTargetAttribute(Type type, string name, DispatchMember member) : this(type, name) { Member = member; }
        public DispatchTargetAttribute(string name) { Name = name; }
        public DispatchTargetAttribute(string name, DispatchMember member) { Name = name; Member = member; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DispatchOrderAttribute : Attribute
    {
        internal readonly int Value;
        public DispatchOrderAttribute(int value) { Value = value; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DispatchPriorityAttribute : Attribute
    {
        internal readonly int Value;
        public DispatchPriorityAttribute(int value) { Value = value; }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class DispatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class DispatchPostfixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class DispatchTranspilerAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class DispatchFinalizerAttribute : Attribute { }
}
