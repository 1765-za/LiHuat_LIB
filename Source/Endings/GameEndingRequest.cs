using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace LihuatLib.Endings
{
    public sealed class GameEndingRequest : IExposable
    {
        private string windowType;
        private string text;
        private SongDef song;
        private float delay;
        private Window window;
        private bool settled;

        public GameEndingRequest() { }
        internal GameEndingRequest(Window window) { this.window = window; windowType = window.GetType().AssemblyQualifiedName; }
        internal GameEndingRequest(string text, SongDef song, float delay) { this.text = text; this.song = song; this.delay = delay; }

        internal static ConstructorInfo Constructor(Type type) => type?.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(ctor => ctor.GetParameters().All(parameter => parameter.HasDefaultValue))
            .OrderBy(ctor => ctor.GetParameters().Length).FirstOrDefault();

        public void Resume(GameEndingChannel channel)
        {
            if (settled) return;
            settled = true;
            channel.Replay(() =>
            {
                if (windowType == null) GameVictoryUtility.ShowCredits(text, song, true, delay);
                else
                {
                    if (window == null)
                    {
                        ConstructorInfo ctor = Constructor(Type.GetType(windowType, true))
                            ?? throw new InvalidOperationException("The saved ending window cannot be reconstructed.");
                        window = (Window)ctor.Invoke(ctor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray());
                    }
                    Find.WindowStack.Add(window);
                }
            });
        }

        public void Discard(GameEndingChannel channel)
        {
            if (settled) return;
            settled = true;
            if (window == null) return;
            channel.Cleanup(window.PostClose);
            window = null;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref windowType, "windowType");
            Scribe_Values.Look(ref text, "text");
            Scribe_Defs.Look(ref song, "song");
            Scribe_Values.Look(ref delay, "delay");
        }
    }
}
