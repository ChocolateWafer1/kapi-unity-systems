using System;
using System.Collections.Generic;
using UnityEngine;

namespace KapiRope
{
    /// <summary>How the colored yarns are arranged in the braided sheath.</summary>
    public enum RopeBraidPattern
    {
        Solid,
        Tracers,
        ThickTracers,
        Diamonds,
        Stripes,
        TwoTone,
        Speckled
    }

    public enum RopeTextureSize
    {
        Small = 256,
        Medium = 512,
        Large = 1024
    }

    /// <summary>Colors and weave settings for a generated braided climbing-rope texture.</summary>
    [Serializable]
    public sealed class RopeBraidStyle
    {
        [Tooltip("Color of most of the yarns.")]
        public Color baseColor = new Color(0.10f, 0.28f, 0.85f);

        [Tooltip("First pattern color.")]
        public Color accentColor = new Color(1f, 0.82f, 0.10f);

        [Tooltip("Second pattern color.")]
        public Color secondAccentColor = new Color(0.97f, 0.97f, 0.97f);

        [Tooltip("How the accent yarns are woven in. Tracers are the thin spirals you see on real climbing ropes.")]
        public RopeBraidPattern pattern = RopeBraidPattern.Tracers;

        [Tooltip("How many yarns go around the rope. Higher = finer weave.")]
        [Range(1, 4)] public int density = 2;

        [Tooltip("Strands inside each yarn bundle.")]
        [Range(1, 5)] public int strandsPerYarn = 3;

        [Tooltip("Depth of the weave in the normal map.")]
        [Range(0f, 2f)] public float bumpiness = 1f;

        [Tooltip("Random brightness differences between strands, so the rope doesn't look printed.")]
        [Range(0f, 1f)] public float colorVariation = 0.5f;

        [Tooltip("Changes the random strand variation and speckles.")]
        public int seed = 1;

        public RopeTextureSize textureSize = RopeTextureSize.Medium;

        public RopeBraidStyle Clone() => (RopeBraidStyle)MemberwiseClone();

        public static readonly string[] PresetNames =
        {
            "Climbing Blue", "Lava", "Jungle", "Candy", "Midnight Neon", "Banana", "Arctic", "Gorilla Brown"
        };

        public static RopeBraidStyle Preset(int index)
        {
            var s = new RopeBraidStyle();
            switch (index)
            {
                case 1:
                    s.baseColor = new Color(0.85f, 0.18f, 0.06f);
                    s.accentColor = new Color(0.08f, 0.08f, 0.08f);
                    s.secondAccentColor = new Color(1f, 0.78f, 0.12f);
                    s.pattern = RopeBraidPattern.Diamonds;
                    break;
                case 2:
                    s.baseColor = new Color(0.16f, 0.52f, 0.18f);
                    s.accentColor = new Color(0.55f, 0.92f, 0.22f);
                    s.secondAccentColor = new Color(0.36f, 0.22f, 0.10f);
                    s.pattern = RopeBraidPattern.ThickTracers;
                    break;
                case 3:
                    s.baseColor = new Color(1f, 0.45f, 0.72f);
                    s.accentColor = new Color(0.98f, 0.98f, 0.98f);
                    s.secondAccentColor = new Color(0.55f, 0.85f, 1f);
                    s.pattern = RopeBraidPattern.Stripes;
                    break;
                case 4:
                    s.baseColor = new Color(0.07f, 0.07f, 0.09f);
                    s.accentColor = new Color(0.1f, 0.95f, 1f);
                    s.secondAccentColor = new Color(1f, 0.15f, 0.85f);
                    s.pattern = RopeBraidPattern.Tracers;
                    break;
                case 5:
                    s.baseColor = new Color(1f, 0.86f, 0.18f);
                    s.accentColor = new Color(0.40f, 0.25f, 0.08f);
                    s.secondAccentColor = new Color(0.30f, 0.62f, 0.15f);
                    s.pattern = RopeBraidPattern.Speckled;
                    break;
                case 6:
                    s.baseColor = new Color(0.92f, 0.95f, 0.98f);
                    s.accentColor = new Color(0.25f, 0.62f, 0.95f);
                    s.secondAccentColor = new Color(0.12f, 0.20f, 0.45f);
                    s.pattern = RopeBraidPattern.ThickTracers;
                    break;
                case 7:
                    s.baseColor = new Color(0.45f, 0.30f, 0.17f);
                    s.accentColor = new Color(0.72f, 0.56f, 0.34f);
                    s.secondAccentColor = new Color(0.22f, 0.15f, 0.09f);
                    s.pattern = RopeBraidPattern.TwoTone;
                    break;
            }
            return s;
        }
    }

    /// <summary>
    /// Generates a seamless braided-sheath texture (like a kernmantle climbing rope) and its normal map. U runs
    /// around the rope and V runs along it; one tile is as tall as the rope is around, so it looks square on
    /// the rope. Yarns cross diagonally in a two-over, two-under weave.
    /// </summary>
    public static class RopeBraidTexture
    {
        const float k_Pi = Mathf.PI;
        const int k_FibersPerStrand = 5;

