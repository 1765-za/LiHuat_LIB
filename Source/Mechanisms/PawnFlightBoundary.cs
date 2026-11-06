using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LihuatLib.Domains;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class PawnFlightBoundary
    {
        internal static void Install() => RegisterFlight(typeof(PawnFlyer));

        internal static void RegisterFlight(Type root)
        {
            MethodInfo factory = AccessTools.Method(root, nameof(PawnFlyer.MakeFlyer));
            NativeIlGuards.Scoped(factory,
                AccessTools.Method(typeof(PawnFlightRules), nameof(PawnFlightRules.BeginTakeoff)).MakeGenericMethod(factory.ReturnType), new[] { 1, 2 },
                AccessTools.Method(typeof(PawnFlightRules), nameof(PawnFlightRules.End)), Array.Empty<int>());
            var methods = new HashSet<MethodInfo>();
            Register(root, root, methods);
            foreach (Type type in root.AllSubclassesNonAbstract()) Register(root, type, methods);
        }
        private static void Register(Type root, Type type, HashSet<MethodInfo> methods)
        {
            foreach (string name in new[] { "RespawnPawn", nameof(PawnFlyer.SpawnSetup), "Tick", "TickInterval", "get_DrawPos", "DrawAt", nameof(PawnFlyer.DynamicDrawPhaseAt) })
            {
                MethodInfo method = NativeIlGuards.Definition(AccessTools.Method(type, name));
                if (method?.GetMethodBody() == null || !root.IsAssignableFrom(method.DeclaringType) || !methods.Add(method)) continue;
                if (name == "RespawnPawn")
                    NativeIlGuards.Scoped(method, AccessTools.Method(typeof(PawnFlightRules), nameof(PawnFlightRules.BeginLanding)), new[] { 0 },
                        AccessTools.Method(typeof(PawnFlightRules), nameof(PawnFlightRules.End)), Array.Empty<int>());
                else NativeIlGuards.Guard(method, AccessTools.Method(typeof(PawnFlightRules),
                    name == "get_DrawPos" || name == "DrawAt" || name == nameof(PawnFlyer.DynamicDrawPhaseAt)
                        ? nameof(PawnFlightRules.HasPassenger) : nameof(PawnFlightRules.KeepValid)), 0);
            }
        }
    }
}
