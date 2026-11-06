using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.Characters;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    public sealed class CharacterManagerWindow : Window
    {
        private CharacterManager manager;
        private CharacterEntry[] rows = Array.Empty<CharacterEntry>();
        private Vector2 scroll;
        private string search = "";
        private int filter, revision = -1;
        private bool dirty = true;
        public override Vector2 InitialSize => new Vector2(920f, 580f);
        public CharacterManagerWindow() { doCloseX = true; forcePause = false; draggable = true; }
        internal static void OpenFromSettings()
        {
            if (!LibraryCapabilities.Has(LibraryCapability.CharacterManagement) || Current.ProgramState != ProgramState.Playing
                || LihuatGame.Current?.Characters == null || Find.WindowStack.IsOpen<CharacterManagerWindow>()) return;
            foreach (Window window in Find.WindowStack.Windows.ToArray())
                if (window is Dialog_Options || window is Dialog_ModSettings) window.Close(false);
            Find.WindowStack.Add(new CharacterManagerWindow());
        }
        public override void PostOpen() { base.PostOpen(); manager = LihuatGame.Current?.Characters; dirty = true; Refresh(); }
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (manager == null || manager != LihuatGame.Current?.Characters) { Close(false); return; }
            if (dirty || revision != manager.Revision) Refresh();
        }
        private void Refresh()
        {
            if (manager == null) return;
            rows = manager.Entries.Where(entry => (filter == 0 || manager.IsStored(entry.Pawn) == (filter == 2))
                && (string.IsNullOrEmpty(search) || Name(entry).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.ProviderLabelKey.Translate().ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(entry => entry.ProviderId).ThenBy(Name).ToArray();
            revision = manager.Revision; dirty = false;
        }
        private static string Name(CharacterEntry entry) => entry.Pawn?.LabelShortCap ?? "Lihuat_CharactersUnavailable".Translate().ToString();
        private string Status(CharacterEntry entry)
        {
            Pawn pawn = entry.Pawn;
            if (pawn == null || pawn.Destroyed || PawnLife.Dead(pawn)) return "Lihuat_CharactersUnavailable".Translate();
            if (!manager.IsRegistered(entry)) return "Lihuat_CharactersUnboundShort".Translate();
            if (manager.IsStored(pawn)) return "Lihuat_CharactersStored".Translate();
            if (pawn.Spawned) return pawn.Map.Parent?.LabelCap ?? "Lihuat_CharactersOnMap".Translate();
            return (pawn.holdingOwner != null ? "Lihuat_CharactersInContainer" : "Lihuat_CharactersAway").Translate();
        }
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor; bool wrap = Text.WordWrap, enabled = GUI.enabled; Color color = GUI.color;
            try
            {
                Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, rect.width - 35, 34), "Lihuat_CharactersTitle".Translate());
                Text.Font = GameFont.Small; Text.WordWrap = true;
                Widgets.Label(new Rect(0, 38, rect.width, 48), "Lihuat_CharactersDescription".Translate());
                Controls(new Rect(0, 94, rect.width, 30));
                Rect list = new Rect(0, 168, rect.width, Mathf.Max(60f, rect.height - 205));
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(10, 132, rect.width * .4f, 26), "Lihuat_CharactersNameHeader".Translate());
                Widgets.Label(new Rect(rect.width * .47f, 132, 160, 26), "Lihuat_CharactersLocationHeader".Translate());
                Text.Font = GameFont.Small;
                if (rows.Length == 0) Widgets.Label(list.ContractedBy(12), "Lihuat_CharactersEmpty".Translate());
                else
                {
                    Rect content = new Rect(0, 0, list.width - 18, rows.Length * 66f);
                    Widgets.BeginScrollView(list, ref scroll, content);
                    try { for (int i = 0; i < rows.Length; i++) DrawRow(new Rect(0, i * 66f, content.width, 62), rows[i], i); }
                    finally { Widgets.EndScrollView(); }
                }
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0, rect.height - 28, rect.width, 26), "Lihuat_CharactersCount".Translate(manager?.Entries.Count ?? 0,
                    manager?.Entries.Count(entry => manager.IsStored(entry.Pawn)) ?? 0));
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.enabled = enabled; GUI.color = color; }
        }
        private void Controls(Rect rect)
        {
            Rect input = new Rect(rect.x, rect.y, Mathf.Max(160, rect.width - 342), 30);
            string next = Widgets.TextField(input, search);
            if (string.IsNullOrEmpty(next))
            {
                Color previous = GUI.color; GUI.color = Color.gray;
                Widgets.Label(input.ContractedBy(5, 0), "Lihuat_CharactersSearch".Translate()); GUI.color = previous;
            }
            if (next != search) { search = next; dirty = true; scroll = Vector2.zero; }
            string[] keys = { "Lihuat_CharactersAll", "Lihuat_CharactersOutside", "Lihuat_CharactersStored" };
            for (int i = 0; i < keys.Length; i++)
            {
                Rect button = new Rect(rect.xMax - 330 + i * 110, rect.y, 102, 30);
                bool clicked = Widgets.ButtonText(button, keys[i].Translate());
                if (filter == i) Widgets.DrawHighlightSelected(button);
                if (clicked) { filter = i; dirty = true; scroll = Vector2.zero; }
            }
        }
        private void DrawRow(Rect rect, CharacterEntry entry, int index)
        {
            if (index % 2 == 0) Widgets.DrawLightHighlight(rect);
            float locationX = rect.width * .47f;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(10, rect.y + 2, locationX - 20, 28), Name(entry));
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(10, rect.y + 31, locationX - 20, 25), entry.ProviderLabelKey.Translate().ToString()
                + (entry.Pawn?.def == null ? "" : " · " + entry.Pawn.def.LabelCap));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(locationX, rect.y, rect.width - locationX - 205, rect.height), Status(entry));
            Pawn pawn = entry.Pawn;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && pawn?.Spawned == true;
            if (Widgets.ButtonText(new Rect(rect.xMax - 196, rect.y + 14, 86, 32), "Lihuat_CharactersLocate".Translate()))
            { Close(false); CameraJumper.TryJumpAndSelect(pawn); }
            GUI.enabled = enabled && manager.IsRegistered(entry) && pawn != null && !pawn.Destroyed && !PawnLife.Dead(pawn)
                && (manager.IsStored(pawn) || pawn.Spawned);
            if (Widgets.ButtonText(new Rect(rect.xMax - 102, rect.y + 14, 94, 32),
                (manager.IsStored(pawn) ? "Lihuat_CharactersRelease" : "Lihuat_CharactersStore").Translate()))
            {
                if (manager.IsStored(pawn)) ReleaseMenu(entry);
                else Report(manager.Store(entry), "Lihuat_CharactersStoredMessage", entry);
                dirty = true;
            }
            GUI.enabled = enabled;
        }
        private void ReleaseMenu(CharacterEntry entry)
        {
            var choices = new List<FloatMenuOption>();
            foreach (Map map in Find.Maps)
            {
                if (!LihuatGame.Current.Domains.CanContain(entry.Pawn, map)) continue;
                Map destination = map;
                choices.Add(new FloatMenuOption(map.Parent?.LabelCap ?? "Lihuat_CharactersOnMap".Translate(), () => SelectCell(entry, destination)));
            }
            if (choices.Count == 0) { Messages.Message("Lihuat_CharactersNoMap".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new FloatMenu(choices));
        }
        private void SelectCell(CharacterEntry entry, Map map)
        {
            Close(false); CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            Messages.Message("Lihuat_CharactersChooseCell".Translate(Name(entry)), MessageTypeDefOf.NeutralEvent, false);
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
            {
                if (Find.CurrentMap != map) { Messages.Message("Lihuat_CharactersNoMap".Translate(), MessageTypeDefOf.RejectInput, false); return; }
                Report(manager.Release(entry, map, target.Cell), "Lihuat_CharactersReleasedMessage", entry);
            }, requiresCastedSelected: false);
        }
        private static void Report(AcceptanceReport result, string success, CharacterEntry entry)
            => Messages.Message(result ? success.Translate(Name(entry)) : result.Reason, result ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
    }
}
