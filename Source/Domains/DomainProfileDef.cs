using Verse;

namespace LihuatLib.Domains
{
    public enum DomainKind { Closed, Open, Mobile, Prototype, Simple }

    public sealed class DomainProfileDef : Def
    {
        public DomainKind kind;
        public float suppression;
        public float internalStrength;
        public float externalStrength;
        public float stability;
        public float shellRecoveryPerSecond = .02f;
        public bool Movable => kind == DomainKind.Mobile || kind == DomainKind.Simple;
        public bool Formal => kind == DomainKind.Closed || kind == DomainKind.Open;
        public bool HasShell => kind == DomainKind.Closed;
        public int ShellCapacity(int size) => UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(500000f * 625f / (size * size)), 1, 12500000);
        public static DomainProfileDef For(DomainKind kind) => DefDatabase<DomainProfileDef>.GetNamed("Lihuat_Domain" + kind);
    }
}
