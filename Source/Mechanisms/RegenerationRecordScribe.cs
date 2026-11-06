using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RegenerationRecordScribe
    {
        private static readonly Dictionary<Type, RegenerationSource> sources = new();
        private static readonly HashSet<MethodInfo> installed = new();

        internal static void Register(RegenerationSource source)
        {
            source = source.CanonicalRecord;
            Type type = source.RecordType;
            if (type == null || !typeof(IExposable).IsAssignableFrom(type) || sources.ContainsKey(type)) return;
            sources.Add(type, source);
            MethodInfo method = type.GetInterfaceMap(typeof(IExposable)).TargetMethods[0];
            if (installed.Contains(method)) return;
            try
            {
                NativeIlGuards.RegisterTransform(method, "lihuat.lib.regeneration.record", 1500, body =>
                {
                    DynamicMethod wrapper = IndependentIl.New(method);
                    ILGenerator il = wrapper.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, body);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(RegenerationRecordScribe), nameof(Expose))));
                    il.Emit(OpCodes.Ret);
                    return wrapper;
                });
                installed.Add(method);
            }
            catch (Exception error)
            {
                sources.Remove(type);
                Log.Warning("[LihuatLib.Regeneration] Recovery lineage serialization was not installed for " + type + ": " + error.GetType().Name);
            }
        }

        private static void Expose(object record)
        {
            if (Scribe.mode != LoadSaveMode.Saving && Scribe.mode != LoadSaveMode.LoadingVars) return;
            if (sources.TryGetValue(record.GetType(), out RegenerationSource source))
                LihuatGame.Current?.Regeneration.ExposeRecord(record, source.Resolve(record), source.Identity(record));
        }
    }
}
