using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class SerializedPawnEmitter
    {
        internal static DynamicMethod Build(MethodInfo original)
        {
            MethodInfo inspect = GeneratedIntegrity.Create(AccessTools.Method(typeof(SerializedPawnReconstruction), nameof(SerializedPawnReconstruction.Inspect)));
            DynamicMethod body = IndependentIl.Clone(original, rewrite: (il, opcode, operand) =>
            {
                if ((opcode != OpCodes.Call && opcode != OpCodes.Callvirt) || !(operand is MethodInfo called) || called.ReturnType != typeof(string)) return false;
                il.Emit(opcode, called); il.Emit(OpCodes.Call, inspect);
                return true;
            });
            DynamicMethod wrapper = IndependentIl.New(original);
            wrapper.InitLocals = true;
            ILGenerator emit = wrapper.GetILGenerator();
            LocalBuilder frame = emit.DeclareLocal(typeof(RegenerationFlow.SnapshotFrame));
            LocalBuilder result = emit.DeclareLocal(typeof(bool));
            emit.Emit(OpCodes.Ldarg_1); emit.Emit(OpCodes.Ldnull); emit.Emit(OpCodes.Stind_Ref);
            emit.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(SerializedPawnReconstruction), nameof(SerializedPawnReconstruction.Begin))));
            emit.Emit(OpCodes.Stloc, frame);
            emit.BeginExceptionBlock();
            emit.Emit(OpCodes.Ldarg_0); emit.Emit(OpCodes.Call, inspect); emit.Emit(OpCodes.Pop);
            emit.Emit(OpCodes.Ldarg_0); emit.Emit(OpCodes.Ldarg_1); emit.Emit(OpCodes.Call, body); emit.Emit(OpCodes.Stloc, result);
            emit.BeginCatchBlock(typeof(RegenerationDeniedException));
            emit.Emit(OpCodes.Pop); emit.Emit(OpCodes.Ldc_I4_0); emit.Emit(OpCodes.Stloc, result);
            emit.Emit(OpCodes.Ldarg_1); emit.Emit(OpCodes.Ldnull); emit.Emit(OpCodes.Stind_Ref);
            emit.BeginFinallyBlock();
            emit.Emit(OpCodes.Ldloc, result); emit.Emit(OpCodes.Ldarg_1); emit.Emit(OpCodes.Ldloc, frame);
            emit.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(SerializedPawnReconstruction), nameof(SerializedPawnReconstruction.End))));
            emit.Emit(OpCodes.And); emit.Emit(OpCodes.Stloc, result);
            emit.EndExceptionBlock();
            emit.Emit(OpCodes.Ldloc, result); emit.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
