using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LihuatLib.Core
{
    internal sealed class ReferenceIdentity<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceIdentity<T> Instance = new ReferenceIdentity<T>();
        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
}
