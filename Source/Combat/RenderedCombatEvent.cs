using System;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Combat
{
    public abstract class RenderedCombatEvent : RenderedCombatEntity
    {
        public sealed override float CoreHealthCurrent => 0f;
        public sealed override float CoreHealthMaximum => 0f;
        public sealed override bool HasHealth => false;
        protected abstract object Controller { get; }
        protected abstract void EndEvent();
        public sealed override void Hit(DamageInfo damage, bool direct) { }
        public sealed override bool Retire(Pawn attacker)
        {
            object subject = Identity;
            if (subject == null || subject.GetType().IsValueType || LihuatGame.Current == null || !Alive) return false;
            if (subject is MapComponent || subject is GameComponent || subject is RimWorld.Planet.WorldComponent)
                throw new InvalidOperationException("An event requires its own state identity.");
            LibraryCapabilities.Require(LibraryCapability.Erasure);
            RetirementSubjects.Register(subject.GetType());
            SubjectCallbacks.InstallEffects(subject.GetType());
            EndEvent();
            if (Alive) return false;
            LihuatGame.Current.RetiredSubjects.Retire(subject);
            var cleanup = new SubjectCleanup(subject, false);
            cleanup.Run(Controller, subject);
            if (cleanup.Limited || cleanup.Failures > 0)
                Log.Warning("[LihuatLib.Combat] Event observer cleanup incomplete: limited=" + cleanup.Limited + " failures=" + cleanup.Failures);
            return true;
        }
    }
}
