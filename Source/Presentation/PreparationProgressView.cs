using System.Diagnostics;
using LihuatLib.Core;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class PreparationProgressView
    {
        private static long revision = -1;
        private static long dismissed = -1;
        private static string title, detail;
        internal static void Draw()
        {
            PreparationProgress progress = LibraryPreparation.Progress;
            if (dismissed == progress.Revision) return;
            if (!progress.Active && (!progress.Complete
                || Stopwatch.GetTimestamp() - progress.FinishedAt > Stopwatch.Frequency * 1.5))
            { dismissed = progress.Revision; return; }
            if (revision != progress.Revision)
            {
                revision = progress.Revision;
                title = "Lihuat_PreparationProgress".Translate(progress.Percent);
                detail = progress.Complete ? "Lihuat_PreparationReady".Translate().ToString()
                    : "Lihuat_PreparationStage".Translate(progress.Stage, progress.Stages,
                        progress.StageKey?.Translate().ToString() ?? "", progress.Completed, progress.Total).ToString();
            }
            float width = Mathf.Min(520f, UI.screenWidth - 32f);
            var rect = new Rect((UI.screenWidth - width) * .5f, UI.screenHeight * .5f + 72f, width, 94f);
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            int depth = GUI.depth;
            try
            {
                GUI.depth = 0; GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.DrawWindowBackground(rect);
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 4f, width - 24f, 26f), title);
                var bar = new Rect(rect.x + 16f, rect.y + 34f, width - 32f, 16f);
                Widgets.FillableBar(bar, progress.Percent / 100f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 58f, width - 24f, 24f), detail);
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; GUI.depth = depth; }
        }
    }
}
