using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class OperationCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsInfrastructureReplacement()
            => PatchProvenance.ReplacesInfrastructure(new StackFrame(3, false).GetMethod());
    }
}
