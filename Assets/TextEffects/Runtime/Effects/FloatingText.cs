using System;
using UnityEngine;

namespace TextFX
{
    /// <summary>
    /// Makes each letter bob, rock and drift as if it were floating on water. A swell travels through the
    /// text, letters lean into its slope like buoys, and per-letter randomness keeps it from looking mechanical.
    /// </summary>
    [Serializable]
    [TextEffectMenu("Motion/Floating Text")]
    public class FloatingText : TextEffect
    {
        public enum FloatGroup
        {
            Letter,
            Word,
            Line,
            WholeText
        }

        public enum WaveDirection
        {
            LeftToRight,
            RightToLeft,
            RippleOut,
            RippleIn,
            Random
        }

        public enum Pivot
        {
            Center,
            Bottom,
            Top
        }

        [Header("Bobbing")]
        [Tooltip("How far letters rise and sink, as a fraction of the letter height.")]
        [Range(0f, 0.5f)] public float bobHeight = 0.06f;

        [Tooltip("Rises and sinks per second.")]
        [Range(0f, 5f)] public float bobSpeed = 0.6f;

        [Header("Rocking")]
        [Tooltip("How far letters tilt side to side, in degrees.")]
        [Range(0f, 45f)] public float tiltAngle = 4f;

        [Tooltip("Rocks per second. Keep it equal to Bob Speed and letters lean into the waves like real buoys.")]
        [Range(0f, 5f)] public float tiltSpeed = 0.6f;

        [Tooltip("The point each letter rocks around.")]
        public Pivot tiltPivot = Pivot.Center;

        [Header("Drifting")]
        [Tooltip("How far letters drift sideways, as a fraction of the letter height.")]
        [Range(0f, 0.5f)] public float driftAmount = 0.02f;

        [Tooltip("Side-to-side drifts per second.")]
        [Range(0f, 5f)] public float driftSpeed = 0.35f;

        [Header("Waves")]
        [Tooltip("Which way the swell travels through the text.\nRipple Out: from the middle to the edges, like a dropped pebble.\nRipple In: from the edges to the middle.\nRandom: no swell, every letter does its own thing.")]
        public WaveDirection waveDirection = WaveDirection.LeftToRight;

        [Tooltip("How many letters one full wave spans. Small = choppy, large = a long rolling swell.")]
        [ShowIf(nameof(hasSwell))]
        [Range(1f, 40f)] public float waveLength = 8f;

        [Tooltip("Adds small, faster ripples on top of the main swell.")]
        [Range(0f, 1f)] public float choppiness = 0.25f;

        [Header("Water Feel")]
        [Tooltip("Gives each letter its own timing and strength so the motion never looks mechanical.")]
        [Range(0f, 1f)] public float randomness = 0.35f;

        [Tooltip("Letters stretch as they rise and squash as they sink.")]
        [Range(0f, 1f)] public float squashAndStretch = 0f;

        [Tooltip("What floats as one piece: each letter, each word (like little rafts), each line, or the whole text.")]
        public FloatGroup floatEach = FloatGroup.Letter;

        [Tooltip("Seconds for the motion to build up from still water after the text appears. 0 = start at full motion.")]
        [Min(0f)] public float easeInTime = 0f;

        [Tooltip("Change this for a different random pattern.")]
        public int seed;

        bool hasSwell => waveDirection != WaveDirection.Random;

        public override void Apply(TextEffectContext context)
        {
            var characters = context.characters;
            float strength = intensity;
            if (easeInTime > 0f) strength *= Mathf.SmoothStep(0f, 1f, context.time / easeInTime);
            if (strength <= 0f) return;

            var scope = ToScope(floatEach);
            var whole = context.wholeText;

            // Measure distances in "letters" so the wave looks the same at any font size.
            float letterWidth = Mathf.Max(whole.height * 0.55f, 1e-4f);
            float textWidth = whole.size.x / letterWidth;

            int lastGroup = -1;
            Vector2 pivot = default;
            Vector2 offset = default;
            Vector2 scale = Vector2.one;
            float tilt = 0f;

            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                var group = context.GetGroup(scope, character);

                // Every letter in a word, line or the whole text shares one motion, so work it out once per group.
                if (group.index != lastGroup)
                {
                    lastGroup = group.index;
                    Simulate(group, character.lineIndex, context.time, strength, whole.min.x, letterWidth, textWidth,
                        out pivot, out offset, out tilt, out scale);
                }

                context.TransformCharacter(character, pivot, offset, tilt, scale);
            }
        }

