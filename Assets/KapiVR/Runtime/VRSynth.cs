using UnityEngine;

namespace KapiVR
{
    /// <summary>
    /// Menu sounds generated in code, so the menu sounds good without any audio files.
    /// Assign your own clips on a VR Menu Button to replace them.
    /// </summary>
    public static class VRSynth
    {
        const int k_SampleRate = 44100;
        const float k_TwoPi = Mathf.PI * 2f;

        static AudioClip s_Hover;
        static AudioClip s_Press;
        static AudioClip s_Confirm;
        static AudioClip s_Whoosh;

        /// <summary>A soft, glassy tick for when the laser lands on a button.</summary>
        public static AudioClip hover => s_Hover != null ? s_Hover : s_Hover = Build("VR Hover", Hover(0.14f));

        /// <summary>A short, weighty "thock" for when the trigger bottoms out.</summary>
        public static AudioClip press => s_Press != null ? s_Press : s_Press = Build("VR Press", Press(0.16f));

        /// <summary>A bright three-note rising chime for a confirmed click.</summary>
        public static AudioClip confirm => s_Confirm != null ? s_Confirm : s_Confirm = Build("VR Confirm", Confirm(1.1f));

        /// <summary>A soft airy swell that sits under a fade to black.</summary>
        public static AudioClip whoosh => s_Whoosh != null ? s_Whoosh : s_Whoosh = Build("VR Whoosh", Whoosh(1.2f));

        static float[] Hover(float seconds)
        {
            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            float phase = 0f;
            float overtone = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)k_SampleRate;
                float frequency = Mathf.Lerp(1750f, 1250f, t / seconds);
                phase += k_TwoPi * frequency / k_SampleRate;
                overtone += k_TwoPi * frequency * 2.76f / k_SampleRate;

                float attack = 1f - Mathf.Exp(-t * 1500f);
                samples[i] = attack * (Mathf.Sin(phase) * Mathf.Exp(-t * 40f) + 0.25f * Mathf.Sin(overtone) * Mathf.Exp(-t * 75f));
            }
            return samples;
        }

        static float[] Press(float seconds)
        {
            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            float phase = 0f;
            float filtered = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)k_SampleRate;
                float frequency = Mathf.Lerp(210f, 95f, Mathf.Clamp01(t / 0.08f));
                phase += k_TwoPi * frequency / k_SampleRate;

                // A tiny burst of high-passed noise gives the "click" at the front of the thock.
                float noise = Noise(i) * 2f - 1f;
                filtered += (noise - filtered) * 0.35f;
                float click = (noise - filtered) * Mathf.Exp(-t * 350f);

                float attack = 1f - Mathf.Exp(-t * 2500f);
                samples[i] = attack * Mathf.Sin(phase) * Mathf.Exp(-t * 32f) + 0.35f * click;
            }
            return samples;
        }

        static float[] Confirm(float seconds)
        {
            // A rising arpeggio (A5, E6, A6) with soft bell-like overtones.
            float[] notes = { 880f, 1318.5f, 1760f };
            float[] starts = { 0f, 0.075f, 0.15f };
            float[] levels = { 0.8f, 0.75f, 0.65f };

            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = Mathf.RoundToInt(starts[n] * k_SampleRate);
                for (int i = start; i < samples.Length; i++)
                {
                    float t = (i - start) / (float)k_SampleRate;
                    float attack = 1f - Mathf.Exp(-t * 900f);
                    float body = Mathf.Sin(k_TwoPi * notes[n] * t) * Mathf.Exp(-t * 6f);
                    float shimmer = 0.3f * Mathf.Sin(k_TwoPi * notes[n] * 2f * t) * Mathf.Exp(-t * 11f)
                        + 0.12f * Mathf.Sin(k_TwoPi * notes[n] * 3.01f * t) * Mathf.Exp(-t * 16f);
                    samples[i] += levels[n] * attack * (body + shimmer);
                }
            }
            return samples;
        }

        static float[] Whoosh(float seconds)
        {
            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            float low = 0f;
            float band = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)k_SampleRate;
                float progress = t / seconds;

                // Two one-pole filters make a band of noise whose center sweeps up then back down.
                float cutoff = Mathf.Lerp(0.02f, 0.12f, Mathf.Sin(progress * Mathf.PI));
                float noise = Noise(i + 7919) * 2f - 1f;
                low += (noise - low) * cutoff;
                band += (low - band) * cutoff * 0.5f;

                float envelope = Mathf.Sin(progress * Mathf.PI);
                samples[i] = (low - band) * envelope * envelope;
            }
            return samples;
        }

        static AudioClip Build(string name, float[] samples)
        {
            float peak = 0f;
            foreach (var sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
                for (int i = 0; i < samples.Length; i++)
                    samples[i] *= 0.9f / peak;

            var clip = AudioClip.Create(name, samples.Length, 1, k_SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static float Noise(int index)
        {
            unchecked
            {
                uint h = (uint)index * 0x9E3779B1u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h >> 8) * (1f / 16777216f);
            }
        }
    }
}
