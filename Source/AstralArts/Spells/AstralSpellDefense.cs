using LihuatLib.Core;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralSpellDefense
    {
        internal static DamageWorker.DamageResult Apply(DamageWorker worker, DamageInfo info, Thing target)
        {
            if (target is Pawn pawn && info.Def.harmsHealth && !info.Def.consideredHelpful && info.Amount > 0f && !OperationScope.MedicalFor(pawn))
            {
                float amount = LihuatGame.Current?.AstralArts.SpellRuntime.Absorb(pawn, info.Amount) ?? info.Amount;
                if (amount <= 0f) return new DamageWorker.DamageResult { deflected = true };
                info.SetAmount(amount);
            }
            return worker.Apply(info, target);
        }
    }
}
