using UnityEngine;

namespace KapiWater
{
    /// <summary>
    /// Swimming sounds generated in code, so the water sounds good without any audio files. Every slot on the Gorilla
    /// Swimmer and Water Volume takes your own clip instead.
    /// </summary>
    public static class WaterSynth
    {
        const int k_SampleRate = 44100;
        const float k_TwoPi = Mathf.PI * 2f;

        static AudioClip s_Rush;
        static AudioClip[] s_Bubbles;
        static AudioClip[] s_Splashes;
        static AudioClip s_Plunge;
        static AudioClip s_Ambience;

        /// <summary>Looping rush of water past your hand. Its volume follows how hard you pull.</summary>
        public static AudioClip Rush => s_Rush != null ? s_Rush : s_Rush = Build("Water Rush", Loop(MakeRush(2.6f), 0.4f));

        /// <summary>Little rising "bloop"s, like single bubbles leaving your hand.</summary>
        public static AudioClip[] Bubbles
        {
            get
            {
                if (s_Bubbles != null && s_Bubbles[0] != null) return s_Bubbles;
                float[] pitches = { 520f, 690f, 870f, 1120f, 1480f };
                s_Bubbles = new AudioClip[pitches.Length];
                for (int i = 0; i < pitches.Length; i++)
                    s_Bubbles[i] = Build($"Water Bubble {i + 1}", MakeBubble(pitches[i], 0.022f + 0.004f * i));
                return s_Bubbles;
            }
        }

        /// <summary>Hand-sized splashes: a slap, a plop and a fizz of bubbles.</summary>
        public static AudioClip[] Splashes
        {
            get
            {
                if (s_Splashes != null && s_Splashes[0] != null) return s_Splashes;
                s_Splashes = new AudioClip[4];
                for (int i = 0; i < s_Splashes.Length; i++)
                    s_Splashes[i] = Build($"Water Splash {i + 1}", MakeSplash(0.6f, 1.0f - 0.12f * i, 101 + i * 37));
                return s_Splashes;
            }
        }

        /// <summary>Your whole body going under: a heavy splash, a deep muffled whoomp and a cloud of bubbles.</summary>
        public static AudioClip Plunge => s_Plunge != null ? s_Plunge : s_Plunge = Build("Water Plunge", MakePlunge(1.3f));

        /// <summary>Looping deep hum of being underwater, with the odd distant bubble.</summary>
        public static AudioClip Ambience => s_Ambience != null ? s_Ambience : s_Ambience = Build("Underwater Ambience", Loop(MakeAmbience(8.5f), 0.5f));

        public static AudioClip Pick(AudioClip[] clips, AudioClip[] fallback)
        {
            var set = clips != null && clips.Length > 0 ? clips : fallback;
            for (int tries = 0; tries < 4; tries++)
            {
                var clip = set[Random.Range(0, set.Length)];
                if (clip != null) return clip;
            }
            foreach (var clip in set)
                if (clip != null) return clip;
            return fallback[Random.Range(0, fallback.Length)];
        }

        static float[] MakeRush(float seconds)
        {
            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            float low = 0f, high = 0f, gurgle = 0f, gurgleTarget = 0.5f;
            float aLow = Coefficient(1300f), aHigh = Coefficient(170f), aGurgle = Coefficient(9f);
            for (int i = 0; i < samples.Length; i++)
            {
                // A band of noise for the rush, with a slow random wobble in loudness so it gurgles.
                float noise = Noise(i) * 2f - 1f;
                low += (noise - low) * aLow;
                high += (noise - high) * aHigh;
                if (i % 2205 == 0) gurgleTarget = 0.35f + 0.65f * Noise(i + 999331);
                gurgle += (gurgleTarget - gurgle) * aGurgle;
                samples[i] = (low - high) * (0.55f + 0.45f * gurgle);
            }
            // A few tiny bubbles mixed through it.
            for (int b = 0; b < 9; b++)
                AddChirp(samples, Noise(b * 131 + 7) * seconds, 1400f + 1600f * Noise(b * 71 + 3), 0.012f, 0.12f);
            return samples;
        }

        static float[] MakeBubble(float pitch, float decay)
        {
            var samples = new float[Mathf.CeilToInt((decay * 7f) * k_SampleRate)];
            AddChirp(samples, 0f, pitch, decay, 1f);
            return samples;
        }

        static float[] MakeSplash(float seconds, float weight, int seed)
        {
            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            float slap = 0f, body = 0f, bodyHigh = 0f;
            float aBodyHigh = Coefficient(700f);
            float plopPhase = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)k_SampleRate;
                float noise = Noise(i + seed * 7919) * 2f - 1f;

                // The slap: bright noise whose brightness drops as it dies away.
                float cutoff = Mathf.Lerp(7000f, 1100f, Mathf.Clamp01(t / 0.25f));
                slap += (noise - slap) * Coefficient(cutoff);
                float slapLevel = (1f - Mathf.Exp(-t * 1500f)) * Mathf.Exp(-t / 0.055f);

                // The "shhh" of water falling back.
                body += (noise - body) * Coefficient(4200f);
                bodyHigh += (body - bodyHigh) * aBodyHigh;
                float bodyLevel = Mathf.Exp(-t / 0.16f) * Mathf.Clamp01(t / 0.01f);

                // The plop: a quick low drop in pitch.
                float plopFrequency = Mathf.Lerp(190f, 70f, Mathf.Clamp01(t / 0.06f));
                plopPhase += k_TwoPi * plopFrequency / k_SampleRate;
                float plop = Mathf.Sin(plopPhase) * Mathf.Exp(-t / 0.045f) * (1f - Mathf.Exp(-t * 900f));

