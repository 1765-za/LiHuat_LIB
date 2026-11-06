using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace LihuatLib.AstralArts
{
    public sealed class JobDriver_AstralHeal : JobDriver
    {
        private int nextRepath;
        private IntVec3 lastAim = IntVec3.Invalid;
        private AstralEntry Entry => LihuatGame.Current?.AstralArts.Find(pawn);
        private Pawn Patient => job.targetA.Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override string GetReport() => "Lihuat_AstralHealJob".Translate();

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !AstralHealing.CanSelect(Entry, Patient));
            Toil approach = ToilMaker.MakeToil("AstralHealApproach");
            approach.defaultCompleteMode = ToilCompleteMode.Never;
            approach.initAction = MoveIntoRange;
            approach.tickAction = () =>
            {
                if (AstralHealing.CanCastFrom(pawn, Patient, pawn.Position))
                { pawn.pather.StopDead(); ReadyForNextToil(); return; }
                if (Find.TickManager.TicksGame >= nextRepath && (lastAim != Patient.Position || !pawn.pather.Moving)) MoveIntoRange();
            };
            approach.AddFinishAction(() => pawn.pather.StopDead());
            yield return approach;
            yield return Toils_General.Do(() =>
            {
                if (AstralHealing.Cast(Entry, Patient, job.count) <= 0f) EndJobWith(JobCondition.Incompletable);
            });
        }

        private void MoveIntoRange()
        {
            if (AstralHealing.CanCastFrom(pawn, Patient, pawn.Position)) { ReadyForNextToil(); return; }
            if (!AstralHealing.TryFindCastCell(Entry, Patient, out IntVec3 cell))
            { EndJobWith(JobCondition.Incompletable); return; }
            lastAim = Patient.Position;
            nextRepath = Find.TickManager.TicksGame + 15;
            pawn.pather.StartPath(cell, PathEndMode.OnCell);
        }
    }
}
