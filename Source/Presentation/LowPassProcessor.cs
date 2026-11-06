using System;

namespace LihuatLib.Presentation
{
    internal sealed class LowPassProcessor
    {
        private readonly double[] delay1 = new double[8];
        private readonly double[] delay2 = new double[8];
        private double b0, b1, b2, a1, a2;
        private float lastCutoff;
        private int lastRate, lastChannels;

        // All state belongs to the audio thread; the main thread only publishes scalar parameters.
        public void Process(float[] data, int channels, int sampleRate, float cutoff)
        {
            if (channels < 1 || channels > delay1.Length || sampleRate <= 0) return;
            if (lastRate != sampleRate || lastChannels != channels)
            {
                Array.Clear(delay1, 0, delay1.Length);
                Array.Clear(delay2, 0, delay2.Length);
                lastChannels = channels;
            }
            if (lastRate != sampleRate || lastCutoff != cutoff)
            {
                double k = Math.Tan(Math.PI * Math.Min(cutoff, sampleRate * .45) / sampleRate);
                double norm = 1.0 / (1.0 + Math.Sqrt(2.0) * k + k * k);
                b0 = k * k * norm; b1 = 2.0 * b0; b2 = b0;
                a1 = 2.0 * (k * k - 1.0) * norm;
                a2 = (1.0 - Math.Sqrt(2.0) * k + k * k) * norm;
                lastRate = sampleRate; lastCutoff = cutoff;
            }
            for (int channel = 0; channel < channels; channel++)
            {
                double first = delay1[channel], second = delay2[channel];
                for (int i = channel; i < data.Length; i += channels)
                {
                    double input = data[i];
                    double output = b0 * input + first;
                    first = b1 * input - a1 * output + second;
                    second = b2 * input - a2 * output;
                    data[i] = (float)output;
                }
                delay1[channel] = Math.Abs(first) < 1e-20 ? 0 : first;
                delay2[channel] = Math.Abs(second) < 1e-20 ? 0 : second;
            }
        }
    }
}
