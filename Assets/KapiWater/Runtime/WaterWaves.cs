using UnityEngine;

namespace KapiWater
{
    /// <summary>
    /// The water shader's Gerstner waves, worked out on the CPU with the exact same math (KapiWaterCommon.hlsl),
    /// so swimming, splashes and the underwater waterline follow the waves you actually see.
    /// </summary>
    public struct WaterWaves
    {
        const float k_Gravity = 9.81f;
        const float k_TwoPi = Mathf.PI * 2f;

        static readonly int s_WaveA = Shader.PropertyToID("_WaveA");
        static readonly int s_WaveB = Shader.PropertyToID("_WaveB");
        static readonly int s_WaveC = Shader.PropertyToID("_WaveC");
        static readonly int s_WaveD = Shader.PropertyToID("_WaveD");
        static readonly int s_Strength = Shader.PropertyToID("_WaveStrength");
        static readonly int s_Speed = Shader.PropertyToID("_WaveSpeed");

        public Vector4 waveA;
        public Vector4 waveB;
        public Vector4 waveC;
        public Vector4 waveD;
        public float strength;
        public float speed;

        /// <summary>Flat water.</summary>
        public static WaterWaves None => default;

        /// <summary>The same clock the shader uses for _Time.y.</summary>
        public static float ShaderTime => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

        /// <summary>Reads the wave settings off a material that uses the Kapi/Water shader.</summary>
        public static WaterWaves FromMaterial(Material material)
        {
            if (material == null || !material.HasProperty(s_WaveA)) return None;
            return new WaterWaves
            {
                waveA = material.GetVector(s_WaveA),
                waveB = material.GetVector(s_WaveB),
                waveC = material.GetVector(s_WaveC),
                waveD = material.GetVector(s_WaveD),
                strength = material.GetFloat(s_Strength),
                speed = material.GetFloat(s_Speed),
            };
        }

        /// <summary>The same waves with every wavelength (and so every wave height) multiplied, like the shader does for a water's Look Scale.</summary>
        public WaterWaves Scaled(float scale)
        {
            var scaled = this;
            scaled.waveA.w *= scale;
            scaled.waveB.w *= scale;
            scaled.waveC.w *= scale;
            scaled.waveD.w *= scale;
            return scaled;
        }

        /// <summary>How far above (and below) its rest level the surface can get, in meters.</summary>
        public float Amplitude
        {
            get
            {
                float sum = waveA.z * Mathf.Max(waveA.w, 0.01f) + waveB.z * Mathf.Max(waveB.w, 0.01f)
                    + waveC.z * Mathf.Max(waveC.w, 0.01f) + waveD.z * Mathf.Max(waveD.w, 0.01f);
                return Mathf.Abs(sum * strength) / k_TwoPi;
            }
        }

        /// <summary>Wave clock in seconds, already multiplied by the wave speed.</summary>
        public float Clock => ShaderTime * speed;

        /// <summary>Where a surface point that rests at p (world XZ) moved to, and the surface normal there.</summary>
        public Vector3 Sample(Vector2 p, float clock, float pin, out Vector3 normal)
        {
            Vector3 offset = Vector3.zero;
            Vector3 tangent = new Vector3(1f, 0f, 0f);
            Vector3 binormal = new Vector3(0f, 0f, 1f);
            AddWave(waveA, p, clock, pin, ref offset, ref tangent, ref binormal);
            AddWave(waveB, p, clock, pin, ref offset, ref tangent, ref binormal);
            AddWave(waveC, p, clock, pin, ref offset, ref tangent, ref binormal);
            AddWave(waveD, p, clock, pin, ref offset, ref tangent, ref binormal);
            normal = Vector3.Cross(binormal, tangent).normalized;
            return offset;
        }

        /// <summary>
        /// Height of the waves above the rest level at world XZ, plus the surface normal there. Waves also push the
        /// surface sideways, so this walks back to the spot that ended up at p before reading it.
        /// </summary>
        public float Height(Vector2 p, float clock, float pin, out Vector3 normal)
        {
            if (strength == 0f)
            {
                normal = Vector3.up;
                return 0f;
            }

            Vector2 q = p;
            for (int i = 0; i < 3; i++)
            {
                Vector3 guess = Sample(q, clock, pin, out _);
                q = p - new Vector2(guess.x, guess.z);
            }
            return Sample(q, clock, pin, out normal).y;
        }

        void AddWave(Vector4 wave, Vector2 p, float clock, float pin, ref Vector3 offset, ref Vector3 tangent, ref Vector3 binormal)
        {
            float steepness = wave.z * strength;
            if (steepness == 0f) return;

            float k = k_TwoPi / Mathf.Max(wave.w, 0.01f);
            float c = Mathf.Sqrt(k_Gravity / k);
            Vector2 d = new Vector2(wave.x, wave.y);
            float length = d.magnitude;
            d = length > 1e-4f ? d / length : new Vector2(1f, 0f);

            float f = k * (Vector2.Dot(d, p) - c * clock);
            float a = steepness / k;
            float sin = Mathf.Sin(f);
            float cos = Mathf.Cos(f);

            offset += new Vector3(d.x * a * cos * pin, a * sin, d.y * a * cos * pin);
            tangent += new Vector3(-d.x * d.x * steepness * sin * pin, d.x * steepness * cos, -d.x * d.y * steepness * sin * pin);
            binormal += new Vector3(-d.x * d.y * steepness * sin * pin, d.y * steepness * cos, -d.y * d.y * steepness * sin * pin);
        }
    }
}
