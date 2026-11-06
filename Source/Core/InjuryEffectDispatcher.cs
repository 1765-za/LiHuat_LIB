using System;
using Verse;

namespace LihuatLib.Core
{
    internal static class InjuryEffectDispatcher
    {
        internal static InjuryAdmission Admit(Pawn pawn, Hediff hediff, DamageInfo? evidence)
        {
            if (!(hediff is Hediff_Injury injury) || OperationScope.MedicalFor(pawn) || InjuryAdmission.Contains(injury)) return null;
            LihuatGame game = LihuatGame.Current;
            float astral = LibraryCapabilities.Has(LibraryCapability.AstralArts) ? game?.AstralArts.DamageReduction(pawn) ?? 0f : 0f;
            float starPath = LibraryCapabilities.Has(LibraryCapability.StarPaths) ? game?.StarPaths.InjuryReduction(pawn, injury) ?? 0f : 0f;
            bool prepared = game?.DamagePreparation.DirectInjury(pawn, injury, evidence) == true;
            if (!prepared && astral <= 0f && starPath <= 0f) return null;
            var scope = new InjuryAdmission(injury);
            try
            {
                AstralArts.AstralCombat.Incoming(injury, astral);
                StarPaths.StarPathCombat.Incoming(injury, starPath);
                return scope;
            }
            catch { scope.Dispose(); throw; }
        }
    }

    internal sealed class InjuryAdmission : IDisposable
    {
        [ThreadStatic] private static InjuryAdmission current;
        private readonly InjuryAdmission previous;
        private readonly Hediff_Injury injury;
        internal InjuryAdmission(Hediff_Injury injury) { this.injury = injury; previous = current; current = this; }
        internal static bool Contains(Hediff_Injury injury)
        {
            for (InjuryAdmission scope = current; scope != null; scope = scope.previous)
                if (scope.injury == injury) return true;
            return false;
        }
        public void Dispose() => current = previous;
    }
}
