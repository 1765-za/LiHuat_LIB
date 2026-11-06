using LihuatLib.Core;
using System.Reflection;
using Verse;

namespace LihuatLib.Domains
{
    internal static class DomainShellDepartureRules
    {
        internal static bool Blocks(Thing thing, DestructiveEntry entry)
        {
            if (entry != DestructiveEntry.Kill && entry != DestructiveEntry.Destroy && entry != DestructiveEntry.DeSpawn
                || !(thing is Building_DomainShell shell) || shell.HitPoints <= 0) return false;
            DomainArea area = DomainAPI.Find(shell.DomainId);
            return area?.Shell == shell && !area.Ending && !area.Closed;
        }

        internal static bool BlocksGeneratedMutation(Thing thing, FieldInfo[] fields)
        {
            if (!Blocks(thing, DestructiveEntry.Destroy)) return false;
            foreach (FieldInfo field in fields)
                if (field.DeclaringType == typeof(Thing) && (field.Name == "mapIndexOrState"
                    || field.Name == "def" || field.Name == "thingIDNumber")) return true;
            return false;
        }
    }
}
