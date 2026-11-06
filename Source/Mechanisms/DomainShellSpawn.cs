using System;
using HarmonyLib;
using LihuatLib.Domains;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DomainShellSpawn
    {
        // Building initialization requires Thing registration to finish before it reads Map.
        private static readonly Action<Building, Map, bool> setup = OriginalMethods.CreateTransfer<Action<Building, Map, bool>>(
            AccessTools.DeclaredMethod(typeof(Building), nameof(Building.SpawnSetup)));

        internal static void Initialize(Building_DomainShell shell, Map map, bool respawningAfterLoad) =>
            setup(shell, map, respawningAfterLoad);
    }
}
