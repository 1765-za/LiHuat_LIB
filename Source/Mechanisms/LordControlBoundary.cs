using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI.Group;

namespace LihuatLib.Mechanisms
{
    internal static class LordControlBoundary
    {
        internal static void Install()
        {
            MethodInfo gate = AccessTools.Method(typeof(LordControlBoundary), nameof(Allows));
            NativeIlGuards.Guard(AccessTools.Method(typeof(Lord), nameof(Lord.AddPawn)), gate, 1, 0);
            NativeIlGuards.Guard(AccessTools.Method(typeof(Lord), "AddPawnInternal"), gate, 1, 0);
            NativeIlGuards.Guard(AccessTools.Method(typeof(Lord), nameof(Lord.CanAddPawn)), gate, 1, 0);
            FieldWriteProtection.Register(AccessTools.Method(typeof(LordControlBoundary), nameof(AllowsWrite)));
        }

        private static bool Allows(Pawn pawn, Lord lord) => LihuatGame.Current?.Lifecycle.AllowsLord(pawn, lord) != false;

        private static bool AllowsWrite(FieldInfo field, object target, object value)
            => field.DeclaringType != typeof(Pawn) || field.Name != nameof(Pawn.lord)
                || target is not Pawn pawn || value is not Lord lord || Allows(pawn, lord);
    }
}
