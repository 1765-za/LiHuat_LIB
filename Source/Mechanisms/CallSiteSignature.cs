using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace LihuatLib.Mechanisms
{
    /// <summary>
    /// Parses an ECMA-335 standalone method signature used by calli. Reflection's
    /// Module.ResolveSignature returns the raw blob, while ILGenerator requires a
    /// reconstructed managed or unmanaged calling convention and concrete Types.
    /// </summary>
    internal sealed class CallSiteSignature
    {
        private const byte CallConvMask = 0x0f;
        private const byte CallConvGeneric = 0x10;
        private const byte CallConvHasThis = 0x20;
        private const byte CallConvExplicitThis = 0x40;

        private readonly bool unmanaged;
        private readonly CallingConventions managedCallingConvention;
        private readonly CallingConvention unmanagedCallingConvention;
        private readonly Type returnType;
        private readonly Type[] requiredParameters;
        private readonly Type[] optionalParameters;

        private CallSiteSignature(
            bool unmanaged,
            CallingConventions managedCallingConvention,
            CallingConvention unmanagedCallingConvention,
            Type returnType,
            Type[] requiredParameters,
            Type[] optionalParameters)
        {
            this.unmanaged = unmanaged;
            this.managedCallingConvention = managedCallingConvention;
            this.unmanagedCallingConvention = unmanagedCallingConvention;
            this.returnType = returnType;
            this.requiredParameters = requiredParameters ?? Type.EmptyTypes;
            this.optionalParameters = optionalParameters ?? Type.EmptyTypes;
        }

        public static CallSiteSignature Parse(
            Module module,
            byte[] signature,
            Type[] typeArguments,
            Type[] methodArguments)
        {
            if (module == null || signature == null || signature.Length == 0)
            {
                throw new BadImageFormatException("standalone call-site signature is empty");
            }

            var reader = new SignatureReader(
                module,
                signature,
                typeArguments ?? Type.EmptyTypes,
                methodArguments ?? Type.EmptyTypes);
            CallSiteSignature parsed = reader.ReadCallSite();
            if (!reader.AtEnd)
            {
                throw new BadImageFormatException("standalone call-site signature has trailing data");
            }

            return parsed;
        }

        public void Emit(ILGenerator generator, OpCode opcode)
        {
            if (generator == null)
            {
                throw new ArgumentNullException(nameof(generator));
            }

            if (unmanaged)
            {
                if (optionalParameters.Length != 0)
                {
                    throw new NotSupportedException("unmanaged vararg calli signatures are not supported");
                }

                generator.EmitCalli(
                    opcode,
                    unmanagedCallingConvention,
                    returnType,
                    requiredParameters);
                return;
            }

            generator.EmitCalli(
                opcode,
                managedCallingConvention,
                returnType,
                requiredParameters,
                optionalParameters.Length == 0 ? null : optionalParameters);
        }

        private sealed class SignatureReader
        {
            private readonly Module module;
            private readonly byte[] data;
            private readonly Type[] typeArguments;
            private readonly Type[] methodArguments;
            private int offset;

            public SignatureReader(
                Module module,
                byte[] data,
                Type[] typeArguments,
                Type[] methodArguments)
            {
                this.module = module;
                this.data = data;
                this.typeArguments = typeArguments;
                this.methodArguments = methodArguments;
            }

            public bool AtEnd => offset == data.Length;

            public CallSiteSignature ReadCallSite()
            {
                byte header = ReadByte();
                byte baseConvention = (byte)(header & CallConvMask);
                if ((header & CallConvGeneric) != 0)
                {
                    ReadCompressedUInt();
                }

                int parameterCount = checked((int)ReadCompressedUInt());
                Type returnType = ReadType(allowVoid: true);
                var required = new List<Type>(parameterCount);
                var optional = new List<Type>();
                bool afterSentinel = false;
                int parametersRead = 0;
                while (parametersRead < parameterCount)
                {
                    if (PeekByte() == 0x41)
                    {
                        if (afterSentinel)
                        {
                            throw new BadImageFormatException("call-site signature contains multiple sentinels");
                        }

                        ReadByte();
                        afterSentinel = true;
                        continue;
                    }

                    Type parameter = ReadType(allowVoid: false);
                    (afterSentinel ? optional : required).Add(parameter);
                    parametersRead++;
                }

                if (offset < data.Length && PeekByte() == 0x41)
                {
                    throw new BadImageFormatException("call-site sentinel has no optional parameter");
                }

                bool hasThis = (header & CallConvHasThis) != 0;
                bool explicitThis = (header & CallConvExplicitThis) != 0;
                switch (baseConvention)
                {
                    case 0x00:
                    case 0x05:
                        CallingConventions managed = baseConvention == 0x05
                            ? CallingConventions.VarArgs
                            : CallingConventions.Standard;
                        if (hasThis)
                        {
                            managed |= CallingConventions.HasThis;
                        }

                        if (explicitThis)
                        {
                            managed |= CallingConventions.ExplicitThis;
                        }

                        if (optional.Count != 0 && baseConvention != 0x05)
                        {
                            throw new BadImageFormatException("optional calli parameters require a vararg signature");
                        }

                        return new CallSiteSignature(
                            false,
                            managed,
                            default(CallingConvention),
                            returnType,
                            required.ToArray(),
                            optional.ToArray());
                    case 0x01:
                    case 0x02:
                    case 0x03:
                    case 0x04:
                    case 0x09:
                        if (hasThis || explicitThis || optional.Count != 0)
                        {
                            throw new NotSupportedException("unsupported unmanaged calli signature flags");
                        }

                        return new CallSiteSignature(
                            true,
                            default(CallingConventions),
                            MapUnmanagedConvention(baseConvention),
                            returnType,
                            required.ToArray(),
                            Type.EmptyTypes);
                    case 0x0b:
                        throw new NotSupportedException("native vararg calli signatures are not supported");
                    default:
                        throw new NotSupportedException(
                            "unsupported standalone call-site convention 0x" + baseConvention.ToString("X2"));
                }
            }

            private Type ReadType(bool allowVoid)
            {
                while (true)
                {
                    byte modifier = PeekByte();
                    if (modifier == 0x1f || modifier == 0x20)
                    {
                        ReadByte();
                        ResolveTypeDefOrRef(ReadCompressedUInt());
                        continue;
                    }

                    if (modifier == 0x45)
                    {
                        ReadByte();
                        continue;
                    }

                    break;
                }

                byte elementType = ReadByte();
                switch (elementType)
                {
                    case 0x01:
                        if (!allowVoid)
                        {
                            throw new BadImageFormatException("void is not a valid calli parameter type");
                        }

                        return typeof(void);
                    case 0x02: return typeof(bool);
                    case 0x03: return typeof(char);
                    case 0x04: return typeof(sbyte);
                    case 0x05: return typeof(byte);
                    case 0x06: return typeof(short);
                    case 0x07: return typeof(ushort);
                    case 0x08: return typeof(int);
                    case 0x09: return typeof(uint);
                    case 0x0a: return typeof(long);
                    case 0x0b: return typeof(ulong);
                    case 0x0c: return typeof(float);
                    case 0x0d: return typeof(double);
                    case 0x0e: return typeof(string);
                    case 0x0f: return ReadType(false).MakePointerType();
                    case 0x10: return ReadType(false).MakeByRefType();
                    case 0x11:
                    case 0x12:
                        return ResolveTypeDefOrRef(ReadCompressedUInt());
                    case 0x13:
                        return ResolveGenericArgument(typeArguments, "type");
                    case 0x14:
                        return ReadArrayType();
                    case 0x15:
                        return ReadGenericInstanceType();
                    case 0x16: return typeof(TypedReference);
                    case 0x18: return typeof(IntPtr);
                    case 0x19: return typeof(UIntPtr);
                    case 0x1b:
                        ReadCallSite();
                        return typeof(IntPtr);
                    case 0x1c: return typeof(object);
                    case 0x1d: return ReadType(false).MakeArrayType();
                    case 0x1e:
                        return ResolveGenericArgument(methodArguments, "method");
                    default:
                        throw new NotSupportedException(
                            "unsupported calli element type 0x" + elementType.ToString("X2"));
                }
            }

            private Type ReadArrayType()
            {
                Type element = ReadType(false);
                int rank = checked((int)ReadCompressedUInt());
                if (rank <= 0)
                {
                    throw new BadImageFormatException("array calli signature has an invalid rank");
                }

                int sizeCount = checked((int)ReadCompressedUInt());
                for (int i = 0; i < sizeCount; i++)
                {
                    ReadCompressedUInt();
                }

                int lowerBoundCount = checked((int)ReadCompressedUInt());
                for (int i = 0; i < lowerBoundCount; i++)
                {
                    ReadCompressedUInt();
                }

                return element.MakeArrayType(rank);
            }

            private Type ReadGenericInstanceType()
            {
                byte kind = ReadByte();
                if (kind != 0x11 && kind != 0x12)
                {
                    throw new BadImageFormatException("generic calli instance is missing class/value-type marker");
                }

                Type definition = ResolveTypeDefOrRef(ReadCompressedUInt());
                int count = checked((int)ReadCompressedUInt());
                Type[] arguments = new Type[count];
                for (int i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = ReadType(false);
                }

                return definition.MakeGenericType(arguments);
            }

            private Type ResolveGenericArgument(Type[] arguments, string kind)
            {
                int index = checked((int)ReadCompressedUInt());
                if (index < 0 || index >= arguments.Length)
                {
                    throw new BadImageFormatException(
                        "calli " + kind + " generic argument index is out of range: " + index);
                }

                return arguments[index];
            }

            private Type ResolveTypeDefOrRef(uint codedToken)
            {
                uint rid = codedToken >> 2;
                uint tokenType;
                switch (codedToken & 0x03)
                {
                    case 0:
                        tokenType = 0x02000000;
                        break;
                    case 1:
                        tokenType = 0x01000000;
                        break;
                    case 2:
                        tokenType = 0x1b000000;
                        break;
                    default:
                        throw new BadImageFormatException("invalid TypeDefOrRef coded token");
                }

                if (rid == 0 || rid > 0x00ffffff)
                {
                    throw new BadImageFormatException("invalid TypeDefOrRef row id");
                }

                return module.ResolveType(
                    unchecked((int)(tokenType | rid)),
                    typeArguments,
                    methodArguments);
            }

            private byte PeekByte()
            {
                if (offset >= data.Length)
                {
                    throw new BadImageFormatException("truncated standalone call-site signature");
                }

                return data[offset];
            }

            private byte ReadByte()
            {
                byte value = PeekByte();
                offset++;
                return value;
            }

            private uint ReadCompressedUInt()
            {
                byte first = ReadByte();
                if ((first & 0x80) == 0)
                {
                    return first;
                }

                if ((first & 0xc0) == 0x80)
                {
                    return (uint)(((first & 0x3f) << 8) | ReadByte());
                }

                if ((first & 0xe0) == 0xc0)
                {
                    return (uint)(((first & 0x1f) << 24)
                        | (ReadByte() << 16)
                        | (ReadByte() << 8)
                        | ReadByte());
                }

                throw new BadImageFormatException("invalid compressed integer in call-site signature");
            }
        }

        private static CallingConvention MapUnmanagedConvention(byte convention)
        {
            switch (convention)
            {
                case 0x01: return CallingConvention.Cdecl;
                case 0x02: return CallingConvention.StdCall;
                case 0x03: return CallingConvention.ThisCall;
                case 0x04: return CallingConvention.FastCall;
                case 0x09: return CallingConvention.Winapi;
                default:
                    throw new NotSupportedException(
                        "unsupported unmanaged calli convention 0x" + convention.ToString("X2"));
            }
        }
    }
}
