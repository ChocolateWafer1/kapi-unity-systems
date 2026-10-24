using UnityEngine;

namespace TextFX
{
    /// <summary>Small math and color helpers shared by the effects.</summary>
    public static class TextFXMath
    {
        public const float TwoPi = Mathf.PI * 2f;

        /// <summary>A stable pseudo-random value in [0, 1). The same key and salt always give the same result.</summary>
        public static float Hash01(int key, int salt)
        {
            unchecked
            {
                uint h = (uint)key * 0x9E3779B1u + (uint)salt * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>Bounces a value back and forth between 0 and 1 (0→1→0→1…), so animating it never jumps.</summary>
        public static float Mirror01(float value)
        {
            value = Mathf.Repeat(value, 2f);
            return value <= 1f ? value : 2f - value;
        }

        /// <summary>Blends in linear space, which keeps in-between colors brighter than a plain lerp.</summary>
        public static Color LerpLinear(Color a, Color b, float t)
        {
            return Color.LerpUnclamped(a.linear, b.linear, t).gamma;
        }

        /// <summary>Blends by walking around the color wheel the short way, for vivid in-between colors.</summary>
        public static Color LerpHue(Color a, Color b, float t)
        {
            Color.RGBToHSV(a, out float hueA, out float satA, out float valA);
            Color.RGBToHSV(b, out float hueB, out float satB, out float valB);

            // Grays have no hue of their own; borrow the other color's so the blend doesn't swing through red.
            if (satA < 1e-4f) hueA = hueB;
            else if (satB < 1e-4f) hueB = hueA;

            float hueStep = Mathf.Repeat(hueB - hueA + 0.5f, 1f) - 0.5f;
            var color = Color.HSVToRGB(Mathf.Repeat(hueA + hueStep * t, 1f), Mathf.Lerp(satA, satB, t), Mathf.Lerp(valA, valB, t));
            color.a = Mathf.Lerp(a.a, b.a, t);
            return color;
        }
    }
}
