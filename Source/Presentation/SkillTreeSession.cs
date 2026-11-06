using LihuatLib.AstralArts;
using LihuatLib.StarPaths;
using Verse;

namespace LihuatLib.Presentation
{
    internal interface ISkillTreeSession
    {
        Pawn Pawn { get; }
        StarPathTreeDef Tree { get; }
        StarPathAccount Account { get; }
        int Revision { get; }
        string Title { get; }
        string Status { get; }
        string Rule { get; }
        string Detail(StarPathNodeDef node);
        string Failure(StarPathNodeDef node);
        bool Upgrade(StarPathNodeDef node);
    }

    internal sealed class StarPathTreeSession : ISkillTreeSession
    {
        private readonly StarPathSubject subject;
        internal StarPathTreeSession(StarPathSubject subject) { this.subject = subject; }
        public Pawn Pawn => subject.Pawn;
        public StarPathTreeDef Tree => subject.Tree;
        public StarPathAccount Account => subject.Account;
        public int Revision => subject.Revision;
        public string Title => "Lihuat_StarPathTitle".Translate();
        public string Status => "Lihuat_StarPathStatus".Translate(Account.Level, Account.AvailablePoints, Account.SpentPoints);
        public string Rule => "Lihuat_StarPathRule".Translate();
        public string Detail(StarPathNodeDef node) => StarPathDescription.Detail(Account, node, (n, rank) => StarPathDescription.Effects(n.ranks[rank]));
        public string Failure(StarPathNodeDef node) => LihuatGame.Current.StarPaths.UpgradeFailure(subject, node);
        public bool Upgrade(StarPathNodeDef node) => LihuatGame.Current.StarPaths.TryUpgrade(subject, node);
    }

    internal interface ISkillTreeRelearn
    {
        string RelearnFailure { get; }
        bool Relearn();
    }

    internal sealed class AstralTreeSession : ISkillTreeSession, ISkillTreeRelearn
    {
        private readonly AstralEntry entry;
        internal AstralTreeSession(AstralEntry entry) { this.entry = entry; }
        public Pawn Pawn => entry.Pawn;
        public StarPathTreeDef Tree => AstralSkillDefs.Lihuat_AstralSkills.layout;
        public StarPathAccount Account => entry.Skills;
        public int Revision => entry.SkillRevision;
        public string Title => "Lihuat_AstralSkillTitle".Translate();
        public string Status => "Lihuat_AstralSkillStatus".Translate(Account.Level, Account.AvailablePoints, Account.SpentPoints,
            entry.Account.Energy.ToString("0"), entry.Capacity.ToString("0"));
        public string Rule => "Lihuat_AstralSkillRule".Translate();
        public string Detail(StarPathNodeDef node) => StarPathDescription.Detail(Account, node, AstralSkillDescription.Effects);
        public string Failure(StarPathNodeDef node) => LihuatGame.Current.AstralArts.SkillUpgradeFailure(entry, node);
        public bool Upgrade(StarPathNodeDef node) => LihuatGame.Current.AstralArts.TryUpgradeSkill(entry, node);
        public string RelearnFailure => LihuatGame.Current.AstralArts.RelearnFailure(entry);
        public bool Relearn() => LihuatGame.Current.AstralArts.RelearnSkills(entry);
    }
}
