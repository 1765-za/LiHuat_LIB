using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifecycleEntryBoundary
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthPawn =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        internal static void Install()
        {
            Register(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)), DestructiveEntry.Kill);
            Register(AccessTools.Method(typeof(Pawn), nameof(Pawn.Destroy)), DestructiveEntry.Destroy);
            Register(AccessTools.Method(typeof(Pawn), nameof(Pawn.Discard)), DestructiveEntry.Discard);
            Register(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.SetDead)), DestructiveEntry.SetDead);
        }
        private static void Register(MethodInfo method, DestructiveEntry entry) => NativeIlGuards.RegisterLayer(method,
            typeof(LifecycleEntryBoundary).FullName, NativeIlGuards.ScopeOrder,
            body => Wrap(method, body, entry), false);

        private static DynamicMethod Wrap(MethodInfo method, MethodInfo body, DestructiveEntry entry)
        {
            DynamicMethod wrapper = IndependentIl.New(method);
            wrapper.InitLocals = true;
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder state = il.DeclareLocal(typeof(LifecycleExecution));
            Label done = il.DefineLabel();
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)entry);
            il.Emit(OpCodes.Ldloca, state);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(LifecycleEntryBoundary), nameof(Begin))));
            il.Emit(OpCodes.Brfalse, done);
            for (int i = 0; i < IndependentIl.Parameters(method).Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(LifecycleEntryBoundary), nameof(Complete))));
            il.MarkLabel(done);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(LifecycleEntryBoundary), nameof(End))));
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
        private static bool Begin(object receiver, DestructiveEntry entry, out LifecycleExecution state)
        {
            state = null;
            Pawn pawn = receiver as Pawn ?? (receiver is Pawn_HealthTracker health ? healthPawn(health) : null);
            if (pawn == null) return true;
            if (LihuatGame.Current?.Lifecycle.Before(pawn, entry) == false) return false;
            state = new LifecycleExecution(pawn, entry);
            return true;
        }
        private static void Complete(LifecycleExecution state)
        {
            if (state != null) LihuatGame.Current?.Lifecycle.After(state.Pawn, state.Entry);
        }
        private static void End(LifecycleExecution state) => state?.Dispose();
    }
}
