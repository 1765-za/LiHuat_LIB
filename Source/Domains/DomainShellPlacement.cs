using System;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Domains
{
    internal static class DomainShellPlacement
    {
        internal static void Place(Building_DomainShell shell, Map map, IntVec3 cell)
        {
            if (shell.Spawned || shell.Destroyed || !Find.Maps.Contains(map)
                || !GenAdj.OccupiedRect(cell, Rot4.North, shell.def.Size).InBounds(map))
                throw new InvalidOperationException("Invalid domain shell placement.");
            using (new OperationScope(OperationKind.Transfer))
            {
                shell.Rotation = Rot4.North;
                shell.Position = cell;
                shell.SpawnSetup(map, false);
                if (!MapRegistrationStorage.Placed(map, shell))
                    throw new InvalidOperationException("Domain shell registration is incomplete.");
            }
        }
    }
}
