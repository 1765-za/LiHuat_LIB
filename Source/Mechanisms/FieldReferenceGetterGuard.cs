using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.StarBlood;
using LihuatLib.Dispatching;

namespace LihuatLib.Mechanisms
{
    internal static class FieldReferenceGetterGuard
    {
        private static readonly HashSet<MethodInfo> installed = new();
        private static readonly DispatchMethod rewrite = new(AccessTools.Method(typeof(FieldReferenceGetterGuard), nameof(Rewrite)))
        { priority = int.MinValue, order = 100 };

        internal static void Discover(MethodInfo method)
        {
            DiscoverWithReader(method, new LifeIlReader());
        }

        internal static void DiscoverWithReader(MethodInfo method, LifeIlReader reader)
        {
            method = NativeIlGuards.Definition(method);
            if (installed.Contains(method) || !LifeIlReader.HasMetadataBody(method)) return;
            byte[] metadata = reader.ReadBody(method);
            if (metadata == null) return;
            bool returned = DynamicWriteCatalogue.AddressResult(method.ReturnType);
            var code = returned ? null : FieldReferenceConsumer.Read(method, metadata);
            bool reads = code != null && FieldReferenceReads.HasRead(code);
            if (!returned && !reads && !FieldReferenceConsumer.Required(method, code)) return;
            if (!reads && (!GeneratedMethodCatalogue.TryGet(method, out var policy, true, true) || !policy.Required)) return;
            bool retained = NativeIlGuards.Contains(method);
            try
            {
                if (!GenericMethodIdentity.Applies(method)) NativeIlGuards.RetainEntries(new[] { method });
                NativeDispatcher.Register(method, transpiler: rewrite);
                installed.Add(method);
            }
            catch (Exception failure)
            {
                try
                {
                    if (!retained && !GenericMethodIdentity.Applies(method)) NativeIlGuards.ReleaseRetained(method);
                }
                catch (Exception rollback) { throw new AggregateException("Field reference protection and rollback failed.", failure, rollback); }
                throw;
            }
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            FieldReferenceReads.Rewrite(code);
            var original = (MethodInfo)__originalMethod;
            foreach (CodeInstruction line in code)
            {
                if ((line.opcode == OpCodes.Ldflda || line.opcode == OpCodes.Ldsflda)
                    && line.operand is FieldInfo field && GeneratedMethodCatalogue.Sensitive(field)
                    && (DynamicWriteCatalogue.AddressResult(original.ReturnType)
                        || IndirectFieldMutation.Writes(code, _ => false, address: candidate => ReferenceEquals(candidate, line))))
                {
                    line.opcode = OpCodes.Call;
                    line.operand = GeneratedFieldWrites.Reference(field);
                }
                else if (line.operand is MethodInfo called && FieldReferenceConsumer.Special(called)
                    && FieldReferenceConsumer.Escapes(original, code, line, called)
                    && GeneratedMethodCatalogue.TryGet(called, out var policy, true) && policy.Required)
                    line.operand = GeneratedMethodBody.For(called);
                yield return line;
            }
        }
    }
}
