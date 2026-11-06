using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ComponentIntegrity
    {
        private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal static void Install()
        {
            MethodInfo dispatch = typeof(ComponentIntegrity).GetMethod(nameof(BeforeDispatch), Methods);
            foreach (string name in new[] { nameof(GameComponentUtility.GameComponentUpdate), nameof(GameComponentUtility.GameComponentTick), nameof(GameComponentUtility.FinalizeInit) })
                NativeIlGuards.Guard(typeof(GameComponentUtility).GetMethod(name, Methods), dispatch);
            MethodInfo save = typeof(RetainedGameComponents).GetMethod(nameof(RetainedGameComponents.Maintain));
            foreach (string name in new[] { "FillComponents", "ExposeSmallComponents" })
                NativeIlGuards.Guard(typeof(Game).GetMethod(name, Methods), save, 0);
        }
        private static void BeforeDispatch() => RetainedGameComponents.Maintain(Current.Game);
    }
}