                samples[i] = slap * slapLevel * 0.9f + (body - bodyHigh) * bodyLevel * 0.5f + plop * 0.55f * weight;
            }

            int bubbles = 7 + seed % 5;
            for (int b = 0; b < bubbles; b++)
            {
                float start = 0.015f + Noise(seed + b * 53) * 0.33f;
                AddChirp(samples, start, 600f + 1900f * Noise(seed * 3 + b * 17), 0.014f + 0.012f * Noise(b + seed), 0.18f + 0.22f * Noise(seed + b * 5) * (1f - start * 2f));
            }
            for (int d = 0; d < 4; d++)
                AddClick(samples, 0.16f + Noise(seed * 11 + d * 29) * 0.38f, 0.05f + 0.06f * Noise(d * 13 + seed));
            return samples;
        }

        static float[] MakePlunge(float seconds)
        {
            var samples = MakeSplash(seconds, 1.4f, 4441);
            float rumble = 0f, rumble2 = 0f;
            float aRumble = Coefficient(320f);
            float aRumble2 = Coefficient(60f);
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)k_SampleRate;
                float noise = Noise(i + 55511) * 2f - 1f;
                rumble += (noise - rumble) * aRumble;
                rumble2 += (rumble - rumble2) * aRumble2;
                float swell = Mathf.Clamp01(t / 0.07f) * Mathf.Exp(-Mathf.Max(0f, t - 0.07f) / 0.38f);
                samples[i] += (rumble - rumble2) * swell * 2.2f;
            }
            for (int b = 0; b < 26; b++)
            {
                float start = 0.04f + Mathf.Pow(Noise(b * 97 + 11), 1.6f) * 0.85f;
                AddChirp(samples, start, 260f + 1000f * Noise(b * 61 + 5), 0.02f + 0.02f * Noise(b * 7), 0.35f * (1f - start / seconds));
            }
            return samples;
        }

        static float[] MakeAmbience(float seconds)
        {
            var samples = new float[Mathf.CeilToInt(seconds * k_SampleRate)];
            float deep = 0f, deep2 = 0f, hiss = 0f, swell = 0.5f, swellTarget = 0.5f;
            float aDeep = Coefficient(240f), aDeep2 = Coefficient(35f), aHiss = Coefficient(2500f), aSwell = Coefficient(0.35f);
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = Noise(i + 31337) * 2f - 1f;
                deep += (noise - deep) * aDeep;
                deep2 += (deep - deep2) * aDeep2;
                hiss += (noise - hiss) * aHiss;
                if (i % 22050 == 0) swellTarget = 0.4f + 0.6f * Noise(i / 22050 + 4242);
                swell += (swellTarget - swell) * aSwell;
                samples[i] = (deep - deep2) * 3.2f * swell + (noise - hiss) * 0.012f;
            }
            for (int b = 0; b < 11; b++)
                AddChirp(samples, Noise(b * 389 + 1) * (seconds - 0.3f), 300f + 500f * Noise(b * 17 + 9), 0.035f, 0.06f + 0.05f * Noise(b));
            return samples;
        }

        /// <summary>One bubble: a sine whose pitch rises as it pops off (like a real bubble), fading quickly.</summary>
        static void AddChirp(float[] samples, float start, float pitch, float decay, float level)
        {
            int first = Mathf.RoundToInt(start * k_SampleRate);
            int length = Mathf.CeilToInt(decay * 7f * k_SampleRate);
            float phase = 0f;
            for (int i = 0; i < length && first + i < samples.Length; i++)
            {
                if (first + i < 0) continue;
                float t = i / (float)k_SampleRate;
                float frequency = pitch * (1f + 1.6f * t / (decay * 7f));
                phase += k_TwoPi * frequency / k_SampleRate;
                float envelope = (1f - Mathf.Exp(-t * 2500f)) * Mathf.Exp(-t / decay);
                samples[first + i] += level * envelope * (Mathf.Sin(phase) + 0.12f * Mathf.Sin(phase * 2f));
            }
        }

        /// <summary>A droplet landing: a tiny click.</summary>
        static void AddClick(float[] samples, float start, float level)
        {
            int first = Mathf.RoundToInt(start * k_SampleRate);
            int length = Mathf.CeilToInt(0.006f * k_SampleRate);
            float previous = 0f;
            for (int i = 0; i < length && first + i < samples.Length; i++)
            {
                float noise = Noise(first + i + 777) * 2f - 1f;
                samples[first + i] += (noise - previous) * level * Mathf.Exp(-i / (0.0015f * k_SampleRate));
                previous = noise;
            }
        }

        /// <summary>Crossfades the end into the start so the clip loops with no click.</summary>
        static float[] Loop(float[] samples, float crossfadeSeconds)
        {
            int fade = Mathf.Min(Mathf.RoundToInt(crossfadeSeconds * k_SampleRate), samples.Length / 3);
            int length = samples.Length - fade;
            var loop = new float[length];
            for (int i = 0; i < length; i++)
            {
                if (i >= fade)
                {
                    loop[i] = samples[i];
                    continue;
                }
                float w = i / (float)fade;
                loop[i] = samples[i] * Mathf.Sqrt(w) + samples[i + length] * Mathf.Sqrt(1f - w);
            }
            return loop;
        }

        static float Coefficient(float cutoff) => 1f - Mathf.Exp(-k_TwoPi * cutoff / k_SampleRate);

        static float Noise(int i)
        {
            unchecked
            {
                uint h = (uint)i * 747796405u + 2891336453u;
                h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
                h = (h >> 22) ^ h;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        static AudioClip Build(string name, float[] samples)
        {
            float peak = 0f;
            foreach (var sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0f)
            {
                float gain = 0.45f / peak;
                for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
            }

            var clip = AudioClip.Create(name, samples.Length, 1, k_SampleRate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
