using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    internal sealed class WorldArtRangeWindow : Window
    {
        private readonly WorldArtService service;
        private readonly WorldArtSubject subject;
        private readonly WorldArtKind kind;
        private readonly Texture2D icon;
        private readonly WorldArtRange range;
        public override Vector2 InitialSize => new(460f, 390f);

        internal WorldArtRangeWindow(WorldArtService service, WorldArtSubject subject, WorldArtKind kind, Texture2D icon)
        {
            this.service = service; this.subject = subject; this.kind = kind; this.icon = icon;
            range = service.RangeFor(subject);
            forcePause = true; doCloseX = true; absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            GameFont oldFont = Text.Font;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, rect.width - 30f, 36f), "Lihuat_WorldArtRangeTitle".Translate(
                    (kind == WorldArtKind.Opening ? WorldArtDefs.Lihuat_Opening : WorldArtDefs.Lihuat_Starfall).LabelCap));
                Text.Font = GameFont.Small;
                if (kind == WorldArtKind.Opening)
                {
                    range.Width = Slider(rect.width, 52f, "Lihuat_WorldArtWidth", range.Width, 1, WorldArtRange.MaxWidth);
                    range.Length = Slider(rect.width, 114f, "Lihuat_WorldArtLength", range.Length, 1, WorldArtRange.MaxLength);
                    Widgets.Label(new Rect(0, 176f, rect.width, 26f), "Lihuat_WorldArtHeading".Translate(Mathf.RoundToInt(range.Heading)));
                    range.Heading = Mathf.Round(Widgets.HorizontalSlider(new Rect(0, 204f, rect.width, 26f), range.Heading, 0f, 175f) / 5f) * 5f;
                }
                else range.Radius = Slider(rect.width, 70f, "Lihuat_WorldArtRadius", range.Radius, 0, WorldArtRange.MaxRadius);
                float cost = kind == WorldArtKind.Opening ? subject.Profile.clearingCost : subject.Profile.impactCost;
                Widgets.Label(new Rect(0, 256f, rect.width, 32f), "Lihuat_WorldArtCost".Translate(cost, subject.ResourceKey.Translate()));
                if (Widgets.ButtonText(new Rect(0, rect.height - 44f, rect.width, 40f), "Lihuat_WorldArtChooseTarget".Translate()))
                {
                    service.SetRange(subject, range);
                    Close();
                    Find.WindowStack.Add(new WorldArtTargetingWindow(service, subject, kind, range.Copy(), icon));
                }
            }
            finally { Text.Font = oldFont; }
        }

        private static int Slider(float width, float y, string key, int value, int minimum, int maximum)
        {
            Widgets.Label(new Rect(0, y, width, 26f), key.Translate(value));
            return Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0, y + 28f, width, 26f), value, minimum, maximum));
        }
    }
}
