using System.Runtime.CompilerServices;
using LihuatLib.AstralArts;
using Verse;

namespace LihuatLib.Presentation
{
    internal sealed class AstralGizmoState : GameComponent
    {
        private sealed class Expansion { internal bool Open; }
        private readonly ConditionalWeakTable<AstralEntry, Expansion> expanded = new ConditionalWeakTable<AstralEntry, Expansion>();
        public AstralGizmoState(Game game) { }
        internal bool IsExpanded(AstralEntry entry) => expanded.TryGetValue(entry, out Expansion state) && state.Open;
        internal void Toggle(AstralEntry entry)
        {
            Expansion state = expanded.GetValue(entry, _ => new Expansion());
            state.Open = !state.Open;
        }
    }
}
