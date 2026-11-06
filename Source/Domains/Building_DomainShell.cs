using Verse;
using UnityEngine;
using LihuatLib.Mechanisms;

namespace LihuatLib.Domains
{
    public class Building_DomainShell : Building
    {
        public string DomainId;
        public int Capacity = 500000;
        public int Width = 25;
        private ShellDamageRecord damageRecord;
        private bool destroying;

        public override void SpawnSetup(Map map, bool respawningAfterLoad) =>
            DomainShellSpawn.Initialize(this, map, respawningAfterLoad);

        public override int HitPoints
        {
            get => base.HitPoints;
            set
            {
                int before = base.HitPoints;
                int after = DomainShellDamageRules.LimitHitPoints(this, before, value);
                base.HitPoints = after;
                if (Prefs.DevMode && Spawned && after < before) damageRecord.Record(before, value, after);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref DomainId, "travelerDomainId");
            Scribe_Values.Look(ref Capacity, "capacity", 500000);
            Scribe_Values.Look(ref Width, "width", 25);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Destroyed || destroying) return;
            destroying = true;
            try
            {
                base.Destroy(mode);
                if (!Destroyed) return;
                DomainArea area = DomainAPI.Find(DomainId);
                if (area?.Shell == this && !area.Ending)
                {
                    if (Prefs.DevMode) damageRecord.Report(this);
                    DomainAPI.Collapse(DomainId, "shell-broken");
                }
            }
            finally { destroying = false; }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false) => Graphics.DrawMesh(MeshPool.plane10,
            Matrix4x4.TRS(drawLoc, Quaternion.identity, new Vector3(Width, 1f, Width)), Graphic.MatSingle, 0);

        public override string GetInspectString()
        {
            string original = base.GetInspectString();
            string recovery = "Lihuat_ShellRecovery".Translate();
            string limit = "Lihuat_ShellDamageLimit".Translate(DomainShellDamageRules.SingleDamagePercent,
                DomainShellDamageRules.MaximumSingleDamage(this));
            string details = recovery + "\n" + limit;
            return original.NullOrEmpty() ? details : original + "\n" + details;
        }
    }
}
