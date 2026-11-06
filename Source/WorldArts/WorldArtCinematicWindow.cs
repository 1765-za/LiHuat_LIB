using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    internal sealed class WorldArtCinematicWindow : Window
    {
        private static readonly AccessTools.FieldRef<WorldCameraDriver, float> desired = AccessTools.FieldRefAccess<WorldCameraDriver, float>("desiredAltitude");
        private readonly WorldArtCast cast;
        private readonly Game game;
        private TimeSpeed previousSpeed;
        private float initialAltitude;
        private Vector3 initialLook;
        public override Vector2 InitialSize => new(UI.screenWidth, UI.screenHeight);
        protected override float Margin => 0f;

        internal WorldArtCinematicWindow(WorldArtCast cast)
        {
            this.cast = cast; game = Current.Game;
            doWindowBackground = false; doCloseX = false; closeOnAccept = false; closeOnCancel = true;
            absorbInputAroundWindow = true; preventCameraMotion = true; shadowAlpha = 0f; layer = WindowLayer.Super;
        }
        public override void PreOpen()
        {
            base.PreOpen();
            previousSpeed = Find.TickManager.CurTimeSpeed;
            initialAltitude = Find.WorldCameraDriver.altitude;
            initialLook = Find.WorldCameraDriver.CurrentlyLookingAtPointOnSphere;
            PlanetLayer.Selected = cast.Target.Layer;
            CameraJumper.TryShowWorld();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }
        public override void WindowUpdate()
        {
            if (Current.Game != game || !WorldArtGeometry.Surface(cast.Target)) { Close(false); return; }
            int now = Find.TickManager.TicksGame;
            if (now >= cast.EndAt) { Close(false); return; }
            Focus(now);
        }
        internal void Focus(int now)
        {
            var frame = cast.Frame;
            float before = Mathf.Clamp01((now - cast.StartedAt) / (float)(cast.ImpactAt - cast.StartedAt));
            float after = Mathf.Max(0f, (now - cast.ImpactAt) / 60f);
            Vector3 look;
            float altitude;
            if (cast.Kind == WorldArtKind.Opening)
            {
                look = frame.Center - frame.Origin;
                Camera camera = Find.WorldCamera;
                float extent = frame.Unit * Mathf.Max(cast.Range.Length * .5f + 1f, cast.Range.Width / Mathf.Max(.5f, camera.aspect));
                float fit = extent / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * 1.18f;
                altitude = frame.Radius + Mathf.Max(35f, fit);
            }
            else
            {
                Vector3 meteor = WorldStarfallVisuals.MeteorPosition(cast, before);
                look = Vector3.Lerp(meteor - frame.Origin, frame.Center - frame.Origin, Mathf.Clamp01(before * 1.3f));
                float impactFit = frame.Unit * (cast.Range.Radius + 1f) * 5.8f
                    / Mathf.Tan(Find.WorldCamera.fieldOfView * Mathf.Deg2Rad * .5f);
                float near = Mathf.Max(36f, impactFit);
                altitude = frame.Radius + Mathf.Lerp(Mathf.Max(125f, near + 65f), near, Mathf.SmoothStep(0f, 1f, before));
                altitude += (1f - Mathf.Exp(-after)) * 36f;
            }
            float enter = Mathf.SmoothStep(0f, 1f, (now - cast.StartedAt) / 24f);
            Vector3 direction = Vector3.Slerp(initialLook, look.normalized, enter);
            Find.WorldCameraDriver.JumpTo(direction);
            float zoom = Mathf.Lerp(initialAltitude, Mathf.Max(WorldCameraDriver.MinAltitude, altitude), enter);
            if (after > 0f && after < .3f) zoom += Mathf.Sin(after * 95f) * (1f - after / .3f) * .8f;
            Find.WorldCameraDriver.altitude = zoom; desired(Find.WorldCameraDriver) = zoom;
        }
        public override void DoWindowContents(Rect rect)
        {
            if (cast.Kind == WorldArtKind.Opening) return;
            int now = Find.TickManager.TicksGame;
            float flash = Mathf.Clamp01(1f - Mathf.Abs(now - cast.ImpactAt) / 10f) * .6f;
            if (flash > 0f) Widgets.DrawBoxSolid(rect, new Color(1f, .94f, .76f, flash));
            Widgets.DrawBoxSolid(new Rect(0, 0, rect.width, 28f), new Color(.015f, .02f, .03f, .9f));
            Widgets.DrawBoxSolid(new Rect(0, rect.height - 48f, rect.width, 48f), new Color(.015f, .02f, .03f, .9f));
            if (Widgets.ButtonText(new Rect(rect.width - 156f, rect.height - 40f, 140f, 32f), "Lihuat_WorldArtSkip".Translate())) Close();
        }
        public override void PostClose()
        {
            if (Current.Game == game && Find.TickManager.CurTimeSpeed == TimeSpeed.Normal) Find.TickManager.CurTimeSpeed = previousSpeed;
            base.PostClose();
        }
    }
}
