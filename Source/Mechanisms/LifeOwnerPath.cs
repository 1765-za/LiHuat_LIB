using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeOwnerPath
    {
        public static LifeObjectPath ForComponent(Type type)
        {
            if (typeof(ThingComp).IsAssignableFrom(type)) return new LifeObjectPath(AccessTools.Field(typeof(ThingComp), "parent"));
            if (typeof(Hediff).IsAssignableFrom(type)) return new LifeObjectPath(AccessTools.Field(typeof(Hediff), "pawn"));
            if (typeof(HediffComp).IsAssignableFrom(type)) return new LifeObjectPath(AccessTools.Field(typeof(HediffComp), "parent"), AccessTools.Field(typeof(Hediff), "pawn"));
            return null;
        }

        public static LifeObjectPath ForRecord(Type type)
        {
            if (!LifeResourceEvidence.ExternalRecord(type)) return null;
            FieldInfo found = null;
            for (Type cursor = type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
            {
                foreach (FieldInfo field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(Thing).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(Pawn_HealthTracker)) continue;
                    if (found != null) return null;
                    found = field;
                }
            }
            return found == null ? null : new LifeObjectPath(found);
        }

        public static bool Matches(LifeObjectPath owner, LifeObjectPath[] receivers) => owner != null
            && receivers.All(p => p.PawnIdentity().Equals(owner));
    }
}