        void Simulate(in TextGroup group, int lineIndex, float time, float strength, float textLeft, float letterWidth,
            float textWidth, out Vector2 pivot, out Vector2 offset, out float tilt, out Vector2 scale)
        {
            const float TwoPi = TextFXMath.TwoPi;
            int key = group.index;

            // Where this piece sits along the swell, and which way the swell is moving there.
            float x = (group.center.x - textLeft) / letterWidth;
            float middle = textWidth * 0.5f;
            float distance;
            float lean;
            switch (waveDirection)
            {
                case WaveDirection.LeftToRight:
                    distance = x;
                    lean = 1f;
                    break;
                case WaveDirection.RightToLeft:
                    distance = textWidth - x;
                    lean = -1f;
                    break;
                case WaveDirection.RippleOut:
                    distance = Mathf.Abs(x - middle);
                    lean = x < middle ? -1f : 1f;
                    break;
                case WaveDirection.RippleIn:
                    distance = middle - Mathf.Abs(x - middle);
                    lean = x < middle ? 1f : -1f;
                    break;
                default:
                    distance = Random(key, 0) * waveLength;
                    lean = 1f;
                    break;
            }

            // Offsetting each line keeps stacked letters from moving in lockstep.
            float phase = distance / waveLength * TwoPi + lineIndex * 0.9f;

            float speedJitter = 1f + (Random(key, 1) - 0.5f) * 0.6f * randomness;
            float sizeJitter = 1f + (Random(key, 2) - 0.5f) * 0.8f * randomness;
            float phaseJitter = Random(key, 3) * TwoPi * randomness;

            float swell = Mathf.Sin(TwoPi * bobSpeed * speedJitter * time - phase + phaseJitter);
            float ripples = 0.6f * Mathf.Sin(TwoPi * bobSpeed * 2.3f * speedJitter * time - phase * 2.1f + Random(key, 4) * TwoPi)
                + 0.4f * Mathf.Sin(TwoPi * bobSpeed * 3.7f * time + phase * 1.3f + Random(key, 5) * TwoPi);
            float wave = Mathf.Lerp(swell, ripples, choppiness * 0.5f);

            // A floating object tilts with the slope of the water under it, which is the cosine of the swell.
            float tiltPhase = TwoPi * tiltSpeed * speedJitter * time - phase + phaseJitter;
            float rock = Mathf.Lerp(-Mathf.Cos(tiltPhase) * lean, ripples, choppiness * 0.3f);

            float driftPhase = TwoPi * driftSpeed * speedJitter * time - phase * 0.7f + Random(key, 6) * TwoPi * randomness + 1.3f;
            float drift = Mathf.Sin(driftPhase);

            float size = group.height * sizeJitter * strength;
            offset = new Vector2(driftAmount * size * drift, bobHeight * size * wave);
            tilt = tiltAngle * sizeJitter * strength * rock;

            float squash = squashAndStretch * 0.15f * wave * strength;
            scale = new Vector2(1f - squash * 0.5f, 1f + squash);

            switch (tiltPivot)
            {
                case Pivot.Bottom:
                    pivot = new Vector2(group.center.x, group.min.y);
                    break;
                case Pivot.Top:
                    pivot = new Vector2(group.center.x, group.max.y);
                    break;
                default:
                    pivot = group.center;
                    break;
            }
        }

        float Random(int key, int salt) => TextFXMath.Hash01(key, seed * 7919 + salt);

        static TextScope ToScope(FloatGroup group)
        {
            switch (group)
            {
                case FloatGroup.Word: return TextScope.Word;
                case FloatGroup.Line: return TextScope.Line;
                case FloatGroup.WholeText: return TextScope.WholeText;
                default: return TextScope.Character;
            }
        }
    }
}
