using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class RegenerationSupplySite
    {
        internal MethodInfo Caller;
        internal MethodInfo Factory;
        internal int Occurrence;
        internal int SourceIndex;
        internal bool SourceLocal;
        internal bool Same(RegenerationSupplySite other) => Caller == other.Caller && Factory == other.Factory
            && Occurrence == other.Occurrence && SourceIndex == other.SourceIndex && SourceLocal == other.SourceLocal;
    }
}
