using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DomainConfinementBoundary
    {
        internal static bool AllowsLocationWrite(FieldInfo field, object target, object value)
            => !(target is Thing thing) || LihuatGame.Current?.Domains.AllowsLocationWrite(field, thing, value) != false;
    }
}
