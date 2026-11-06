using System;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed class WorldArtSubject
    {
        public string Id { get; }
        public Pawn Pawn { get; }
        public WorldArtProfileDef Profile { get; }
        public string ResourceKey { get; }
        public int Priority { get; }
        private readonly Func<float> available;
        private readonly Func<float, bool> spend;
        private readonly Func<bool> permission;
        public float Available => available();
        public bool Permitted => permission?.Invoke() != false;

        public WorldArtSubject(string id, Pawn pawn, WorldArtProfileDef profile, string resourceKey,
            Func<float> available, Func<float, bool> spend, int priority = 0, Func<bool> permission = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            Id = id; Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            ResourceKey = resourceKey; Priority = priority;
            this.available = available ?? throw new ArgumentNullException(nameof(available));
            this.spend = spend ?? throw new ArgumentNullException(nameof(spend));
            this.permission = permission;
        }

        internal bool Spend(float amount) => amount == 0f || amount > 0f && Available >= amount && spend(amount);
    }
}
