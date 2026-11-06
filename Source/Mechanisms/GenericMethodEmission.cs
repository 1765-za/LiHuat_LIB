using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal sealed class GenericMethodEmission
    {
        private readonly MethodInfo original;
        private readonly TypeBuilder owner;
        private readonly MethodBuilder method;
        private readonly List<Delegate> roots = new();
        private static readonly ConditionalWeakTable<Type, List<Delegate>> retained = new();
        internal ILGenerator IL => method.GetILGenerator();

        internal GenericMethodEmission(MethodInfo original, ModuleBuilder module)
        {
            this.original = original;
            owner = module.DefineType("GenericBody_" + Guid.NewGuid().ToString("N"), TypeAttributes.Public | TypeAttributes.Sealed);
            Type[] types = original.DeclaringType.IsGenericType ? original.DeclaringType.GetGenericArguments() : Type.EmptyTypes;
            if (types.Length != 0) Configure(owner.DefineGenericParameters(types.Select(t => t.Name).ToArray()), types);
            method = owner.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static);
            if (original.IsGenericMethod)
            {
                Type[] parameters = original.GetGenericArguments();
                Configure(method.DefineGenericParameters(parameters.Select(t => t.Name).ToArray()), parameters);
            }
            method.SetReturnType(original.ReturnType);
            method.SetParameters(IndependentIl.Parameters(original));
            method.SetImplementationFlags(MethodImplAttributes.NoInlining | MethodImplAttributes.IL);
            method.InitLocals = true;
        }

        private static void Configure(GenericTypeParameterBuilder[] output, Type[] input)
        {
            for (int i = 0; i < input.Length; i++)
            {
                output[i].SetGenericParameterAttributes(input[i].GenericParameterAttributes & ~GenericParameterAttributes.VarianceMask);
                Type[] constraints = input[i].GetGenericParameterConstraints();
                Type parent = constraints.FirstOrDefault(t => !t.IsInterface);
                if (parent != null) output[i].SetBaseTypeConstraint(parent);
                output[i].SetInterfaceConstraints(constraints.Where(t => t.IsInterface).ToArray());
            }
        }

        internal MethodInfo Complete()
        {
            Type type = owner.CreateType();
            if (roots.Count != 0) retained.Add(type, roots);
            if (type.IsGenericTypeDefinition) type = type.MakeGenericType(original.DeclaringType.GetGenericArguments());
            MethodInfo result = type.GetMethod("Invoke");
            if (result.IsGenericMethodDefinition) result = result.MakeGenericMethod(original.GetGenericArguments());
            return result;
        }

        internal bool EmitRuntimeCall(OpCode opcode, MethodBase called)
        {
            if (called is not DynamicMethod dynamic) return false;
            if (opcode != OpCodes.Call && opcode != OpCodes.Ldftn)
                throw new NotSupportedException("A dynamic continuation requires a static call or function pointer.");
            roots.Add(IndependentIl.Compile(dynamic));
            IL.Emit(OpCodes.Ldc_I8, RuntimeMethodAddress.Get(dynamic).ToInt64());
            IL.Emit(OpCodes.Conv_I);
            if (opcode == OpCodes.Call)
                IL.EmitCalli(OpCodes.Calli, CallingConventions.Standard, dynamic.ReturnType, IndependentIl.Parameters(dynamic), null);
            return true;
        }
    }
}
