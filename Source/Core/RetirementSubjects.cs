using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace LihuatLib.Core
{
    public sealed class RetirementSubjects
    {
        private sealed class Mark { }
        private readonly ConditionalWeakTable<object, Mark> retired = new();
        private static readonly ConcurrentDictionary<Type, byte> types = new();
        internal static void Register(Type type) => types.TryAdd(type, 0);
        internal static bool Recognizes(Type type) => types.ContainsKey(type);
        public bool Contains(object subject) => subject != null && retired.TryGetValue(subject, out _);
        internal void Retire(object subject)
        {
            if (subject == null || !Recognizes(subject.GetType())) throw new ArgumentException("Unknown retirement subject.");
            retired.GetValue(subject, _ => new Mark());
        }
    }
}
