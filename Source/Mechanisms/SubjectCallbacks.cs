using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    public static class SubjectCallbacks
    {
        private static readonly HashSet<MethodInfo> installed = new();
        private static readonly HashSet<Type> complete = new();
        private static readonly MethodInfo gate = typeof(SubjectCallbacks).GetMethod(nameof(Block), BindingFlags.Static | BindingFlags.NonPublic);
        public static long Blocked { get; private set; }
        public static int Installed => installed.Count;

        internal static void InstallEffects(Type type)
        {
            if (complete.Contains(type)) return;
            var reader = new LifeIlReader();
            var effects = new List<MethodInfo>();
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!Eligible(method) || method.Name == "ExposeData") continue;
                var body = reader.Read(method);
                if (method.ReturnType == typeof(void) || body?.Any(l => l.opcode == OpCodes.Stfld || l.opcode == OpCodes.Stsfld
                    || l.operand is MethodInfo call && call.ReturnType == typeof(void)) == true) effects.Add(method);
            }
            NativeInlineCallers.Prepare(effects);
            foreach (MethodInfo method in effects) Install(method);
            complete.Add(type);
        }

        internal static bool Eligible(MethodInfo method) => LifeIlReader.HasMetadataBody(method) && method.Name != "Finalize"
            && method.Name != nameof(IDisposable.Dispose) && !method.IsGenericMethod
            && !method.DeclaringType.IsGenericType && !method.DeclaringType.IsValueType
            && !method.ReturnType.IsByRef && !method.ReturnType.IsPointer && !method.GetParameters().Any(p => p.ParameterType.IsPointer);

        internal static void Install(MethodInfo method)
        {
            if (!Eligible(method) || installed.Contains(method)) return;
            NativeInlineCallers.Prepare(new[] { method });
            NativeIlGuards.RegisterTransform(method, typeof(SubjectCallbacks).FullName, NativeIlGuards.GateOrder + 210,
                next => ErasureCallbackIl.Build(method, next, gate, SubjectArgument));
            installed.Add(method);
        }

        private static bool SubjectArgument(ParameterInfo parameter)
        {
            Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
            return !parameter.IsOut && (type == typeof(object) || RetirementSubjects.Recognizes(type));
        }

        private static bool Block(object owner)
        {
            RetirementSubjects store = LihuatGame.Current?.RetiredSubjects;
            if (store == null || owner == null) return false;
            object subject = RetirementSubjects.Recognizes(owner.GetType()) ? owner : ErasureOwnership.ResolveSubject(owner);
            if (!store.Contains(subject)) return false;
            Blocked++;
            return true;
        }
    }
}
