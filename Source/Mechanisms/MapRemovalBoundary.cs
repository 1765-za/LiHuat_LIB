using System;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MapRemovalBoundary
    {
        internal static void Install()
        {
            var method = AccessTools.Method(typeof(Game), nameof(Game.DeinitAndRemoveMap));
            NativeIlGuards.RegisterLayer(method, typeof(MapRemovalBoundary).FullName, NativeIlGuards.ScopeOrder,
                body => IlBoundaryEmitter.Scope(method, body,
                    AccessTools.Method(typeof(MapRemovalBoundary), nameof(Begin)), new[] { 0, 1 },
                    AccessTools.Method(typeof(MapRemovalBoundary), nameof(End)), Array.Empty<int>()), false);
        }

        private static bool Begin(Game game, Map map, out Map state)
        {
            state = null;
            if (game != Current.Game || map == null || !game.Maps.Contains(map)) return true;
            if (LihuatGame.Current?.Lifecycle.BeforeMapRemoval(map) == false) return false;
            state = map;
            return true;
        }

        private static void End(Map state)
        {
            if (state != null) LihuatGame.Current?.Lifecycle.AfterMapRemoval(state, !Find.Maps.Contains(state));
        }
    }
}
