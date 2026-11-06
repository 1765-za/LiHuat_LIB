using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisCodec
    {
        private readonly Func<Guid, Module> module;
        private readonly Func<string, Assembly> assembly;
        private readonly LifeAnalysisSnapshots snapshots;
        private readonly Dictionary<string, Type> types = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Dictionary<Type, Dictionary<int, MemberInfo>> members = new Dictionary<Type, Dictionary<int, MemberInfo>>();
        internal readonly HashSet<Module> Modules = new HashSet<Module>();
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        public LifeAnalysisCodec(Func<Guid, Module> module, Func<string, Assembly> assembly, LifeAnalysisSnapshots snapshots = null)
        { this.module = module; this.assembly = assembly; this.snapshots = snapshots; }

        internal void Write(BinaryWriter writer, LifeAnalysisArtifact artifact)
        {
            LifeAnalysisResult result = artifact.Result;
            writer.Write(result.Unreadable); writer.Write(result.Limited); writer.Write(result.Retirement);
            writer.Write(result.Regeneration);
            writer.Write(result.Supplies.Length);
            foreach (RegenerationSupplySite site in result.Supplies)
            {
                Member(writer, site.Caller); Member(writer, site.Factory); writer.Write(site.Occurrence);
                writer.Write(site.SourceIndex); writer.Write(site.SourceLocal);
            }
            writer.Write(artifact.Inputs.Count);
            foreach (var input in artifact.Inputs)
            {
                Member(writer, input.Key);
                writer.Write(input.Value.Body?.Length ?? -1);
                if (input.Value.Body != null) writer.Write(input.Value.Body);
                writer.Write(input.Value.Locals.Length);
                foreach (Type local in input.Value.Locals) Type(writer, local);
            }
            writer.Write(result.Methods.Length);
            foreach (MethodInfo method in result.Methods) Member(writer, method);
            writer.Write(result.Dependencies.Length);
            foreach (LifeDependency dependency in result.Dependencies)
            { Fields(writer, dependency.Fields.ToArray()); Paths(writer, dependency.Receivers); }
            writer.Write(result.Sites.Length);
            foreach (LifeExecutionSite site in result.Sites)
            {
                Member(writer, site.Method); Member(writer, site.Operation); writer.Write(site.Occurrence);
                writer.Write(site.ReceiverArgument); Fields(writer, site.ReceiverFields); Paths(writer, site.Receivers);
                writer.Write(site.Indexed); writer.Write(site.StateOwned);
            }
            writer.Write(result.Deferred.Length);
            foreach (LifeDeferredSite site in result.Deferred)
            {
                Member(writer, site.Method); Member(writer, site.Operation); writer.Write(site.Occurrence);
                writer.Write(site.DelegateArgument); writer.Write(site.Owner.Index); writer.Write(site.Owner.Local);
                Type(writer, site.Owner.Type); Fields(writer, site.Owner.Fields);
            }
            writer.Write(result.Reconstruction);
            writer.Write(result.Availability.Length);
            foreach (var entry in result.Availability) { Member(writer, entry.Key); writer.Write(entry.Value); }
        }
        internal LifeAnalysisArtifact Read(BinaryReader reader)
        {
            var result = new LifeAnalysisResult { Unreadable = reader.ReadInt32(), Limited = reader.ReadInt32(), Retirement = reader.ReadInt32(), Regeneration = reader.ReadInt32(),
                FromCache = true };
            result.Supplies = new RegenerationSupplySite[Count(reader, 1024)];
            for (int i = 0; i < result.Supplies.Length; i++)
                result.Supplies[i] = new RegenerationSupplySite { Caller = (MethodInfo)Member(reader), Factory = (MethodInfo)Member(reader),
                    Occurrence = reader.ReadInt32(), SourceIndex = reader.ReadInt32(), SourceLocal = reader.ReadBoolean() };
            var inputs = new Dictionary<MethodInfo, LifeMethodSnapshot>();
            int count = Count(reader, 1024);
            for (int i = 0; i < count; i++)
            {
                MethodInfo method = (MethodInfo)Member(reader);
                int length = reader.ReadInt32();
                if (length < -1 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
                byte[] body = length < 0 ? null : reader.ReadBytes(length);
                if (body != null && body.Length != length) throw new EndOfStreamException();
                var locals = new Type[Count(reader, 1024)];
                for (int n = 0; n < locals.Length; n++) locals[n] = Type(reader);
                inputs.Add(method, snapshots?.Intern(method, body, locals) ?? new LifeMethodSnapshot(body, locals));
            }
            result.Methods = new MethodInfo[Count(reader, 1024)];
            for (int i = 0; i < result.Methods.Length; i++) result.Methods[i] = (MethodInfo)Member(reader);
            result.Dependencies = new LifeDependency[Count(reader, 8192)];
            for (int i = 0; i < result.Dependencies.Length; i++) result.Dependencies[i] = new LifeDependency(Fields(reader), Paths(reader));
            var il = new LifeIlReader(method => inputs.TryGetValue(method, out LifeMethodSnapshot input) ? input.Body : null,
                method => inputs.TryGetValue(method, out LifeMethodSnapshot input) ? input.Locals : System.Type.EmptyTypes,
                method => inputs.TryGetValue(method, out LifeMethodSnapshot input) ? input.Code(method) : null);
            result.Sites = new LifeExecutionSite[Count(reader, 8192)];
            for (int i = 0; i < result.Sites.Length; i++)
            {
                var site = new LifeExecutionSite { Method = (MethodInfo)Member(reader), Operation = Member(reader),
                    Occurrence = reader.ReadInt32(), ReceiverArgument = reader.ReadInt32(), ReceiverFields = Fields(reader),
                    Receivers = Paths(reader), Indexed = reader.ReadBoolean(), StateOwned = reader.ReadBoolean() };
                site.OriginalCode = il.Read(site.Method) ?? throw new InvalidDataException();
                result.Sites[i] = site;
            }
            result.Deferred = new LifeDeferredSite[Count(reader, 8192)];
            for (int i = 0; i < result.Deferred.Length; i++)
            {
                var site = new LifeDeferredSite { Method = (MethodInfo)Member(reader), Operation = Member(reader),
                    Occurrence = reader.ReadInt32(), DelegateArgument = reader.ReadInt32(),
                    Owner = new LifeValueSource { Index = reader.ReadInt32(), Local = reader.ReadBoolean(), Type = Type(reader), Fields = Fields(reader) } };
                site.OriginalCode = il.Read(site.Method) ?? throw new InvalidDataException();
                result.Deferred[i] = site;
            }
            result.Reconstruction = reader.ReadBoolean();
            result.Availability = new KeyValuePair<MethodInfo, bool>[Count(reader, 8192)];
            for (int i = 0; i < result.Availability.Length; i++)
                result.Availability[i] = new KeyValuePair<MethodInfo, bool>((MethodInfo)Member(reader), reader.ReadBoolean());
            return new LifeAnalysisArtifact(result, inputs);
        }
        private void Track(Type type)
        {
            Modules.Add(type.Module);
            if (type.HasElementType) Track(type.GetElementType());
            if (type.IsGenericType) foreach (Type argument in type.GetGenericArguments()) Track(argument);
        }
        private void Type(BinaryWriter writer, Type type)
        {
            if (type == null) throw new InvalidDataException();
            Track(type);
            if (type.IsGenericParameter)
            {
                writer.Write((byte)1);
                MemberInfo owner = (MemberInfo)type.DeclaringMethod ?? type.DeclaringType;
                writer.Write(owner.Module.ModuleVersionId.ToString("N")); writer.Write(owner.MetadataToken);
                writer.Write(type.GenericParameterPosition);
            }
            else if (type.ContainsGenericParameters && type.IsConstructedGenericType)
            {
                writer.Write((byte)2); Type(writer, type.GetGenericTypeDefinition());
                Type[] arguments = type.GetGenericArguments(); writer.Write(arguments.Length);
                foreach (Type argument in arguments) Type(writer, argument);
            }
            else if (type.ContainsGenericParameters && type.HasElementType)
            {
                writer.Write(type.IsByRef ? (byte)3 : type.IsPointer ? (byte)4 : (byte)5);
                if (type.IsArray) { writer.Write(type.IsSZArray); writer.Write(type.GetArrayRank()); }
                Type(writer, type.GetElementType());
            }
            else
            {
                if (type.AssemblyQualifiedName == null) throw new InvalidDataException();
                writer.Write((byte)0); writer.Write(type.AssemblyQualifiedName);
            }
        }
        private Type Type(BinaryReader reader)
        {
            byte kind = reader.ReadByte();
            if (kind == 1)
            {
                Module owner = module(Guid.ParseExact(reader.ReadString(), "N")) ?? throw new InvalidDataException();
                int token = reader.ReadInt32(), position = reader.ReadInt32();
                MemberInfo declaration = owner.ResolveMember(token);
                Type[] arguments = declaration is MethodInfo method ? method.GetGenericArguments()
                    : declaration is Type ownerType ? ownerType.GetGenericArguments() : throw new InvalidDataException();
                if ((uint)position >= (uint)arguments.Length) throw new InvalidDataException();
                return arguments[position];
            }
            if (kind == 2)
            {
                Type definition = Type(reader);
                var arguments = new Type[Count(reader, 32)];
                for (int i = 0; i < arguments.Length; i++) arguments[i] = Type(reader);
                return definition.MakeGenericType(arguments);
            }
            if (kind == 3) return Type(reader).MakeByRefType();
            if (kind == 4) return Type(reader).MakePointerType();
            if (kind == 5)
            {
                bool vector = reader.ReadBoolean(); int rank = reader.ReadInt32();
                Type element = Type(reader);
                if (rank < 1 || rank > 32 || vector && rank != 1) throw new InvalidDataException();
                return vector ? element.MakeArrayType() : element.MakeArrayType(rank);
            }
            if (kind != 0) throw new InvalidDataException();
            string name = reader.ReadString();
            if (!types.TryGetValue(name, out Type type))
                types.Add(name, type = System.Type.GetType(name, identity => assembly(identity.FullName), null, true));
            return type;
        }
        private void Member(BinaryWriter writer, MemberInfo member)
        {
            Modules.Add(member.Module);
            writer.Write(member.Module.ModuleVersionId.ToString("N")); writer.Write(member.MetadataToken);
            Type(writer, member.DeclaringType);
            if (member is MethodInfo method && method.IsGenericMethod && !method.IsGenericMethodDefinition)
            {
                Type[] arguments = method.GetGenericArguments(); writer.Write(arguments.Length);
                foreach (Type argument in arguments) Type(writer, argument);
            }
            else writer.Write(0);
        }
        private MemberInfo Member(BinaryReader reader)
        {
            Guid id = Guid.ParseExact(reader.ReadString(), "N"); int token = reader.ReadInt32(); Type declaring = Type(reader);
            int count = Count(reader, 32);
            Type[] arguments = count == 0 ? System.Type.EmptyTypes : new Type[count];
            for (int i = 0; i < arguments.Length; i++) arguments[i] = Type(reader);
            Module resolved = module(id) ?? throw new InvalidDataException();
            if (declaring.Module != resolved) throw new InvalidDataException();
            if (!members.TryGetValue(declaring, out Dictionary<int, MemberInfo> declared))
            {
                declared = new Dictionary<int, MemberInfo>();
                foreach (MemberInfo member in declaring.GetMembers(Members))
                    if (!declared.ContainsKey(member.MetadataToken)) declared.Add(member.MetadataToken, member);
                members.Add(declaring, declared);
            }
            if (!declared.TryGetValue(token, out MemberInfo resolvedMember)) throw new InvalidDataException();
            return arguments.Length > 0 ? ((MethodInfo)resolvedMember).MakeGenericMethod(arguments) : resolvedMember;
        }
        private void Fields(BinaryWriter writer, FieldInfo[] fields)
        { writer.Write(fields.Length); foreach (FieldInfo field in fields) Member(writer, field); }
        private FieldInfo[] Fields(BinaryReader reader)
        {
            var fields = new FieldInfo[Count(reader, 64)];
            for (int i = 0; i < fields.Length; i++) fields[i] = (FieldInfo)Member(reader);
            return fields;
        }
        private void Paths(BinaryWriter writer, LifeObjectPath[] paths)
        { writer.Write(paths.Length); foreach (LifeObjectPath path in paths) Path(writer, path); }
        private LifeObjectPath[] Paths(BinaryReader reader)
        {
            var paths = new LifeObjectPath[Count(reader, 32)];
            for (int i = 0; i < paths.Length; i++) paths[i] = Path(reader, 0);
            return paths;
        }
        private void Path(BinaryWriter writer, LifeObjectPath path)
        {
            if (path == LifeObjectPath.Recursive) writer.Write((byte)4);
            else if (path.Root == null) writer.Write((byte)0);
            else if (path.Root is FieldInfo field) { writer.Write((byte)1); Member(writer, field); }
            else if (path.Root is Tuple<MethodInfo, int> argument)
            { writer.Write((byte)2); Member(writer, argument.Item1); writer.Write(argument.Item2); }
            else if (path.Root is Tuple<MethodInfo, LifeObjectPath> indexed)
            { writer.Write((byte)3); Member(writer, indexed.Item1); Path(writer, indexed.Item2); }
            else throw new InvalidDataException();
            Fields(writer, path.Fields);
        }
        private LifeObjectPath Path(BinaryReader reader, int depth)
        {
            object root;
            switch (reader.ReadByte())
            {
                case 0: root = null; break;
                case 1: root = (FieldInfo)Member(reader); break;
                case 2: root = Tuple.Create((MethodInfo)Member(reader), reader.ReadInt32()); break;
                case 3: root = Tuple.Create((MethodInfo)Member(reader), Path(reader, depth + 1)); break;
                case 4: Fields(reader); return LifeObjectPath.Recursive;
                default: throw new InvalidDataException();
            }
            LifeObjectPath result = LifeObjectPath.Symbol(root);
            foreach (FieldInfo field in Fields(reader)) result = result.Append(field);
            return result;
        }
        internal static int Count(BinaryReader reader, int maximum)
        { int count = reader.ReadInt32(); if (count < 0 || count > maximum) throw new InvalidDataException(); return count; }
    }
}
