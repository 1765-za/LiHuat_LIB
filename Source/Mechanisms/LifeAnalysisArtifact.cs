using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisArtifact
    {
        internal readonly LifeAnalysisResult Result;
        internal readonly Dictionary<MethodInfo, LifeMethodSnapshot> Inputs;
        internal readonly bool Persistable;
        internal Dictionary<MethodInfo, string> Unavailable;

        internal LifeAnalysisArtifact(LifeAnalysisResult result, Dictionary<MethodInfo, LifeMethodSnapshot> inputs, bool persistable = true)
        { Result = result; Inputs = inputs; Persistable = persistable; }
    }
}
