using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace LihuatLib.Mechanisms
{
    internal static class RuntimePublicationModules
    {
        private static readonly ConditionalWeakTable<AssemblyLoadContext, ModuleBuilder> modules = new();

        internal static ModuleBuilder For(MethodBase method, ModuleBuilder shared)
        {
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(method.Module.Assembly);
            if (context == AssemblyLoadContext.GetLoadContext(shared.Assembly)) return shared;
            return modules.GetValue(context, Create);
        }

        private static ModuleBuilder Create(AssemblyLoadContext context)
        {
            using (context.EnterContextualReflection())
                return AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LihuatPublications_" + Guid.NewGuid().ToString("N")),
                    AssemblyBuilderAccess.Run).DefineDynamicModule("Entries");
        }
    }
}
