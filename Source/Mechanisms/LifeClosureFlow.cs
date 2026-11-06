using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeClosureAllocation : IEquatable<LifeClosureAllocation>
    {
        private readonly MethodInfo method;
        private readonly int instruction;
        public LifeClosureAllocation(MethodInfo method, int instruction) { this.method = method; this.instruction = instruction; }
        public bool Equals(LifeClosureAllocation other) => other != null && method == other.method && instruction == other.instruction;
        public override bool Equals(object value) => Equals(value as LifeClosureAllocation);
        public override int GetHashCode() => unchecked(method.GetHashCode() * 31 + instruction);
    }

    internal sealed class LifeDelegateCall : IEquatable<LifeDelegateCall>
    {
        public readonly MethodInfo Method;
        public readonly LifeFlowValue Target;
        public LifeDelegateCall(MethodInfo method, LifeFlowValue target) { Method = method; Target = target; }
        public bool Equals(LifeDelegateCall other) => other != null && Method == other.Method && LifeFlowValue.Same(Target, other.Target);
        public override bool Equals(object value) => Equals(value as LifeDelegateCall);
        public override int GetHashCode() => unchecked(Method.GetHashCode() * 31 + Target.ShapeHash);
    }

    internal static class LifeClosureFlow
    {
        public static LifeFlowValue Construct(ConstructorInfo constructor, LifeFlowValue[] args, MethodInfo method, int instruction)
        {
            Type type = constructor.DeclaringType;
            if (typeof(Delegate).IsAssignableFrom(type) && args.Length == 2 && args[1].Constant is MethodInfo callback)
                return new LifeFlowValue(constant: new LifeDelegateCall(callback, args[0]));
            return type.IsDefined(typeof(CompilerGeneratedAttribute), false)
                ? new LifeFlowValue(paths: new[] { LifeObjectPath.Symbol(new LifeClosureAllocation(method, instruction)) })
                : LifeFlowValue.Empty;
        }

        public static bool Read(Dictionary<LifeObjectPath, LifeFlowValue> captures, LifeFlowValue receiver, FieldInfo field, out LifeFlowValue value)
        {
            value = LifeFlowValue.Empty;
            if (captures == null || receiver.Paths.Length == 0) return false;
            bool first = true;
            foreach (LifeObjectPath path in receiver.Paths)
            {
                if (!(path.Root is LifeClosureAllocation) || !captures.TryGetValue(path.Append(field), out LifeFlowValue stored)) return false;
                value = first ? stored : LifeFlowValue.Merge(value, stored);
                first = false;
            }
            return true;
        }

        public static void Store(ref Dictionary<LifeObjectPath, LifeFlowValue> captures, LifeFlowValue receiver, FieldInfo field, LifeFlowValue value)
        {
            if (receiver.Paths.Length != 1 || !(receiver.Paths[0].Root is LifeClosureAllocation)) return;
            if (captures == null) captures = new Dictionary<LifeObjectPath, LifeFlowValue>();
            captures[receiver.Paths[0].Append(field)] = value;
        }

        public static Dictionary<LifeObjectPath, LifeFlowValue> Copy(Dictionary<LifeObjectPath, LifeFlowValue> captures) =>
            captures == null ? null : new Dictionary<LifeObjectPath, LifeFlowValue>(captures);

        public static Dictionary<LifeObjectPath, LifeFlowValue> Merge(Dictionary<LifeObjectPath, LifeFlowValue> left,
            Dictionary<LifeObjectPath, LifeFlowValue> right)
        {
            if (left == null && right == null) return null;
            var result = new Dictionary<LifeObjectPath, LifeFlowValue>();
            foreach (LifeObjectPath key in (left?.Keys.AsEnumerable() ?? Enumerable.Empty<LifeObjectPath>())
                .Concat(right?.Keys.AsEnumerable() ?? Enumerable.Empty<LifeObjectPath>()).Distinct())
            {
                LifeFlowValue a = null, b = null;
                left?.TryGetValue(key, out a); right?.TryGetValue(key, out b);
                // An unassigned capture must not become proof of a unique execution recipient.
                var unknown = new LifeFlowValue(paths: new[] { key });
                result[key] = LifeFlowValue.Merge(a ?? unknown, b ?? unknown);
            }
            return result;
        }
    }
}
