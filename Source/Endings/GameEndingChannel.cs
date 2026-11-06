using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LihuatLib.Endings
{
    public sealed class GameEndingChannel
    {
        private sealed record Handler(string Id, int Order, Func<GameEndingRequest, bool> Receive);
        private readonly List<Handler> handlers = new();
        private readonly EndingWindowCatalog windows = new();
        private int replay, cleanup;

        public void Register(string id, int order, Func<GameEndingRequest, bool> receive)
        {
            handlers.RemoveAll(handler => handler.Id == id);
            handlers.Add(new Handler(id, order, receive));
            handlers.Sort((left, right) => left.Order != right.Order ? left.Order.CompareTo(right.Order)
                : StringComparer.Ordinal.Compare(left.Id, right.Id));
        }

        internal bool AllowWindow(Window window)
        {
            if (replay > 0 || handlers.Count == 0 || !windows.IsTerminal(window)) return true;
            if (cleanup > 0) return false;
            var request = new GameEndingRequest(window);
            return !Dispatch(request);
        }

        internal bool AllowCredits(string text, SongDef song, bool exit, float delay)
        {
            if (!exit || replay > 0 || handlers.Count == 0) return true;
            if (cleanup > 0) return false;
            return !Dispatch(new GameEndingRequest(text, song, delay));
        }

        private bool Dispatch(GameEndingRequest request)
        {
            foreach (Handler handler in handlers) if (handler.Receive(request)) return true;
            return false;
        }

        internal void Replay(Action action) { replay++; try { action(); } finally { replay--; } }
        internal void Cleanup(Action action)
        {
            Game game = Verse.Current.Game;
            bool screenshot = Find.UIRoot.screenshotMode.Active;
            cleanup++;
            try { action(); }
            catch (Exception error) { Log.Error("[LihuatLib.Endings] Ending presentation cleanup failed: " + error); }
            finally
            {
                cleanup--;
                if (Verse.Current.Game == game) Find.UIRoot.screenshotMode.Active = screenshot;
            }
        }
    }
}
