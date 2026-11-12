using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class ErasureCallbacks
    {
        private static readonly HashSet<MethodInfo> installed = new HashSet<MethodInfo>();
        private static readonly HashSet<Type> types = new HashSet<Type>();
        public static long Blocked { get; private set; }
        public static long Rejected { get; private set; }
        public static int Installed => installed.Count;

        internal static void Install(Type type)
        {
            if (!LibraryCapabilities.Has(LibraryCapability.Erasure)) return;
            if (!types.Add(type)) return;
            for (Type current = type; LifeResourceEvidence.IsCustom(current); current = current.BaseType)
                foreach (MethodInfo method in current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) Install(method);
        }

        internal static void Install(MethodInfo method)
        {
            method = MethodIdentity.Declaration(method);
            if (!LibraryCapabilities.Has(LibraryCapability.Erasure)) return;
            if (!LifeIlReader.HasMetadataBody(method) || installed.Contains(method) || !LifeResourceEvidence.IsCustom(method.DeclaringType)
                || method.IsGenericMethod || method.DeclaringType.IsGenericType || method.DeclaringType.IsValueType
                || method.ReturnType.IsByRef || method.ReturnType.IsPointer || method.Name == "ExposeData"
                || method.IsStatic && !method.GetParameters().Any(ErasureCallbackIl.TargetArgument)) return;
            try
            {
                if (method.GetMethodBody() == null) return;
                NativeIlGuards.RegisterTransform(method, typeof(ErasureCallbacks).FullName, NativeIlGuards.GateOrder + 200,
                    body => ErasureCallbackIl.Build(method, body));
                installed.Add(method);
            }
            catch (Exception) { Rejected++; }
        }

        internal static bool BlockOwner(object owner) => BlockPawn(ErasureObservers.UniqueOwner(owner));
        internal static bool BlockPawn(Pawn pawn)
        {
            if (!ErasureObservers.Contains(pawn)) return false;
            Blocked++;
            return true;
        }

    }
}
