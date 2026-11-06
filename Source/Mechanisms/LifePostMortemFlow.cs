using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifePostMortemFlow
    {
        internal static readonly LifeFlowValue CorpseValue = new LifeFlowValue(paths: new[] { LifeObjectPath.Symbol(typeof(Corpse)) });

        internal static bool ReturnsCorpse(MethodInfo method) => typeof(Corpse).IsAssignableFrom(method.ReturnType);

        internal static bool IsCorpse(LifeFlowValue receiver) => receiver.Paths.Length > 0
            && receiver.Paths.All(p => p.Root is Type type && typeof(Corpse).IsAssignableFrom(type) && p.Fields.Length == 0);
    }
}
