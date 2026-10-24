using System;
using UnityEngine;

namespace KapiVR
{
    /// <summary>Soft sprites for button effects, generated once in code so no image files are needed.</summary>
    public static class VRUISprites
    {
        /// <summary>Pixels of glow outside the button edge in <see cref="glowFrame"/>.</summary>
        public const int GlowPixels = 32;

        static Sprite s_GlowFrame;

        /// <summary>
        /// A 9-sliced frame that is see-through in the middle, with a bright line along a rounded edge and a soft
        /// glow fading outward. Stretch it <see cref="GlowPixels"/> beyond a button to outline it.
        /// </summary>
        public static Sprite glowFrame => s_GlowFrame != null ? s_GlowFrame : s_GlowFrame = CreateGlowFrame();

        static Sprite CreateGlowFrame()
        {
            const int size = 96;
            const float cornerRadius = 8f;
            const float half = size * 0.5f - GlowPixels;

            var texture = Generate(size, size, (x, y) =>
            {
                // Signed distance to the rounded rectangle: negative inside, positive outside.
                float px = Mathf.Abs(x - size * 0.5f) - (half - cornerRadius);
                float py = Mathf.Abs(y - size * 0.5f) - (half - cornerRadius);
                float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude;
                float distance = outside + Mathf.Min(Mathf.Max(px, py), 0f) - cornerRadius;

                if (distance >= 0f)
                {
                    float fade = 1f - Mathf.Clamp01(distance / GlowPixels);
                    return Mathf.Max(Mathf.Exp(-distance * 0.9f), 0.55f * fade * fade);
                }
                return distance > -5f ? Mathf.Exp(distance * 0.9f) : 0f;
            });

            const float border = GlowPixels + cornerRadius;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static Texture2D Generate(int width, int height, Func<float, float, float> alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f)) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
