using LihuatLib.AstralArts;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal sealed class AstralEnergyGizmo : Gizmo
    {
        private readonly AstralEntry entry;
        internal AstralEnergyGizmo(AstralEntry entry) { this.entry = entry; Order = -97.9f; }
        public override float GetWidth(float maxWidth) => 190f;
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect box = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            try
            {
                Widgets.DrawWindowBackground(box);
                Rect inner = box.ContractedBy(7f);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = false;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "Lihuat_AstralBar".Translate(
                    entry.Account.Energy.ToString("F1"), entry.Capacity.ToString("F0")));
                Rect bar = new Rect(inner.x, inner.y + 25f, inner.width, 12f);
                Widgets.DrawBoxSolid(bar, new Color(.06f, .07f, .1f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(entry.Account.Energy / entry.Capacity), bar.height), new Color(.36f, .68f, .84f));
                string status = entry.Allows(AstralArt.Repair) ? AstralArtDescriptions.RepairStatus(entry) : "";
                if (entry.Allows(AstralArt.Isolation)) status += (status.Length == 0 ? "" : " / ") + AstralArtDescriptions.IsolationStatus(entry);
                if (entry.Account.EnhanceEnabled) status += (status.Length == 0 ? "" : " / ")
                    + (AstralArtDescriptions.EnhanceKey(entry) + "State").Translate(AstralArtDescriptions.EnhanceStatus(entry));
                Widgets.Label(new Rect(inner.x, inner.y + 42f, inner.width, 20f), status);
                TooltipHandler.TipRegion(box, AstralArtDescriptions.Energy(entry));
                return new GizmoResult(GizmoState.Clear);
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; }
        }
    }
}
