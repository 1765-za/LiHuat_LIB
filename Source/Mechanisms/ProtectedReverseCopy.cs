using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class ProtectedReverseCopy
    {
        private sealed class Source
        {
            internal readonly MethodBase Original;
            internal Source(MethodBase original) => Original = original;
        }
        private static readonly ConditionalWeakTable<MethodInfo, Source> wrapped = new();

        public static MethodInfo Wrap(MethodInfo replacement, MethodBase original)
        {
            if (replacement == null) return null;
            if (original == null) return GeneratedMethodBoundary.ProtectCopy(replacement);
            if (wrapped.TryGetValue(replacement, out Source source) && Equals(source.Original, original)) return replacement;
            MethodInfo result = ReverseLifecycle.Wrap(NativeIlGuards.WrapCopy(replacement, original) ?? replacement, original);
            if (!ReferenceEquals(result, replacement)) wrapped.GetValue(result, _ => new Source(original));
            return result;
        }
    }
}
