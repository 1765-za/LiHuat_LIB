using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicWriteNative
    {
        private sealed class Publication
        {
            internal readonly Delegate Root;
            internal readonly IntPtr Address;
            internal Publication(Delegate root, IntPtr address) { Root = root; Address = address; }
        }

        private static readonly ConditionalWeakTable<MethodInfo, Publication> publications = new();
        internal static IntPtr Publish(MethodBase method, IntPtr result)
        {
            if (!DynamicWriteCatalogue.Ready || DynamicWriteCatalogue.Building || result == IntPtr.Zero || method is not MethodInfo info) return result;
            info = MethodIdentity.Declaration(info);
            FieldInfo[] fields;
            bool authority;
            if (info is DynamicMethod dynamicMethod)
            {
                if (!DynamicWriteCatalogue.TryPolicy(dynamicMethod, out fields, out authority)) return result;
            }
            else
            {
                if (!GeneratedMethodCatalogue.TryGet(info, out var policy)) return result;
                fields = policy.Fields; authority = policy.Authority;
            }
            return publications.GetValue(info, original => Build(original is DynamicMethod ? original : GeneratedMethodBody.For(original),
                authority || fields.Any(GeneratedIntegrity.Owns))).Address;
        }

        private static Publication Build(MethodInfo original, bool ownsFields)
        {
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try
            {
                DynamicMethod gate = GeneratedWriteGate.Build(original, ownsFields);
                Delegate root = IndependentIl.Compile(gate);
                IntPtr address = RuntimeMethodAddress.Get(gate);
                return new Publication(root, address);
            }
            finally { DynamicWriteCatalogue.Building = previous; }
        }
    }
}
