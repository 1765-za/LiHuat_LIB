using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using LihuatLib.Mechanisms;

namespace LihuatLib.Dispatching
{
    internal static class DispatchCompiler
    {
        internal static DynamicMethod Build(MethodBase target, MethodInfo body, IReadOnlyList<DispatchHandler> handlers)
        {
            DynamicMethod method = IndependentIl.New(target);
            Emit(target, body, handlers, method.GetILGenerator());
            return method;
        }

        internal static void Emit(MethodBase target, MethodInfo body, IReadOnlyList<DispatchHandler> handlers, ILGenerator il)
        {
            var args = new DispatchArguments(target, il, handlers);
            LocalBuilder captured = args.Exception == null ? null : il.DeclareLocal(typeof(ExceptionDispatchInfo));
            if (args.Exception != null) il.BeginExceptionBlock();
            foreach (DispatchHandler handler in handlers)
            {
                if (handler.Phase != DispatchPhase.Prefix) continue;
                bool affects = handler.Method.ReturnType != typeof(void) || handler.Method.GetParameters().Any(p => p.ParameterType.IsByRef);
                Label skip = il.DefineLabel();
                if (args.RunOriginal != null && affects) { il.Emit(OpCodes.Ldloc, args.RunOriginal); il.Emit(OpCodes.Brfalse, skip); }
                args.Call(handler);
                if (handler.Method.ReturnType == typeof(bool)) il.Emit(OpCodes.Stloc, args.RunOriginal);
                else if (handler.Method.ReturnType != typeof(void)) throw new ArgumentException("Prefix must return void or bool: " + handler.Method);
                il.MarkLabel(skip);
            }
            Label after = il.DefineLabel();
            if (args.RunOriginal != null) { il.Emit(OpCodes.Ldloc, args.RunOriginal); il.Emit(OpCodes.Brfalse, after); }
            Type[] parameters = IndependentIl.Parameters(target), continuation = IndependentIl.Parameters(body);
            if (!parameters.SequenceEqual(continuation) || IndependentIl.ResultType(target) != body.ReturnType)
                throw new ArgumentException("Dispatcher continuation signature differs: " + target);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, body);
            if (args.Result != null) il.Emit(OpCodes.Stloc, args.Result);
            il.MarkLabel(after);
            foreach (DispatchHandler handler in handlers)
            {
                if (handler.Phase != DispatchPhase.Postfix) continue;
                args.Call(handler);
                if (handler.Method.ReturnType == typeof(void)) continue;
                if (args.Result == null || handler.Method.ReturnType != args.Result.LocalType)
                    throw new ArgumentException("Postfix result type differs: " + handler.Method);
                il.Emit(OpCodes.Stloc, args.Result);
            }
            if (args.Exception != null)
            {
                il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Stloc, args.Exception);
                Capture(il, args.Exception, captured);
                il.EndExceptionBlock();
                foreach (DispatchHandler handler in handlers)
                {
                    if (handler.Phase != DispatchPhase.Finalizer) continue;
                    il.BeginExceptionBlock();
                    args.Call(handler);
                    if (handler.Method.ReturnType == typeof(Exception)) il.Emit(OpCodes.Stloc, args.Exception);
                    else if (handler.Method.ReturnType != typeof(void)) throw new ArgumentException("Finalizer must return void or Exception: " + handler.Method);
                    il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Stloc, args.Exception);
                    Capture(il, args.Exception, captured); il.EndExceptionBlock();
                }
                Label success = il.DefineLabel(), changed = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, args.Exception); il.Emit(OpCodes.Brfalse, success);
                il.Emit(OpCodes.Ldloc, captured); il.Emit(OpCodes.Brfalse, changed);
                il.Emit(OpCodes.Ldloc, captured); il.Emit(OpCodes.Callvirt, typeof(ExceptionDispatchInfo).GetProperty(nameof(ExceptionDispatchInfo.SourceException)).GetMethod);
                il.Emit(OpCodes.Ldloc, args.Exception); il.Emit(OpCodes.Bne_Un, changed);
                il.Emit(OpCodes.Ldloc, captured); il.Emit(OpCodes.Callvirt, typeof(ExceptionDispatchInfo).GetMethod(nameof(ExceptionDispatchInfo.Throw), Type.EmptyTypes));
                il.MarkLabel(changed);
                il.Emit(OpCodes.Ldloc, args.Exception); il.Emit(OpCodes.Call, typeof(ExceptionDispatchInfo).GetMethod(nameof(ExceptionDispatchInfo.Throw), new[] { typeof(Exception) }));
                il.MarkLabel(success);
            }
            if (args.Result != null) il.Emit(OpCodes.Ldloc, args.Result);
            il.Emit(OpCodes.Ret);
        }

        private static void Capture(ILGenerator il, LocalBuilder exception, LocalBuilder captured)
        {
            il.Emit(OpCodes.Ldloc, exception); il.Emit(OpCodes.Call, typeof(ExceptionDispatchInfo).GetMethod(nameof(ExceptionDispatchInfo.Capture)));
            il.Emit(OpCodes.Stloc, captured);
        }
    }
}
