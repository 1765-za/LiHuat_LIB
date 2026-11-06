using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace LihuatLib.Mechanisms
{
    internal static class PortableCallTargets
    {
        private sealed class Binding
        {
            internal readonly MethodInfo Method;
            internal readonly Type DelegateType;
            internal readonly bool Open;
            internal readonly Lazy<IntPtr> Address;
            internal Binding(MethodInfo method, Type delegateType)
            {
                Method = method; DelegateType = delegateType;
                Open = delegateType != null && method.IsStatic
                    && method.GetParameters().Length == delegateType.GetMethod("Invoke").GetParameters().Length;
                Address = new Lazy<IntPtr>(() => RuntimeMethodAddress.Get(Method));
            }
        }
        // Metadata IL cannot hold a DynamicMethod token for ldftn; the emitted ID owns its callable target.
        private static readonly ConcurrentDictionary<int, Binding> bindings = new();
        private static int nextId;

        internal static int Register(MethodInfo original, MethodInfo replacement, Type delegateType)
        {
            if (delegateType != null && !original.IsStatic && original.DeclaringType.IsValueType
                && replacement.IsStatic && replacement.GetParameters().Length == delegateType.GetMethod("Invoke").GetParameters().Length + 1)
            {
                Type[] arguments = original.GetParameters().Select(p => p.ParameterType).Prepend(typeof(object)).ToArray();
                var adapter = new DynamicMethod(Guid.NewGuid().ToString("N"), original.ReturnType, arguments, original.Module, true);
                ILGenerator body = adapter.GetILGenerator();
                body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Unbox, original.DeclaringType);
                for (int i = 1; i < arguments.Length; i++) body.Emit(OpCodes.Ldarg, (short)i);
                body.Emit(OpCodes.Call, replacement); body.Emit(OpCodes.Ret);
                replacement = adapter;
            }
            int id = Interlocked.Increment(ref nextId);
            bindings.TryAdd(id, new Binding(replacement, delegateType));
            return id;
        }
        internal static void Remove(int id) => bindings.TryRemove(id, out _);
        internal static Delegate Bind(object target, int id)
        {
            Binding binding = bindings[id];
            return binding.Open ? binding.Method.CreateDelegate(binding.DelegateType)
                : binding.Method.CreateDelegate(binding.DelegateType, target);
        }
        internal static IntPtr Address(int id) => bindings[id].Address.Value;

        internal static void LookupReceiver(ILGenerator il)
        {
            Label present = il.DefineLabel();
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brtrue, present); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Newobj, typeof(NullReferenceException).GetConstructor(Type.EmptyTypes)); il.Emit(OpCodes.Throw);
            il.MarkLabel(present); il.Emit(OpCodes.Pop);
        }
    }
}
