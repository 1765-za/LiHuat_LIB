using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RegenerationSupplyEmitter
    {
        internal static DynamicMethod Rewrite(MethodInfo caller, RegenerationSupplySite[] sites, int[] ids)
        {
            var wrappers = sites.Select((site, i) => Wrap(site.Factory, ids[i])).ToArray();
            var occurrences = new Dictionary<MethodInfo, int>();
            int changed = 0;
            DynamicMethod body = IndependentIl.Clone(caller, rewrite: (il, opcode, operand) =>
            {
                if (opcode != OpCodes.Call || !(operand is MethodInfo factory) || !sites.Any(s => s.Factory == factory)) return false;
                occurrences.TryGetValue(factory, out int occurrence); occurrences[factory] = occurrence + 1;
                int index = Array.FindIndex(sites, s => s.Factory == factory && s.Occurrence == occurrence);
                if (index < 0) return false;
                RegenerationSupplySite site = sites[index];
                il.Emit(site.SourceLocal ? OpCodes.Ldloc : OpCodes.Ldarg, (short)site.SourceIndex);
                il.Emit(OpCodes.Call, wrappers[index]); changed++; return true;
            });
            if (changed != sites.Length) throw new InvalidOperationException("Supply call sites were not fully emitted.");
            return body;
        }
        private static DynamicMethod Wrap(MethodInfo factory, int id)
        {
            Type[] parameters = IndependentIl.Parameters(factory);
            var wrapper = new DynamicMethod(Guid.NewGuid().ToString("N"), factory.ReturnType,
                parameters.Concat(new[] { typeof(Pawn) }).ToArray(), factory.Module, true) { InitLocals = true };
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder scope = il.DeclareLocal(typeof(RegenerationFlow.Scope));
            LocalBuilder result = factory.ReturnType == typeof(void) ? null : il.DeclareLocal(factory.ReturnType);
            Label done = il.DefineLabel();
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldc_I4, id); il.Emit(OpCodes.Ldarg, (short)parameters.Length); il.Emit(OpCodes.Ldloca, scope);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(RegenerationSupplyGuard), nameof(RegenerationSupplyGuard.Begin))));
            il.Emit(OpCodes.Brfalse, done);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, factory);
            if (result != null) il.Emit(OpCodes.Stloc, result);
            il.MarkLabel(done);
            il.BeginCatchBlock(typeof(RegenerationDeniedException));
            il.Emit(OpCodes.Pop);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloc, scope);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(RegenerationSupplyGuard), nameof(RegenerationSupplyGuard.End))));
            il.EndExceptionBlock();
            if (result != null) il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
