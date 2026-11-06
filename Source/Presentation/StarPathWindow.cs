using System;
using System.Collections.Generic;
using LihuatLib.StarPaths;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    public sealed class StarPathWindow : Window
    {
        private static readonly Color gold = new Color(.91f, .73f, .32f), dim = new Color(.48f, .51f, .57f);
        private static readonly Color[] branches = { new Color(.96f, .66f, .28f), new Color(.86f, .49f, .37f),
            new Color(.39f, .69f, .89f), new Color(.50f, .79f, .65f), new Color(.94f, .57f, .60f),
            new Color(.52f, .73f, .95f), new Color(.65f, .78f, .43f), new Color(.77f, .64f, .91f) };
        private readonly ISkillTreeSession subject;
        private readonly string[] titles, cards;
        private readonly Vector2[] positions;
        private readonly int[] branchRoots;
        private readonly (Vector2 Start, Vector2 End, StarPathRequirement Requirement, int Row)[] links;
        private StarPathNodeDef selected;
        private string title, status, description, upgrade;
        private int revision = -1;
        private Vector2 scroll, detailScroll;
        public override Vector2 InitialSize => new Vector2(Math.Min(1170f, UI.screenWidth - 40f), Math.Min(735f, UI.screenHeight - 40f));

        public StarPathWindow(StarPathSubject subject) : this(new StarPathTreeSession(subject)) { }
        public StarPathWindow(AstralArts.AstralEntry entry) : this(new AstralTreeSession(entry)) { }
        private StarPathWindow(ISkillTreeSession subject)
        {
            this.subject = subject ?? throw new ArgumentNullException(nameof(subject));
            int count = subject.Tree.nodes.Count;
            titles = new string[count]; cards = new string[count]; positions = new Vector2[count];
            var rows = new SortedDictionary<int, int>();
            var indices = new Dictionary<StarPathNodeDef, int>();
            for (int i = 0; i < count; i++)
            {
                StarPathNodeDef node = subject.Tree.nodes[i];
                titles[i] = node.Title; positions[i] = new Vector2(18f + node.column * 218f, 20f + node.row * 112f);
                indices.Add(node, i);
                if (!rows.TryGetValue(node.row, out int first) || node.column < subject.Tree.nodes[first].column) rows[node.row] = i;
            }
            branchRoots = new int[rows.Count]; rows.Values.CopyTo(branchRoots, 0);
            var connections = new List<(Vector2, Vector2, StarPathRequirement, int)>();
            for (int i = 0; i < count; i++)
                foreach (StarPathRequirement requirement in subject.Tree.nodes[i].requirements)
                {
                    StarPathNodeDef node = subject.Tree.nodes[i];
                    int parent = indices[requirement.node];
                    bool vertical = requirement.node.column == node.column;
                    Vector2 start = vertical ? positions[parent] + new Vector2(97f, node.row < requirement.node.row ? 0f : 86f)
                        : positions[parent] + new Vector2(194f, 43f);
                    Vector2 end = vertical ? positions[i] + new Vector2(97f, node.row < requirement.node.row ? 86f : 0f)
                        : positions[i] + new Vector2(0f, 43f);
                    connections.Add((start, end, requirement, node.row));
                }
            links = connections.ToArray();
            selected = subject.Tree.nodes[0];
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true;
            Refresh();
        }

        private void Refresh()
        {
            revision = subject.Revision;
            title = subject.Title + " · " + subject.Pawn.LabelShortCap;
            status = subject.Status;
            for (int i = 0; i < cards.Length; i++)
            {
                StarPathNodeDef node = subject.Tree.nodes[i]; int rank = subject.Account.Rank(node);
                cards[i] = rank == node.ranks.Count ? "Lihuat_StarPathMastered".Translate().ToString()
                    : "Lihuat_StarPathCost".Translate(node.ranks[rank].cost);
            }
            RefreshSelected();
        }
        private void RefreshSelected()
        {
            description = subject.Detail(selected);
            int rank = subject.Account.Rank(selected);
            upgrade = rank == selected.ranks.Count ? "Lihuat_StarPathMastered".Translate().ToString()
                : "Lihuat_StarPathUpgrade".Translate(selected.ranks[rank].cost);
        }
        public override void DoWindowContents(Rect rect)
        {
            if (revision != subject.Revision) Refresh();
            Color saved = GUI.color;
            try
            {
                GUI.color = gold; Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(4f, 0f, rect.width - 34f, 36f), title);
                Text.Font = GameFont.Small; GUI.color = Color.white;
                Widgets.Label(new Rect(5f, 42f, rect.width - (subject is ISkillTreeRelearn ? 155f : 0f), 26f), status);
                if (subject is ISkillTreeRelearn relearn) DrawRelearn(new Rect(rect.width - 145f, 39f, 140f, 30f), relearn);
                GUI.color = dim; Widgets.Label(new Rect(5f, 73f, rect.width, 40f), subject.Rule);
                GUI.color = Color.white;
                float sidebar = Math.Min(285f, rect.width * .34f);
                DrawNavigation(new Rect(0f, 115f, rect.width - sidebar - 18f, 26f));
                Rect board = new Rect(0f, 153f, rect.width - sidebar - 18f, rect.height - 153f);
                DrawTree(board);
                DrawSelected(new Rect(board.xMax + 18f, board.y, sidebar, board.height));
            }
            finally { GUI.color = saved; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; }
        }

        private void DrawNavigation(Rect rect)
        {
            float width = rect.width / branchRoots.Length;
            for (int i = 0; i < branchRoots.Length; i++)
            {
                int index = branchRoots[i];
                Rect button = new Rect(rect.x + i * width, rect.y, width - 4f, rect.height);
                GUI.color = subject.Tree.nodes[index].row == selected.row ? gold : Color.white;
                if (Widgets.ButtonText(button, titles[index])) SelectNode(index, true);
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, "Lihuat_StarPathNavigation".Translate());
        }
        private void DrawRelearn(Rect rect, ISkillTreeRelearn relearn)
        {
            string failure = relearn.RelearnFailure;
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && failure == null;
                if (Widgets.ButtonText(rect, "Lihuat_AstralRelearn".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Lihuat_AstralRelearnConfirm".Translate(subject.Account.SpentPoints),
                        () => { if (relearn.Relearn()) Refresh(); }));
            }
            finally { GUI.enabled = enabled; }
            if (failure != null) TooltipHandler.TipRegion(rect, failure.Translate());
        }

        private void SelectNode(int index, bool focus)
        {
            selected = subject.Tree.nodes[index]; detailScroll = Vector2.zero;
            if (focus) scroll = new Vector2(scroll.x, Math.Max(0f, positions[index].y - 12f));
            RefreshSelected();
        }

        private void DrawTree(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(.065f, .08f, .105f));
            float width = 0f, height = 0f;
            foreach (Vector2 position in positions) { width = Math.Max(width, position.x + 212f); height = Math.Max(height, position.y + 104f); }
            Rect view = new Rect(0f, 0f, Math.Max(rect.width - 18f, width), Math.Max(rect.height - 18f, height));
            Widgets.BeginScrollView(rect, ref scroll, view);
            try
            {
                foreach (var link in links)
                    Widgets.DrawLine(link.Start, link.End, subject.Account.Rank(link.Requirement.node) >= link.Requirement.rank
                        ? branches[link.Row % branches.Length] : new Color(.24f, .26f, .30f), 2f);
                for (int i = 0; i < subject.Tree.nodes.Count; i++)
                    if (positions[i].y + 86f >= scroll.y && positions[i].y <= scroll.y + rect.height) DrawNode(i);
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawNode(int index)
        {
            StarPathNodeDef node = subject.Tree.nodes[index];
            Rect card = new Rect(positions[index].x, positions[index].y, 194f, 86f);
            int rank = subject.Account.Rank(node);
            Color accent = branches[node.row % branches.Length];
            Widgets.DrawBoxSolid(card, selected == node ? new Color(.19f, .20f, .23f) : new Color(.12f, .135f, .16f));
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, 4f, card.height), rank > 0 ? accent : dim);
            GUI.color = selected == node ? gold : Color.white; Widgets.DrawBox(card, selected == node ? 2 : 1);
            GUI.color = Color.white;
            Widgets.Label(new Rect(card.x + 14f, card.y + 9f, 166f, 25f), titles[index]);
            GUI.color = rank > 0 ? accent : dim;
            Widgets.Label(new Rect(card.x + 14f, card.y + 42f, 62f, 25f), rank + " / " + node.ranks.Count);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(card.x + 72f, card.y + 42f, 106f, 25f), cards[index]);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            if (Widgets.ButtonInvisible(card)) SelectNode(index, false);
        }
        private void DrawSelected(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(.10f, .115f, .14f));
            Rect inner = rect.ContractedBy(14f);
            GUI.color = gold; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 34f), selected.Title);
            Text.Font = GameFont.Small; GUI.color = Color.white;
            Rect area = new Rect(inner.x, inner.y + 47f, inner.width, inner.height - 145f);
            Rect view = new Rect(0f, 0f, area.width - 18f, Math.Max(area.height, Text.CalcHeight(description, area.width - 18f)));
            Widgets.BeginScrollView(area, ref detailScroll, view);
            try { Widgets.Label(view, description); }
            finally { Widgets.EndScrollView(); }
            string failure = subject.Failure(selected);
            GUI.color = dim;
            if (failure != null) Widgets.Label(new Rect(inner.x, inner.yMax - 84f, inner.width, 37f), failure.Translate());
            GUI.color = Color.white;
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && failure == null;
                if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 38f, inner.width, 36f), upgrade) && subject.Upgrade(selected)) Refresh();
            }
            finally { GUI.enabled = enabled; }
        }
    }
}
