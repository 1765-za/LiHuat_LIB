using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    public static class PrefixAdmission
    {
        private sealed class Entry
        {
            internal readonly Func<object, bool> Protects;
            internal readonly Dictionary<MethodInfo, MethodInfo> Calls = new Dictionary<MethodInfo, MethodInfo>();
            internal Entry(Func<object, bool> protects) { Protects = protects; }
        }
        private static readonly Dictionary<MethodBase, int> entries = new Dictionary<MethodBase, int>();
        private static readonly List<Entry> policies = new List<Entry>();
        private static readonly FieldInfo patchMethod = typeof(Harmony).Assembly.GetType("HarmonyLib.PatchCall", true)
            .GetField("method", BindingFlags.Instance | BindingFlags.NonPublic);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(MethodInfo method, Func<object, bool> protects)
        {
            LibraryCapabilities.Require(LibraryCapability.IlDispatch);
            LibraryCapabilities.RequirePublicationThread();
            if (!InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly()))
                throw new InvalidOperationException("Admission owner is not registered.");
            if (method == null || method.IsStatic || method.DeclaringType.IsValueType)
                throw new ArgumentException("Admission requires an instance entry with a reference subject.", nameof(method));
            if (protects == null) throw new ArgumentNullException(nameof(protects));
            method = NativeIlGuards.Definition(method);
            if (entries.ContainsKey(method)) throw new InvalidOperationException("Admission already registered: " + method);
            PrefixAdmissionEmission.Install();
            int id = policies.Count;
            policies.Add(new Entry(protects));
            entries.Add(method, id);
            try { PatchIntegrity.RefreshExisting(method); }
            catch { entries.Remove(method); policies.RemoveAt(id); throw; }
        }

        internal static void Prepare(IEnumerable prefixes, MethodBase original)
        {
            if (original is MethodInfo method) original = NativeIlGuards.Definition(method);
            if (!entries.TryGetValue(original, out int id)) return;
            Entry entry = policies[id];
            foreach (object call in prefixes)
            {
                MethodInfo prefix = MethodIdentity.Declaration((MethodInfo)patchMethod.GetValue(call));
                if (prefix.ReturnType != typeof(bool) || InfrastructureAssemblies.Contains(prefix.Module.Assembly) || entry.Calls.ContainsKey(prefix)) continue;
                entry.Calls[prefix] = PrefixAdmissionEmitter.Build(prefix, original.DeclaringType, id);
            }
        }

        internal static CodeInstruction Route(object config, CodeInstruction code)
        {
            if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo prefix)) return null;
            MethodBase original = PrefixAdmissionEmission.Original(config);
            if (original is MethodInfo method) original = NativeIlGuards.Definition(method);
            if (!entries.TryGetValue(original, out int id)
                || !policies[id].Calls.TryGetValue(MethodIdentity.Declaration(prefix), out MethodInfo call)) return null;
            return new CodeInstruction(code) { operand = call };
        }
        internal static bool Protects(int id, object subject) => policies[id].Protects(subject);
    }
}
