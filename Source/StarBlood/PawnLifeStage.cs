using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.StarBlood
{
    internal sealed class PawnLifeStage
    {
        private static readonly FieldInfo cachedField = AccessTools.Field(typeof(Pawn_AgeTracker), "cachedLifeStageIndex");
        private static readonly FieldInfo lockedField = AccessTools.Field(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.lockedLifeStageIndex));
        private static readonly AccessTools.FieldRef<Pawn_AgeTracker, int> cached = AccessTools.FieldRefAccess<Pawn_AgeTracker, int>("cachedLifeStageIndex");
        private readonly Pawn pawn;
        private readonly Pawn_AgeTracker tracker;
        private int knownStage, knownLock;

        internal PawnLifeStage(Pawn pawn)
        {
            this.pawn = pawn; tracker = pawn.ageTracker;
            if (tracker == null) return;
            knownStage = tracker.CurLifeStageIndex;
            knownLock = tracker.lockedLifeStageIndex;
        }

        private bool Valid(int index, bool locked) => index >= (locked ? -1 : 0) && index < pawn.RaceProps.lifeStageAges.Count;
        internal bool Intact => tracker == null || Valid(cached(tracker), false) && Valid(tracker.lockedLifeStageIndex, true);

        internal bool Rejects(FieldInfo field, object target, object value) => ReferenceEquals(target, tracker)
            && (field == cachedField || field == lockedField) && (!(value is int index) || !Valid(index, field == lockedField));

        internal bool Update()
        {
            if (tracker == null) return false;
            bool changed = false;
            if (Valid(tracker.lockedLifeStageIndex, true)) knownLock = tracker.lockedLifeStageIndex;
            else { tracker.lockedLifeStageIndex = knownLock; changed = true; }
            if (Valid(cached(tracker), false)) knownStage = cached(tracker);
            else { cached(tracker) = knownStage; changed = true; }
            return changed;
        }
    }
}
