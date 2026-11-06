using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeDiscoveryMetadata
    {
        private static readonly LifeMetadataDiagnostics diagnostics = new(Log.Warning);
        internal static int Unavailable => diagnostics.Unavailable;
        internal static IDisposable Batch() => diagnostics.BeginBatch();

        internal static Type[] Types(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error)
            {
                foreach (Exception loader in error.LoaderExceptions)
                    if (loader != null) Report(assembly.FullName, loader.Message, loader);
                return error.Types;
            }
            catch (Exception error) when (ResolutionFailure(error))
            {
                Report(assembly.FullName, "types", error);
                return Type.EmptyTypes;
            }
        }

        internal static IEnumerable<MethodInfo> Methods(Type type, BindingFlags flags)
        {
            MethodInfo[] methods;
            try { methods = type.GetMethods(flags); }
            catch (Exception error) when (ResolutionFailure(error))
            {
                Report(type, error);
                yield break;
            }
            foreach (MethodInfo method in methods)
            {
                bool resolved;
                try
                {
                    // Enumeration alone does not resolve a method's signature on CoreCLR.
                    _ = method.ReturnType;
                    foreach (ParameterInfo parameter in method.GetParameters()) _ = parameter.ParameterType;
                    resolved = true;
                }
                catch (Exception error) when (ResolutionFailure(error))
                {
                    Report(method, error);
                    resolved = false;
                }
                if (resolved) yield return method;
            }
        }

        internal static IEnumerable<FieldInfo> Fields(Type type, BindingFlags flags)
        {
            FieldInfo[] fields;
            try { fields = type.GetFields(flags); }
            catch (Exception error) when (ResolutionFailure(error))
            {
                Report(type, error);
                yield break;
            }
            foreach (FieldInfo field in fields)
            {
                bool resolved;
                try { _ = field.FieldType; resolved = true; }
                catch (Exception error) when (ResolutionFailure(error))
                {
                    Report(field, error);
                    resolved = false;
                }
                if (resolved) yield return field;
            }
        }

        internal static bool ResolutionFailure(Exception error) => error is TypeLoadException
            || error is ReflectionTypeLoadException || error is FileNotFoundException
            || error is FileLoadException || error is BadImageFormatException || error is MissingMemberException;

        internal static void Report(MemberInfo member, Exception error)
            => Report(member, error.GetType().Name + ": " + error.Message);

        internal static void Report(MemberInfo member, string reason)
        {
            string identity = member.Module.ModuleVersionId + ":0x" + member.MetadataToken.ToString("X8");
            Report(member.Module.Assembly.GetName().Name,
                (member.DeclaringType?.FullName ?? member.Name) + "." + member.Name + " [" + identity + "]", reason);
        }

        private static void Report(string assembly, string member, Exception error)
            => Report(assembly, member, error.GetType().Name + ": " + error.Message);

        private static void Report(string assembly, string member, string reason) => diagnostics.Report(assembly, member, reason);
    }
}
