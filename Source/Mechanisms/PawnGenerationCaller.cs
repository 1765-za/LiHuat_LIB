using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class PawnGenerationCaller
    {
        internal static string Read(StackTrace trace)
        {
            bool generation = false;
            for (int i = 0; i < trace.FrameCount; i++)
            {
                MethodBase method = trace.GetFrame(i)?.GetMethod();
                if (method is MethodInfo info)
                {
                    method = Harmony.GetOriginalMethod(info);
                    if (Equals(method, info) && PatchProvenance.IsReplacement(info)) continue;
                }
                if (method?.DeclaringType == null || method.Module.Assembly.IsDynamic) continue;
                if (method.DeclaringType == typeof(PawnGenerator))
                {
                    generation = true;
                    continue;
                }
                if (generation) return method.DeclaringType.Name + "." + method.Name;
            }
            return string.Empty;
        }
    }
}
