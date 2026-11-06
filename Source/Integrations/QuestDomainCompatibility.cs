using LihuatLib.Dispatching;
using System;
using System.Linq.Expressions;
using HarmonyLib;
using Verse;

namespace LihuatLib.Integrations
{
    internal static class QuestDomainCompatibility
    {
        private static Func<object, Pawn> caster;
        private static Func<object, Map> map;

        internal static void Install()
        {
            Type worker = AccessTools.TypeByName("QuestEditor_Library.DomainWorker");
            if (worker == null) return;
            Type domain = AccessTools.TypeByName("QuestEditor_Library.MapComponent_Domain");
            caster = Reader<Pawn>(worker, "Caster");
            map = Reader<Map>(worker, "map");
            NativeDispatcher.Register(AccessTools.PropertyGetter(worker, "CasterIsPresent"),
                postfix: new DispatchMethod(typeof(QuestDomainCompatibility), nameof(CasterPresence)));
            NativeDispatcher.Register(AccessTools.DeclaredMethod(domain, "ReturnToSource"),
                prefix: new DispatchMethod(typeof(QuestDomainCompatibility), nameof(ReturnToSource)));
        }

        private static Func<object, T> Reader<T>(Type type, string property)
        {
            var instance = Expression.Parameter(typeof(object));
            var call = Expression.Call(Expression.Convert(instance, type), AccessTools.PropertyGetter(type, property));
            return Expression.Lambda<Func<object, T>>(call, instance).Compile();
        }

        private static void CasterPresence(object __instance, ref bool __result)
        {
            if (!__result && LihuatGame.Current?.Domains is { } domains)
                __result = domains.ContainsInNestedDomain(caster(__instance), map(__instance));
        }

        private static bool ReturnToSource(MapComponent __instance) =>
            LihuatGame.Current?.Domains.HasNestedDomain(__instance.map) != true;
    }
}
