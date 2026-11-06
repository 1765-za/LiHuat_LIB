using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class PawnGenerationStep
    {
        internal static long Cancelled { get; private set; }
        internal static void Run(GenStep step, Map map, GenStepParams parameters)
        {
            try { step.Generate(map, parameters); }
            catch (RegenerationDeniedException) { Cancelled++; }
        }
    }
}
