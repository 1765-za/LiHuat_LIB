using Verse;

namespace LihuatLib.StarBlood
{
    public static class StarBloodRegistry
    {
        public static StarBloodRules For(Pawn pawn) => pawn != null && LihuatGame.Current is LihuatGame game
            ? game.Subjects.Find(pawn) : null;

        internal static void Register(StarBloodRules engine) => LihuatGame.Current.Subjects.Register(engine);
        internal static void Remove(Pawn pawn, StarBloodRules engine)
        {
            if (For(pawn) == engine) LihuatGame.Current.Subjects.Remove(engine);
        }
    }
}
