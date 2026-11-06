using System.Reflection;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    internal static class RuntimeAuthorityCalls
    {
        internal static bool Changes(MethodBase method) => method is ConstructorInfo
            ? method.DeclaringType == typeof(OperationScope) || method.DeclaringType == typeof(DeveloperExecution.Scope)
            : method.DeclaringType == typeof(InfrastructureAssemblies) && method.Name == nameof(InfrastructureAssemblies.Register)
                || method.DeclaringType == typeof(DeveloperExecution) && method.Name == "Run";
    }
}
