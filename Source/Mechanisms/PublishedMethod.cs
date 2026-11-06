using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class PublishedMethod
    {
        private static readonly ModuleBuilder module = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("LihuatPatchPublications"), AssemblyBuilderAccess.Run).DefineDynamicModule("Methods");
        private static readonly ConditionalWeakTable<MethodInfo, DynamicMethod> bodies = new();

        internal static MethodInfo Materialize(MethodInfo method)
        {
            if (method is not DynamicMethod body) return method;
            Type[] parameters = body.GetParameters().Select(p => p.ParameterType).ToArray();
            Type delegateType = Expression.GetDelegateType(parameters.Concat(new[] { body.ReturnType }).ToArray());
            TypeBuilder type = module.DefineType("Patch_" + Guid.NewGuid().ToString("N"), TypeAttributes.Public | TypeAttributes.Sealed);
            FieldBuilder target = type.DefineField("Target", delegateType, FieldAttributes.Private | FieldAttributes.Static);
            MethodBuilder invoke = type.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static, body.ReturnType, parameters);
            ILGenerator il = invoke.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, target);
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Callvirt, delegateType.GetMethod("Invoke"));
            il.Emit(OpCodes.Ret);
            Type published = type.CreateType();
            published.GetField("Target", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, body.CreateDelegate(delegateType));
            MethodInfo result = published.GetMethod("Invoke");
            bodies.Add(result, body);
            FieldReferenceReads.RecordCopy(result, body);
            return result;
        }

        internal static DynamicMethod Body(MethodBase method) => method is DynamicMethod dynamic ? dynamic
            : bodies.TryGetValue((MethodInfo)method, out DynamicMethod body) ? body
            : throw new ArgumentException("Method is not a published boundary.", nameof(method));
    }
}
