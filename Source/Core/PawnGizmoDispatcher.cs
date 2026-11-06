using System.Collections.Generic;
using Verse;

namespace LihuatLib.Core
{
    internal static class PawnGizmoDispatcher
    {
        internal static IEnumerable<Gizmo> Add(Pawn pawn, IEnumerable<Gizmo> original)
        {
            if (LibraryCapabilities.Has(LibraryCapability.AstralArts)) original = Presentation.AstralGizmos.Add(pawn, original);
            if (LibraryCapabilities.Has(LibraryCapability.StarPaths)) original = Presentation.StarPathGizmos.Add(pawn, original);
            return original;
        }
    }
}
