using UnityEngine;
using UnityEngine.Rendering;

namespace KapiWater
{
    /// <summary>
    /// A pixel-art particle system that only lives on one side of the water surface: bubbles and specks vanish when
    /// they leave the water, splash droplets vanish when they fall back in. Everything is emitted from code.
    /// </summary>
    sealed class WaterParticles
    {
        const string k_ShaderResource = "KapiWater_PixelParticle";

        public enum Side { Underwater, AboveWater }

        readonly ParticleSystem m_System;
        readonly ParticleSystemRenderer m_Renderer;
        readonly ParticleSystem.Particle[] m_Buffer;
        readonly Side m_Side;
        readonly bool m_ExactSurface;
        readonly Material m_Material;

        /// <param name="gravity">Multiplies normal gravity. Negative floats up.</param>
        /// <param name="drag">How quickly particles lose their starting speed.</param>
        /// <param name="wobble">Random drifting sway, for bubbles and specks.</param>
        /// <param name="exactSurface">Follow the waves exactly (costs more) instead of the calm surface height.</param>
        public WaterParticles(string name, Transform parent, Texture2D sprite, int maxParticles, float gravity, float drag, float wobble,
            Side side, bool exactSurface, AnimationCurve sizeOverLife)
        {
            m_Side = side;
            m_ExactSurface = exactSurface;
            m_Buffer = new ParticleSystem.Particle[maxParticles];

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            m_System = go.AddComponent<ParticleSystem>();
            m_System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = m_System.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startRotation = 0f; // Never spin, so the pixels stay on a grid.
            main.gravityModifier = gravity;

            var emission = m_System.emission;
            emission.enabled = false;
            var shape = m_System.shape;
            shape.enabled = false;

            if (drag > 0f)
            {
                var limit = m_System.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 100f;
                limit.drag = drag;
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            if (wobble > 0f)
            {
                var noise = m_System.noise;
                noise.enabled = true;
                noise.strength = wobble;
                noise.frequency = 1.6f;
                noise.scrollSpeed = 0.6f;
                noise.quality = ParticleSystemNoiseQuality.Medium;
            }

            if (sizeOverLife != null)
            {
                var size = m_System.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLife);
            }

            // Fade in and out at the ends of their lives. The shader turns that into a pixel dither.
            var color = m_System.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;

            var shader = Resources.Load<Shader>(k_ShaderResource);
            if (shader == null) Debug.LogWarning($"[Kapi Water] '{k_ShaderResource}' wasn't found in a Resources folder, so particles won't show.");
            else m_Material = new Material(shader) { name = name + " (Pixel)", mainTexture = sprite, hideFlags = HideFlags.DontSave };

            m_Renderer = go.GetComponent<ParticleSystemRenderer>();
            m_Renderer.renderMode = ParticleSystemRenderMode.Billboard;
            m_Renderer.alignment = ParticleSystemRenderSpace.View;
            m_Renderer.sharedMaterial = m_Material;
            m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.minParticleSize = 0f;
            m_Renderer.maxParticleSize = 10f; // Bubbles right in front of your eyes shouldn't shrink.
            m_Renderer.sortMode = ParticleSystemSortMode.None;

            m_System.Play();
        }

        public int Count => m_System != null ? m_System.particleCount : 0;

        /// <summary>Multiplies normal gravity. Negative floats up.</summary>
        public float Gravity
        {
            set
            {
                if (m_System == null) return;
                var main = m_System.main;
                if (!Mathf.Approximately(main.gravityModifierMultiplier, value)) main.gravityModifierMultiplier = value;
            }
        }

        /// <summary>How much the particles pick up the scene's lighting (0 = exact colors).</summary>
        public float Lit
        {
            set { if (m_Material != null) m_Material.SetFloat("_Lit", value); }
        }

        public void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            if (m_System == null) return;
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
                applyShapeToPosition = false,
            };
            m_System.Emit(emit, 1);
        }

        /// <summary>Removes particles on the wrong side of the water, and (with range > 0) ones too far from center.</summary>
        public void Cull(Vector3 center = default, float range = 0f)
        {
            if (m_System == null || m_System.particleCount == 0) return;
            int count = m_System.GetParticles(m_Buffer);
            int kept = 0;
            float rangeSq = range * range;
            for (int i = 0; i < count; i++)
            {
                var particle = m_Buffer[i];
                Vector3 p = particle.position;
                bool keep = range <= 0f || (p - center).sqrMagnitude <= rangeSq;
                if (keep)
                {
                    bool inWater = InWater(p);
                    keep = m_Side == Side.Underwater ? inWater : !inWater || particle.velocity.y > 0f;
                }
                if (keep) m_Buffer[kept++] = particle;
            }
            if (kept != count) m_System.SetParticles(m_Buffer, kept);
        }

        bool InWater(Vector3 p)
        {
            if (m_ExactSurface) return WaterVolume.Find(p) != null;
            var waters = WaterVolume.Active;
            for (int i = 0; i < waters.Count; i++)
            {
                var water = waters[i];
                if (p.y <= water.RestHeight - water.Waves.Amplitude && p.y >= water.Bottom && water.IsOverWater(p)) return true;
            }
            return false;
        }

        public void Destroy()
        {
            if (m_System != null) Object.Destroy(m_System.gameObject);
            if (m_Material != null) Object.Destroy(m_Material);
        }
    }

    /// <summary>Tiny pixel-art sprites for the particles, drawn in code with hard point-filtered pixels.</summary>
    static class WaterPixelArt
    {
        // '#' rim, '@' shine, '+' body, '.' clear. Top row first.
        static readonly string[] k_Bubble =
        {
            "..####..",
            ".#....#.",
            "#.@@...#",
            "#.@....#",
            "#......#",
            "#......#",
            ".#....#.",
            "..####..",
        };

        static readonly string[] k_Speck = { "##", "##" };

        static readonly string[] k_Droplet =
        {
            ".##.",
            "#@+#",
            "#++#",
            ".##.",
        };

        static Texture2D s_Bubble;
        static Texture2D s_Speck;
        static Texture2D s_Droplet;

        public static Texture2D Bubble => s_Bubble != null ? s_Bubble : s_Bubble = Build("Pixel Bubble", k_Bubble);
        public static Texture2D Speck => s_Speck != null ? s_Speck : s_Speck = Build("Pixel Speck", k_Speck);
        public static Texture2D Droplet => s_Droplet != null ? s_Droplet : s_Droplet = Build("Pixel Droplet", k_Droplet);

        static Texture2D Build(string name, string[] rows)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[width * height];
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 c;
                    switch (rows[row][x])
                    {
                        case '#': c = new Color32(215, 235, 255, 255); break;
                        case '@': c = new Color32(255, 255, 255, 255); break;
                        case '+': c = new Color32(190, 220, 245, 255); break;
                        default: c = new Color32(255, 255, 255, 0); break;
                    }
                    pixels[(height - 1 - row) * width + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
