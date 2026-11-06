using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    internal sealed class WorldArtTargetingWindow : Window
    {
        private static readonly AccessTools.FieldRef<WorldTargeter, Action> update = AccessTools.FieldRefAccess<WorldTargeter, Action>("onUpdate");
        private readonly WorldArtService service;
        private readonly WorldArtSubject subject;
        private readonly WorldArtKind kind;
        private readonly WorldArtRange range;
        private readonly Texture2D icon;
        private readonly Action draw;
        private readonly Mesh mesh = new() { name = "LihuatWorldArtPreview" };
        private PlanetTile shown = PlanetTile.Invalid;
        public override Vector2 InitialSize => Vector2.one;
        protected override float Margin => 0f;

        internal WorldArtTargetingWindow(WorldArtService service, WorldArtSubject subject, WorldArtKind kind, WorldArtRange range, Texture2D icon)
        {
            this.service = service; this.subject = subject; this.kind = kind; this.range = range; this.icon = icon;
            draw = Preview;
            doWindowBackground = false; doCloseX = false; closeOnAccept = false; closeOnCancel = false;
            absorbInputAroundWindow = false; preventCameraMotion = false; shadowAlpha = 0f;
        }
        public override void PreOpen()
        {
            base.PreOpen();
            Find.WorldTargeter.BeginTargeting(target =>
            {
                WorldArtCast cast = service.BeginCast(subject, kind, target.Tile, range);
                if (cast == null) return false;
                Find.WindowStack.Add(new WorldArtCinematicWindow(cast));
                return true;
            }, true, icon, onUpdate: draw, canSelectTarget: target => WorldArtGeometry.Surface(target.Tile), showCancelButton: true);
        }
        public override void WindowUpdate()
        {
            if (!ReferenceEquals(update(Find.WorldTargeter), draw)) Close(false);
        }
        public override void DoWindowContents(Rect rect) { }
        public override void PostClose()
        {
            if (ReferenceEquals(update(Find.WorldTargeter), draw)) Find.WorldTargeter.StopTargeting();
            UnityEngine.Object.Destroy(mesh);
            base.PostClose();
        }
        private void Preview() => DrawPreview(GenWorld.MouseTile());
        private void DrawPreview(PlanetTile tile)
        {
            if (!WorldArtGeometry.Surface(tile)) return;
            if (shown != tile) { Rebuild(tile); shown = tile; }
            WorldArtEffectDraw.Mesh(mesh, Matrix4x4.identity, WorldArtEffectDraw.Solid,
                kind == WorldArtKind.Opening ? new Color(.3f, .85f, 1f, .3f) : new Color(1f, .45f, .15f, .35f));
        }
        private void Rebuild(PlanetTile center)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            var corners = new List<Vector3>(8);
            foreach (PlanetTile tile in WorldArtGeometry.Footprint(center, kind, range))
            {
                corners.Clear(); tile.Layer.GetTileVertices(tile, corners);
                int first = vertices.Count;
                foreach (Vector3 vertex in corners)
                { vertices.Add(vertex + (vertex - tile.Layer.Origin).normalized * .08f); uv.Add(Vector2.zero); }
                for (int i = 1; i < corners.Count - 1; i++)
                { triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + i); }
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        }
    }
}
