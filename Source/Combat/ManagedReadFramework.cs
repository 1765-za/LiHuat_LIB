using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace LihuatLib.Combat
{
    internal static class ManagedReadFramework
    {
        internal static bool SpanType(Type type) => type?.IsGenericType == true && (type.GetGenericTypeDefinition() == typeof(Span<>)
            || type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>));

        internal static bool Call(MethodBase method, ManagedReadValue[] args, ManagedReadFrame frame, bool factory,
            out ManagedReadValue result, out bool known)
        {
            result = ManagedReadValue.Scalar(); known = true;
            Type owner = method.DeclaringType;
            Type returns = method is MethodInfo info ? info.ReturnType : owner;
            ManagedReadValue At(int i) => frame.Resolve(args[i]);
            bool Owned(int i) => At(i).Origin == ReadOrigin.Owned;
            bool Address(int i) => args[i].Address && args[i].Writable;
            if (method is ConstructorInfo)
            {
                if (SpanType(owner)) { result = At(0) with { Type = owner, Address = false, Writable = false }; return true; }
                if (owner == typeof(AesGcm)) { result = ManagedReadValue.Owned(owner); return true; }
                if (owner.IsGenericType && owner.GetGenericTypeDefinition() == typeof(Nullable<>)) { result = ManagedReadValue.Scalar(owner); return true; }
                if (factory && (owner == typeof(DynamicMethod) || typeof(Delegate).IsAssignableFrom(owner)))
                { result = ManagedReadValue.Owned(owner); return true; }
                known = false; return false;
            }
            result = ManagedReadValue.For(returns);
            if (SpanType(owner))
            {
                if (method.Name is "Slice" or "op_Implicit") { result = At(0) with { Type = returns, Address = false, Writable = false }; return true; }
                if (method.Name == "get_Length" || method.Name == "get_IsEmpty") return true;
                if (method.Name is "CopyTo" or "TryCopyTo") return Owned(1);
                if (method.Name is "Clear" or "Fill") return Owned(0);
                if (method.Name == "get_Item") { result = At(0) with { Type = returns.GetElementType(), Address = true, Writable = Owned(0) }; return true; }
                return false;
            }
            if (owner == typeof(MemoryExtensions) && method.Name == "AsSpan")
            { result = At(0) with { Type = returns }; return true; }
            if (owner == typeof(BinaryPrimitives))
            {
                if (method.Name.StartsWith("Read", StringComparison.Ordinal)) return true;
                if (method.Name.StartsWith("Write", StringComparison.Ordinal)) return Owned(0);
                return false;
            }
            if (owner == typeof(Convert))
            {
                if (method.Name == "TryFromBase64String")
                    return args.Length == 3 && Owned(1) && Address(2) && frame.Write(args[2], ManagedReadValue.Scalar(typeof(int)));
                if (method.Name == "FromBase64String") { result = ManagedReadValue.Owned(returns); return true; }
                if (method.Name == "ToBase64String") return true;
                if (method.Name.StartsWith("To", StringComparison.Ordinal) && returns.IsPrimitive && method.GetParameters().All(p => p.ParameterType.IsPrimitive)) return true;
                return false;
            }
            if (owner == typeof(AesGcm)) return method.Name == "Decrypt" && args.Length == 6 && Owned(0) && Owned(4)
                || method.Name == "Dispose" && Owned(0);
            if (owner == typeof(IDisposable) && method.Name == nameof(IDisposable.Dispose)) return At(0).Type == typeof(AesGcm) && Owned(0);
            if (owner == typeof(CryptographicOperations)) return method.Name == "ZeroMemory" && Owned(0)
                || method.Name == "FixedTimeEquals";
            if ((owner == typeof(HMACSHA256) || owner == typeof(SHA256) || owner == typeof(SHA512))
                && method.Name == "HashData" && returns == typeof(byte[])) { result = ManagedReadValue.Owned(returns); return true; }
            if (owner == typeof(string)) return method.Name is "IsNullOrEmpty" or "IsNullOrWhiteSpace" or "get_Length" or "get_Chars" or "Equals";
            if (owner == typeof(Math) || owner == typeof(MathF) || owner == typeof(BitConverter))
                return returns.IsPrimitive && method.GetParameters().All(p => !p.ParameterType.IsByRef && !p.ParameterType.IsPointer);
            if (owner.IsGenericType && owner.GetGenericTypeDefinition() == typeof(Nullable<>))
                return method.Name is "GetValueOrDefault" or "get_Value" or "get_HasValue";
            if (owner == typeof(Type) && method.Name == nameof(Type.GetTypeFromHandle)) return true;
            if (factory)
            {
                if ((owner == typeof(Type) || owner == typeof(MemberInfo)) && method.Name == "get_Module" && At(0).Type == typeof(Type)) return true;
                if (owner == typeof(DynamicMethod) && Owned(0) && method.Name is "GetILGenerator" or "CreateDelegate")
                { result = ManagedReadValue.Owned(returns); return true; }
                if (owner == typeof(MethodInfo) && At(0).Type == typeof(DynamicMethod) && Owned(0) && method.Name == "CreateDelegate")
                { result = ManagedReadValue.Owned(returns); return true; }
                if (owner == typeof(ILGenerator) && Owned(0) && method.Name is "Emit" or "DeclareLocal" or "DefineLabel" or "MarkLabel")
                { result = ManagedReadValue.Owned(returns); return true; }
            }
            known = false; return false;
        }
    }
}
