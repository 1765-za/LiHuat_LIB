using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal sealed class DomainLowPassFilter : MonoBehaviour
    {
        private const float OpenCutoff = 22000f;
        private const float DomainCutoff = 1200f;
        private const float FadeInSeconds = .65f;
        private const float FadeOutSeconds = .8f;
        private readonly LowPassProcessor processor = new LowPassProcessor();
        private Game game;
        private float amount;
        private bool active;
        private volatile bool processing;
        private volatile int sampleRate;
        private volatile float cutoffFrequency = OpenCutoff;
        public bool Released { get; private set; }
        public float CutoffFrequency => cutoffFrequency;

        public void Initialize(Game owner)
        {
            game = owner;
            sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.OnAudioConfigurationChanged += RefreshSampleRate;
            processing = true;
        }

        public void SetActive(bool value) => active = value;
        private void RefreshSampleRate(bool deviceChanged) => sampleRate = AudioSettings.outputSampleRate;

        private void LateUpdate()
        {
            if (Released) return;
            if (!ReferenceEquals(game, Current.Game) || Current.ProgramState != ProgramState.Playing)
            {
                Release();
                return;
            }
            float target = active ? 1f : 0f;
            if (amount == target)
            {
                if (!active) Release();
                return;
            }
            amount = Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime / (active ? FadeInSeconds : FadeOutSeconds));
            if (amount == 0f)
            {
                Release();
                return;
            }
            float blend = amount * amount * (3f - 2f * amount);
            cutoffFrequency = OpenCutoff * Mathf.Pow(DomainCutoff / OpenCutoff, blend);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (processing) processor.Process(data, channels, sampleRate, cutoffFrequency);
        }

        private void Release()
        {
            if (Released) return;
            Released = true;
            processing = false;
            AudioSettings.OnAudioConfigurationChanged -= RefreshSampleRate;
            game = null;
            Destroy(this);
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();
    }
}