        /// <summary>Fills <paramref name="albedo"/> and <paramref name="normal"/> (bottom row first, like Texture2D).</summary>
        public static void Generate(RopeBraidStyle style, int size, Color32[] albedo, Color32[] normal)
        {
            int carriers = 4 * Mathf.Clamp(style.density, 1, 4);
            int strands = Mathf.Clamp(style.strandsPerYarn, 1, 5);
            var heights = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float a = (u + v) * carriers;
                    float b = (u - v) * carriers;
                    float fa = a - Mathf.Floor(a);
                    float fb = b - Mathf.Floor(b);
                    int ia = Mod(Mathf.FloorToInt(a), carriers);
                    int ib = Mod(Mathf.FloorToInt(b), carriers);

                    // Each yarn floats over two crossing yarns, then dives under the next two.
                    int phase = (ia + ib) & 3;
                    bool sOnTop = phase < 2;
                    float across = sOnTop ? fa : fb;
                    float along = ((sOnTop ? phase : phase - 2) + (sOnTop ? fb : fa)) * 0.5f;
                    int yarn = sOnTop ? ia : ib;
                    int direction = sOnTop ? 0 : 1;

                    float bundle = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(k_Pi * across)), 0.6f);
                    float strandCoord = across * strands;
                    int strandId = (int)strandCoord;
                    float strand = Mathf.Sqrt(Mathf.Clamp01(Mathf.Sin(k_Pi * (strandCoord - strandId))));
                    float dive = 0.25f + 0.75f * Mathf.Sqrt(Mathf.Clamp01(Mathf.Sin(k_Pi * along)));
                    float fiber = 0.5f + 0.5f * Mathf.Cos(2f * k_Pi * (strandCoord * k_FibersPerStrand + along * 2.5f));
                    float height = dive * (0.55f * bundle + 0.35f * strand + 0.10f * fiber * strand);
                    heights[y * size + x] = height;

                    float variation = Hash01(yarn, strandId, direction, style.seed);
                    float shade = 0.5f + 0.55f * height;
                    shade *= 1f - 0.08f * style.colorVariation + 0.16f * style.colorVariation * variation;
                    shade *= 0.93f + 0.07f * fiber;

                    Color c = YarnColor(style, direction, yarn, carriers) * shade;
                    albedo[y * size + x] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), 255);
                }
            }

            // Slopes are scaled by the weave cell size so the bump looks the same at any resolution.
            float slopeScale = 0.5f * size / (4f * carriers) * 3f * style.bumpiness;
            for (int y = 0; y < size; y++)
            {
                int up = ((y + 1) % size) * size;
                int down = ((y + size - 1) % size) * size;
                for (int x = 0; x < size; x++)
                {
                    int right = (x + 1) % size;
                    int left = (x + size - 1) % size;
                    float dx = (heights[y * size + right] - heights[y * size + left]) * slopeScale;
                    float dy = (heights[up + x] - heights[down + x]) * slopeScale;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * size + x] = new Color32(ToByte(n.x * 0.5f + 0.5f), ToByte(n.y * 0.5f + 0.5f), ToByte(n.z * 0.5f + 0.5f), 255);
                }
            }
        }

        static Color YarnColor(RopeBraidStyle style, int direction, int yarn, int carriers)
        {
            int half = carriers / 2;
            switch (style.pattern)
            {
                case RopeBraidPattern.Tracers:
                    if (direction == 0 && yarn == 0) return style.accentColor;
                    if (direction == 1 && yarn == half) return style.secondAccentColor;
                    break;
                case RopeBraidPattern.ThickTracers:
                    if (direction == 0 && (yarn == 0 || yarn == 1)) return style.accentColor;
                    if (direction == 1 && (yarn == half || yarn == half + 1)) return style.secondAccentColor;
                    break;
                case RopeBraidPattern.Diamonds:
                    if (yarn == 0) return style.accentColor;
                    if (yarn == half) return style.secondAccentColor;
                    break;
                case RopeBraidPattern.Stripes:
                    if (direction == 0 && yarn < half) return yarn == 0 ? style.secondAccentColor : style.accentColor;
                    break;
                case RopeBraidPattern.TwoTone:
                    if (direction == 1) return style.accentColor;
                    break;
                case RopeBraidPattern.Speckled:
                    float roll = Hash01(yarn, 7, direction, style.seed + 101);
                    if (roll < 0.22f) return style.accentColor;
                    if (roll < 0.36f) return style.secondAccentColor;
                    break;
            }
            return style.baseColor;
        }

        static readonly Dictionary<string, Material> s_RuntimeMaterials = new Dictionary<string, Material>();

        /// <summary>
        /// Builds a URP Lit material with freshly generated textures. Used when a rope has no material at runtime;
        /// in the editor, "Make Braid Material" saves real assets instead.
        /// </summary>
        public static Material GetRuntimeMaterial(RopeBraidStyle style)
        {
            string key = JsonUtility.ToJson(style);
            if (s_RuntimeMaterials.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            int size = (int)style.textureSize;
            var albedoPixels = new Color32[size * size];
            var normalPixels = new Color32[size * size];
            Generate(style, size, albedoPixels, normalPixels);

            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "Rope Braid", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            albedo.SetPixels32(albedoPixels);
            albedo.Apply(true, true);
            var normal = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Rope Braid Normal", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            normal.SetPixels32(normalPixels);
            normal.Apply(true, true);

            var material = new Material(shader) { name = "Rope Braid (Runtime)" };
            ApplyTextures(material, albedo, normal);
            s_RuntimeMaterials[key] = material;
            return material;
        }

        public static void ApplyTextures(Material material, Texture albedo, Texture normal)
        {
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_MainTex", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetFloat("_Metallic", 0f);
            material.EnableKeyword("_NORMALMAP");
        }

        static int Mod(int value, int m)
        {
            int r = value % m;
            return r < 0 ? r + m : r;
        }

        static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        static float Hash01(int a, int b, int c, int seed)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u + (uint)seed * 3266489917u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }
}
