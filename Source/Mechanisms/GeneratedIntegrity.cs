using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal sealed class DetectorGeneration
    {
        private readonly Dictionary<MethodInfo, DynamicMethod> methods = new Dictionary<MethodInfo, DynamicMethod>();
        private readonly List<int> masks = new List<int>();
        private readonly Random random;
        public readonly string Identity = Guid.NewGuid().ToString("N");
        public string Shape => Identity + ":" + string.Join(",", masks);
        public DetectorGeneration()
        {
            byte[] seed = new byte[4];
            using (RandomNumberGenerator entropy = RandomNumberGenerator.Create()) entropy.GetBytes(seed);
            random = new Random(BitConverter.ToInt32(seed, 0));
        }
        private int Mask() { int mask = random.Next(1, int.MaxValue); masks.Add(mask); return mask; }
        private DynamicMethod Method(Type result, Type[] parameters) => new DynamicMethod(Guid.NewGuid().ToString("N"), result, parameters, typeof(GeneratedIntegrity).Module, true);
        private void EncodeBoolean(ILGenerator il)
        {
            int mask = Mask(); il.Emit(OpCodes.Ldc_I4, mask); il.Emit(OpCodes.Xor); il.Emit(OpCodes.Ldc_I4, mask); il.Emit(OpCodes.Xor);
        }
        public Func<float, bool> Finite()
        {
            DynamicMethod method = Method(typeof(bool), new[] { typeof(float) }); ILGenerator il = method.GetILGenerator();
            int variant = random.Next(3); masks.Add(variant);
            if (variant == 0)
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldc_R4, 0f); il.Emit(OpCodes.Ceq);
            }
            else
            {
                float first = variant == 1 ? float.NegativeInfinity : float.PositiveInfinity;
                float second = variant == 1 ? float.PositiveInfinity : float.NegativeInfinity;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, first); il.Emit(variant == 1 ? OpCodes.Cgt : OpCodes.Clt);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, second); il.Emit(variant == 1 ? OpCodes.Clt : OpCodes.Cgt); il.Emit(OpCodes.And);
            }
            EncodeBoolean(il); il.Emit(OpCodes.Ret);
            return (Func<float, bool>)method.CreateDelegate(typeof(Func<float, bool>));
        }
        public Func<MemberInfo, bool> Ownership()
        {
            DynamicMethod method = Method(typeof(bool), new[] { typeof(MemberInfo) });
            ILGenerator il = method.GetILGenerator(); Label empty = il.DefineLabel();
            Label declared = il.DefineLabel(), assembly = il.DefineLabel();
            LocalBuilder type = il.DeclareLocal(typeof(Type));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, empty);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, typeof(Type)); il.Emit(OpCodes.Stloc, type);
            il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Brtrue, declared);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(MemberInfo), nameof(MemberInfo.DeclaringType)));
            il.Emit(OpCodes.Stloc, type); il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Brtrue, declared);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(MemberInfo), nameof(MemberInfo.Module)));
            il.Emit(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Module), nameof(Module.Assembly)));
            il.Emit(OpCodes.Br, assembly);
            il.MarkLabel(declared); il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Type), nameof(Type.Assembly)));
            il.MarkLabel(assembly);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(InfrastructureAssemblies), nameof(InfrastructureAssemblies.Contains)));
            EncodeBoolean(il); il.Emit(OpCodes.Ret);
            il.MarkLabel(empty); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<MemberInfo, bool>)method.CreateDelegate(typeof(Func<MemberInfo, bool>));
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public DynamicMethod Clone(MethodInfo source)
        {
            CodePublicationAuthority.RequireCaller();
            CodePublicationAuthority.RequireBlueprint(source);
            if (methods.TryGetValue(source, out DynamicMethod existing)) return existing;
            ParameterInfo[] parameters = source.GetParameters();
            MethodBody body = NativeRuntime.SourceBody(source) ?? throw new InvalidOperationException("Missing detector IL: " + source);
            if (!source.IsStatic) throw new ArgumentException("Detector blueprints must be static.", nameof(source));
            DynamicMethod method = Method(source.ReturnType, parameters.Select(p => p.ParameterType).ToArray());
            method.InitLocals = body.InitLocals;
            for (int i = 0; i < parameters.Length; i++) method.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
            methods.Add(source, method);
            CodePublicationAuthority.Record(method);
            if (!PortableIlEmitter.TryEmit(source, body, CilRewrite.ReadSource(source, body), method.GetILGenerator(), out string reason,
                (called, virtualCall) => (called.DeclaringType == typeof(PatchIntegrity) || called.DeclaringType == source.DeclaringType)
                    && called.IsStatic && !called.ContainsGenericParameters && called.GetMethodBody() != null
                    ? Clone(called) : called, Rewrite)) throw new InvalidOperationException("Detector generation failed: " + reason);
            return method;
        }
        private bool Rewrite(ILGenerator il, OpCode opcode, object operand)
        {
            int value;
            if (opcode == OpCodes.Ldc_I4 || opcode == OpCodes.Ldc_I4_S) value = Convert.ToInt32(operand);
            else if (opcode == OpCodes.Ldc_I4_M1) value = -1;
            else if (opcode.Value >= OpCodes.Ldc_I4_0.Value && opcode.Value <= OpCodes.Ldc_I4_8.Value) value = opcode.Value - OpCodes.Ldc_I4_0.Value;
            else return false;
            int mask = Mask();
            il.Emit(OpCodes.Ldc_I4, value ^ mask); il.Emit(OpCodes.Ldc_I4, mask); il.Emit(OpCodes.Xor); return true;
        }
    }

    public static class GeneratedIntegrity
    {
        private static readonly DetectorGeneration generation = new DetectorGeneration();
        public static readonly Func<float, bool> Finite = generation.Finite();
        public static readonly Func<MemberInfo, bool> Owns = generation.Ownership();
        public static string Shape => generation.Shape;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static DynamicMethod Create(MethodInfo blueprint)
        {
            CodePublicationAuthority.RequireCaller();
            return generation.Clone(blueprint);
        }
    }
}
