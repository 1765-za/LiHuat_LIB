using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class GeneratedMethodBoundary
    {
        private sealed class ProtectedCopy
        {
            internal readonly DynamicMethod Method;
            internal ProtectedCopy(DynamicMethod method) => Method = method;
        }
        private static readonly ConditionalWeakTable<MethodInfo, ProtectedCopy> copies = new();

        internal static void Install()
        {
            PatchIntegrity.WrapperCommitted += _ => Invalidate();
            LifeMethodRevisions.Changed += _ => Invalidate();
            CilRewrite.Changed += _ => Invalidate();
            NativeIlGuards.FilterArguments(AccessTools.Method(typeof(Delegate), "CreateDelegateInternal"),
                AccessTools.Method(typeof(GeneratedMethodBoundary), nameof(Publish)), 1);
        }

        private static void Invalidate()
        {
            GeneratedMethodCatalogue.Invalidate();
            GeneratedMethodBody.Invalidate();
            ReferenceArgumentMutation.Invalidate();
            FieldReferenceReads.Invalidate();
            copies.Clear();
        }

        private static Delegate Publish(MethodInfo method, Delegate result)
        {
            method = MethodIdentity.Declaration(method);
            if (result != null && DynamicWriteCatalogue.Ready && !DynamicWriteCatalogue.Building
                && PawnCollectionCalls.Body(method) is DynamicMethod collection)
                return result.GetType().GetMethod("Invoke").GetParameters().Length == collection.GetParameters().Length
                    ? collection.CreateDelegate(result.GetType()) : collection.CreateDelegate(result.GetType(), result.Target);
            if (result == null || !GeneratedMethodCatalogue.TryGet(method, out var policy)) return result;
            DynamicMethod body = GeneratedMethodBody.For(method);
            int arguments = result.GetType().GetMethod("Invoke").GetParameters().Length;
            Delegate guarded = arguments == body.GetParameters().Length
                ? body.CreateDelegate(result.GetType()) : body.CreateDelegate(result.GetType(), result.Target);
            Delegate resultDelegate = policy.EntryRequired ? DynamicWriteDelegate.Protect(guarded, policy.Fields, policy.Authority) : guarded;
            FieldReferenceReads.Record(resultDelegate, method, result.Target);
            return resultDelegate;
        }

        internal static MethodInfo ProtectCopy(MethodInfo method)
        {
            if (method is DynamicMethod dynamicMethod)
            {
                if (!DynamicWriteCatalogue.TryPolicy(dynamicMethod, out FieldInfo[] fields, out bool authority)) return method;
                return copies.GetValue(method, _ => new ProtectedCopy(GeneratedWriteGate.Build(method,
                    authority || fields.Any(GeneratedIntegrity.Owns)))).Method;
            }
            method = MethodIdentity.Declaration(method);
            if (!GeneratedMethodCatalogue.TryGet(method, out var policy, true)) return method;
            return policy.EntryRequired ? copies.GetValue(method, _ => new ProtectedCopy(GeneratedWriteGate.Build(GeneratedMethodBody.For(method),
                    policy.Authority || policy.Fields.Any(GeneratedIntegrity.Owns)))).Method : GeneratedMethodBody.For(method);
        }

        internal static DynamicMethod InvocationBody(MethodInfo method) => PawnCollectionCalls.Body(method)
            ?? (GeneratedMethodCatalogue.TryGet(method, out _) ? GeneratedMethodBody.For(method) : null);

        internal static object Invoke(DynamicMethod body, MethodInfo method, object instance, BindingFlags flags, Binder binder,
            object[] arguments, System.Globalization.CultureInfo culture)
        {
            if (method.IsStatic) return body.Invoke(null, flags, binder, arguments, culture);
            object[] actual = new object[(arguments?.Length ?? 0) + 1];
            actual[0] = instance;
            if (arguments != null) Array.Copy(arguments, 0, actual, 1, arguments.Length);
            try { return body.Invoke(null, flags, binder, actual, culture); }
            finally { if (arguments != null) Array.Copy(actual, 1, arguments, 0, arguments.Length); }
        }

        internal static void RequireInvoke(MethodInfo method, object instance, object[] arguments)
        {
            if (!GeneratedMethodCatalogue.TryGet(method, out var generated) || !generated.EntryRequired) return;
            var policy = new DynamicWritePolicy(null, generated.Fields, generated.Authority);
            bool allowed = policy.Allows() && policy.AllowsTarget(instance);
            if (allowed && arguments != null)
                foreach (object argument in arguments) if (!policy.AllowsTarget(argument)) { allowed = false; break; }
            if (allowed) return;
            DynamicWritePolicy.Denied();
            throw new UnauthorizedAccessException("Generated mutation of protected state was rejected.");
        }
    }
}
