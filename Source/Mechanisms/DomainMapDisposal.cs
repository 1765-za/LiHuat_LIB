using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DomainMapDisposal
    {
        private static readonly AccessTools.FieldRef<MapDrawer, Section[,]> sections =
            AccessTools.FieldRefAccess<MapDrawer, Section[,]>("sections");
        private static readonly AccessTools.FieldRef<MapDrawer, List<MapDrawLayer>> global =
            AccessTools.FieldRefAccess<MapDrawer, List<MapDrawLayer>>("global");
        private static readonly Action<Map> disposePartial = Build();

        internal static void Dispose(Map map)
        {
            if (map.mapDrawer == null || Complete(map.mapDrawer)) map.Dispose();
            else disposePartial(map);
        }

        private static Action<Map> Build()
        {
            var body = IndependentIl.Clone(AccessTools.Method(typeof(Map), nameof(Map.Dispose)), (method, virtualCall) =>
                method.DeclaringType == typeof(MapDrawer) && method.Name == nameof(MapDrawer.Dispose)
                    ? AccessTools.Method(typeof(DomainMapDisposal), nameof(DisposeDrawer)) : method);
            return (Action<Map>)body.CreateDelegate(typeof(Action<Map>));
        }

        private static bool Complete(MapDrawer drawer)
        {
            Section[,] allocated = sections(drawer);
            if (allocated == null || global(drawer) == null) return false;
            foreach (Section section in allocated) if (section == null) return false;
            return true;
        }

        private static void DisposeDrawer(MapDrawer drawer)
        {
            // Generation can fail before any render sections exist; dispose only resources actually created.
            Section[,] allocated = sections(drawer);
            if (allocated != null)
                foreach (Section section in allocated) section?.Dispose();
            List<MapDrawLayer> layers = global(drawer);
            if (layers != null)
            {
                foreach (MapDrawLayer layer in layers) layer?.Dispose();
                layers.Clear();
            }
            sections(drawer) = null;
        }
    }
}
