using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal sealed partial class LifeCallGraphReader
    {
        internal bool Limited { get; private set; }
        internal Exception Failure { get; private set; }
        internal delegate bool ScanCalls(MethodInfo method, byte[] body, Dictionary<MethodInfo, LifeCallKind> targets, out int indirect);

        internal bool ScanNormal(MethodInfo method, byte[] body, Dictionary<MethodInfo, LifeCallKind> targets, out int indirect)
        {
            targets.Clear(); indirect = 0;
            List<CodeInstruction> code = Read(method, body);
            if (code == null) return false;
            foreach (CodeInstruction line in code)
            {
                if (line.opcode == OpCodes.Calli) { indirect++; continue; }
                LifeCallKind kind = line.opcode == OpCodes.Call ? LifeCallKind.Direct
                    : line.opcode == OpCodes.Callvirt ? LifeCallKind.Virtual
                    : line.opcode == OpCodes.Ldftn || line.opcode == OpCodes.Ldvirtftn ? LifeCallKind.Delegate : 0;
                if (kind == 0 || line.operand is not MethodInfo target) continue;
                target = NativeIlGuards.Definition(target);
                targets.TryGetValue(target, out LifeCallKind previous);
                targets[target] = previous | kind;
            }
            return true;
        }

        internal List<CodeInstruction> Read(MethodInfo method, byte[] body)
        {
            Type[] types = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
            Type[] arguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            try
            {
                var result = LifeIlReader.Decode(body, (kind, token) => kind == OperandType.InlineMethod
                    ? Resolve(method.Module, token, types, arguments) : null);
                if (result == null)
                {
                    Limited = true;
                    Failure ??= new BadImageFormatException("Method IL could not be decoded.");
                }
                return result;
            }
            catch (Exception error) when (error is BadImageFormatException || error is EndOfStreamException || error is OverflowException)
            { Limited = true; Failure ??= error; return null; }
        }

        private MethodBase Resolve(Module module, int token, Type[] types, Type[] arguments)
        {
            try { return module.ResolveMethod(token, types, arguments); }
            catch (Exception error) when (LifeDiscoveryMetadata.ResolutionFailure(error) || error is ArgumentException)
            { Limited = true; Failure ??= error; return null; }
        }
    }
}
