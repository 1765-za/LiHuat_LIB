using System;
using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Core
{
    public static class DeveloperExecution
    {
        private sealed class DeveloperWindow { }
        private sealed class Callback
        {
            private readonly Action action;
            internal Callback(Action action) => this.action = action;
            internal void Invoke() => Run(action);
        }
        private static readonly ConditionalWeakTable<Window, DeveloperWindow> windows = new ConditionalWeakTable<Window, DeveloperWindow>();
        [ThreadStatic] private static int depth;
        public static bool Active => depth > 0;
        public static bool Allows(Thing thing) => Active;

        public sealed class Scope : IDisposable
        {
            private readonly bool entered;
            private readonly LihuatGame game;
            private bool disposed;
            internal Scope()
            {
                if (!Prefs.DevMode && !Active) return;
                entered = true;
                if (depth++ != 0) return;
                game = LihuatGame.Current;
                try { game?.Lifecycle.DeveloperStarted(); }
                catch { depth--; throw; }
            }
            public void Dispose()
            {
                if (disposed || !entered) return;
                disposed = true;
                try
                {
                    if (depth == 1 && game == LihuatGame.Current)
                    {
                        game?.Lifecycle.DeveloperFinished();
                        game?.Subjects.AcceptDeveloperChanges();
                    }
                }
                finally { depth--; }
            }
        }

        internal static void Run(Action action) { using (new Scope()) action(); }
        internal static Action Wrap(Action action) => action == null || action.HasSingleTarget && action.Target is Callback ? action : new Callback(action).Invoke;
        internal static void Track(Window window)
        {
            if (Active && window != null) windows.GetValue(window, _ => new DeveloperWindow());
        }
        internal static Scope EnterWindow(Window window)
        {
            UnityEngine.Event input = UnityEngine.Event.current;
            if (input == null || input.type == UnityEngine.EventType.Layout || input.type == UnityEngine.EventType.Repaint) return null;
            return window != null && windows.TryGetValue(window, out _) ? new Scope() : null;
        }
    }
}
