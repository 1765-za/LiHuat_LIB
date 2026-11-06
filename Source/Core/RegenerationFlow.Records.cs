using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Core
{
    public sealed partial class RegenerationFlow
    {
        private sealed class RecordOrigin
        {
            internal readonly string Root;
            internal RecordOrigin(string root) { Root = root; }
        }
        private readonly ConditionalWeakTable<object, RecordOrigin> recordOrigins = new();

        internal void ExposeRecord(object record, Pawn source, string identity)
        {
            if (Scribe.mode != LoadSaveMode.Saving && Scribe.mode != LoadSaveMode.LoadingVars) return;
            string root = recordOrigins.TryGetValue(record, out RecordOrigin origin) ? origin.Root
                : RecordRoot(source, identity);
            Scribe_Values.Look(ref root, "lihuatRegenerationRoot");
            if (Scribe.mode != LoadSaveMode.LoadingVars || string.IsNullOrEmpty(root)) return;
            recordOrigins.Remove(record);
            recordOrigins.Add(record, new RecordOrigin(root));
        }

        private string RecordRoot(Pawn source, string identity) => source != null ? RootFor(source) ?? source.ThingID
            : string.IsNullOrEmpty(identity) ? null : history.RootForBody(identity) ?? identity;

        internal bool BeginRecord(object record, Pawn source, string identity, RegenerationKind kind, out Scope scope)
        {
            scope = default;
            if (record == null) return true;
            if (!recordOrigins.TryGetValue(record, out RecordOrigin origin))
            {
                string root = RecordRoot(source, identity);
                if (root == null) return true;
                origin = recordOrigins.GetValue(record, _ => new RecordOrigin(root));
            }
            // A recovery record retains its lineage when its current body is replaced or cleared.
            return Enter(new RegenerationOrigin(source, origin.Root, kind), out scope);
        }
    }
}
