using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal sealed class DomainAudioController
    {
        private readonly Game game;
        private DomainLowPassFilter filter;

        public DomainAudioController(Game game) => this.game = game;

        public void Update(bool active)
        {
            if (filter != null && filter.Released) filter = null;
            if (filter == null)
            {
                if (!active) return;
                AudioListener listener = Find.Camera?.GetComponent<AudioListener>();
                if (listener == null) return;
                filter = listener.gameObject.AddComponent<DomainLowPassFilter>();
                filter.Initialize(game);
            }
            filter.SetActive(active);
        }
    }
}
