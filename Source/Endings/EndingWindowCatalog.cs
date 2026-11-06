using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.Endings
{
    internal sealed class EndingWindowCatalog
    {
        private readonly Dictionary<Type, bool> terminal = new();
        private static readonly MethodInfo credits = AccessTools.Method(typeof(GameVictoryUtility), nameof(GameVictoryUtility.ShowCredits));

        internal bool IsTerminal(Window window)
        {
            if (window == null || window.doCloseX || window.closeOnCancel || window.closeOnAccept || !window.absorbInputAroundWindow) return false;
            Type type = window.GetType();
            if (type.Assembly == typeof(Window).Assembly || InfrastructureAssemblies.Contains(type.Assembly)) return false;
            if (!terminal.TryGetValue(type, out bool value)) terminal[type] = value = Analyze(type);
            return value;
        }

        private static bool Analyze(Type type)
        {
            if (GameEndingRequest.Constructor(type) == null) return false;
            var reader = new LifeIlReader();
            var calls = new LifeCallGraphReader();
            int remaining = 256 * 1024, count = 128;
            foreach (MethodInfo method in LifeDiscoveryMetadata.Methods(type,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!LifeIlReader.HasMetadataBody(method)) continue;
                byte[] body;
                try { body = reader.ReadBody(method); }
                catch (Exception error) when (LifeDiscoveryMetadata.ResolutionFailure(error)) { continue; }
                if (--count < 0 || (remaining -= body?.Length ?? 0) < 0) return false;
                var code = calls.Read(method, body)?.Where(line => line.opcode != OpCodes.Nop).ToArray();
                if (code == null) continue;
                for (int i = 2; i < code.Length; i++)
                    if (code[i].opcode == OpCodes.Call && Equals(code[i].operand, credits)
                        && code[i - 1].opcode == OpCodes.Ldc_R4 && code[i - 2].opcode == OpCodes.Ldc_I4_1) return true;
            }
            return false;
        }
    }
}
