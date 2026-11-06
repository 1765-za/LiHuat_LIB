using LihuatLib.StarPaths;
using RimWorld;
using Verse;

namespace LihuatLib.Presentation
{
    public abstract class StatWorker_CombatReadout : StatWorker
    {
        protected abstract float Value(Pawn pawn);
        public override bool ShouldShowFor(StatRequest req) => req.Thing is Pawn pawn && Value(pawn) > 0f;
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true) => req.Thing is Pawn pawn ? Value(pawn) : 0f;
        public override string GetStatDrawEntryLabel(StatDef definition, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
            => value.ToString("0.##%");
        public override string ValueToStringFor(Thing thing) => (thing is Pawn pawn ? Value(pawn) : 0f).ToString("0.##%");
        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense) => string.Empty;
        public override string GetExplanationFinalizePart(StatRequest req, ToStringNumberSense numberSense, float finalVal) => string.Empty;
    }
    public sealed class StatWorker_GlobalDamageReduction : StatWorker_CombatReadout
    {
        public override bool ShouldShowFor(StatRequest req) => req.Thing is Pawn pawn
            && LihuatGame.Current?.StarPaths.Find(pawn)?.ProviderSettlesGlobal != true && Value(pawn) > 0f;
        protected override float Value(Pawn pawn)
        {
            var game = LihuatGame.Current;
            float astral = game?.AstralArts.DamageReduction(pawn) ?? 0f, stars = game?.StarPaths.GlobalReduction(pawn) ?? 0f;
            return 1f - (1f - astral) * (1f - stars);
        }
    }
    public abstract class StatWorker_DamageConversion : StatWorker_CombatReadout
    {
        protected abstract DamageChannel Channel { get; }
        protected override float Value(Pawn pawn)
        {
            var split = LihuatGame.Current?.StarPaths.Split(pawn, 1f) ?? default;
            return Channel == DamageChannel.Advanced ? split.Advanced : Channel == DamageChannel.Specialized ? split.Specialized : split.True;
        }
    }
    public sealed class StatWorker_AdvancedConversion : StatWorker_DamageConversion { protected override DamageChannel Channel => DamageChannel.Advanced; }
    public sealed class StatWorker_SpecializedConversion : StatWorker_DamageConversion { protected override DamageChannel Channel => DamageChannel.Specialized; }
    public sealed class StatWorker_TrueConversion : StatWorker_DamageConversion { protected override DamageChannel Channel => DamageChannel.True; }
}
