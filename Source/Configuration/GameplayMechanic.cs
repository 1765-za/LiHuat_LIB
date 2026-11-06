using System;

namespace LihuatLib.Configuration
{
    [Flags]
    public enum GameplayMechanic
    {
        None = 0,
        GuaranteedHit = 1,
        TrueDamage = 8,
        PercentageDamage = 16,
        InstantMovement = 32,
        BlessedErasure = 64,
        StarShield = 128,
        ParryDeflection = 256,
        All = GuaranteedHit | TrueDamage | PercentageDamage | InstantMovement | BlessedErasure | StarShield | ParryDeflection
    }
}
