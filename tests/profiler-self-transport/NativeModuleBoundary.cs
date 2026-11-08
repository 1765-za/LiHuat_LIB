namespace LihuatLib.Mechanisms
{
    // Compile boundary only. The test invokes Request, never Attach/Send or Bind.
    // Accidentally entering the full backend binding path must fail, not succeed.
    internal static class NativeModule
    {
        internal static T Bind<T>(string name) where T : Delegate =>
            throw new NotSupportedException("Full native backend binding is outside the self-transport test.");
    }
}
