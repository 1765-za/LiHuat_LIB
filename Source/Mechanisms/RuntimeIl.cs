using System.Reflection;

namespace LihuatLib.Mechanisms
{
    public static class RuntimeIl
    {
        public static bool Available => NativeRuntime.Available;
        public static string Failure => NativeRuntime.Failure;
        public static bool TryInitialize(out string failure)
        {
            LibraryCapabilities.Require(LibraryCapability.RuntimeIl);
            bool ready = NativeRuntime.TryInitialize();
            failure = NativeRuntime.Failure;
            return ready;
        }
        public static NativeIlStatus GetStatus(MethodInfo method)
        {
            NativeRuntime.Validate(method);
            return NativeRuntime.Status(method);
        }
    }
}
