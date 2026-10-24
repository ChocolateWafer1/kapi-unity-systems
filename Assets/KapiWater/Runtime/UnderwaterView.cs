using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KapiWater
{
    /// <summary>
    /// Every camera already gets the underwater look when it dips below a Water Volume's waves (water fog, wobble,
    /// caustics, darker with depth, a waterline when your head is half in). This adds the rest for the player's
    /// camera: drifting pixel specks, muffled game audio and an underwater hum, and a switch to turn the look off.
    /// The look and sound come from the water you're in. The Gorilla Swimmer adds this to your head camera for you.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Kapi Water/Underwater View")]
    public sealed class UnderwaterView : MonoBehaviour
    {
        const float k_OpenAirCutoff = 22000f;

        [Tooltip("Turns off the underwater look for this camera (sounds and specks too).")]
        [SerializeField] bool m_Effect = true;

        [Tooltip("Muffle the rest of the game's audio while this camera is underwater. Needs an Audio Listener on this camera or in the scene.")]
        [SerializeField] bool m_MuffleAudio = true;

        [Tooltip("How quickly the sound goes muffled and back, in seconds.")]
        [SerializeField, Min(0f)] float m_MuffleSpeed = 0.15f;

        Camera m_Camera;
        WaterVolume m_Water;
        float m_HeadDepth;
        float m_Under;
        AudioLowPassFilter m_Filter;
        bool m_OwnsFilter;
        AudioSource m_Ambience;
        WaterParticles m_Specks;
        float m_SpeckBudget;

        /// <summary>The water this camera is in or just above, or null.</summary>
        public WaterVolume Water => m_Water;

        /// <summary>True while the camera is under the waves.</summary>
        public bool IsUnderwater => m_Water != null && m_HeadDepth > 0f;

        /// <summary>How far below the waves the camera is, in meters (negative = above).</summary>
        public float Depth => m_HeadDepth;

        /// <summary>Adds an underwater view to the camera (or returns the one already there).</summary>
        public static UnderwaterView Attach(Camera camera)
        {
            if (camera == null) return null;
            var view = camera.GetComponent<UnderwaterView>();
            return view != null ? view : camera.gameObject.AddComponent<UnderwaterView>();
        }

        /// <summary>True if this camera has an Underwater View with the look switched off.</summary>
        internal static bool TurnsOffLook(Camera camera)
        {
            return camera.TryGetComponent<UnderwaterView>(out var view) && view.isActiveAndEnabled && !view.m_Effect;
        }

        void Awake()
        {
            m_Camera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            // The look needs the camera's depth and opaque textures; ask for them on the camera itself (only while
            // playing, so nothing gets saved), on top of the water's own request.
            if (!Application.isPlaying) return;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.requiresDepthOption = CameraOverrideOption.On;
            data.requiresColorOption = CameraOverrideOption.On;
        }

        void OnDisable()
        {
            m_Under = 0f;
            ApplyAudio(0f);
        }

        void OnDestroy()
        {
            UnderwaterRendering.Forget(m_Camera);
            m_Specks?.Destroy();
            if (m_Ambience != null) Destroy(m_Ambience.gameObject);
            if (m_OwnsFilter && m_Filter != null) Destroy(m_Filter);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 head = transform.position;
            m_Water = m_Effect ? WaterVolume.Find(head, UnderwaterRendering.LensReach(m_Camera)) : null;
            m_HeadDepth = m_Water != null ? m_Water.SurfaceHeight(head) - head.y : -1f;

            float target = IsUnderwater ? 1f : 0f;
            m_Under = m_MuffleSpeed <= 0f ? target : Mathf.MoveTowards(m_Under, target, dt / m_MuffleSpeed);
            ApplyAudio(m_Under);
            UpdateSpecks(head, dt);
        }

        void ApplyAudio(float under)
        {
            WaterVolume water = m_Water;
            float cutoff = water != null ? water.MuffleCutoff : 900f;

            if (m_MuffleAudio && (under > 0f || m_Filter != null))
            {
                if (m_Filter == null && under > 0f) FindFilter();
                if (m_Filter != null)
                {
                    // Sweep the cutoff on a log scale so it sounds even.
                    m_Filter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(k_OpenAirCutoff), Mathf.Log(cutoff), under));
                    m_Filter.lowpassResonanceQ = 1f;
                    m_Filter.enabled = under > 0.001f;
                }
            }

            if (under <= 0f && m_Ambience == null) return;
            if (m_Ambience == null)
            {
                var go = new GameObject("Underwater Ambience");
                go.transform.SetParent(transform, false);
                m_Ambience = go.AddComponent<AudioSource>();
                m_Ambience.loop = true;
                m_Ambience.playOnAwake = false;
                m_Ambience.spatialBlend = 0f;
                m_Ambience.bypassListenerEffects = true; // Already sounds like it's underwater.
            }

            var clip = water != null && water.Ambience != null ? water.Ambience : WaterSynth.Ambience;
            if (m_Ambience.clip != clip) m_Ambience.clip = clip;
            m_Ambience.volume = (water != null ? water.AmbienceVolume : 0.18f) * under;
            if (under > 0.001f && !m_Ambience.isPlaying) m_Ambience.Play();
            else if (under <= 0.001f && m_Ambience.isPlaying) m_Ambience.Stop();
        }

        void FindFilter()
        {
            var listener = GetComponent<AudioListener>();
            if (listener == null) listener = FindFirstObjectByType<AudioListener>();
            if (listener == null) return;
            m_Filter = listener.GetComponent<AudioLowPassFilter>();
            if (m_Filter != null) return;
            m_Filter = listener.gameObject.AddComponent<AudioLowPassFilter>();
            m_OwnsFilter = true;
        }

        void UpdateSpecks(Vector3 head, float dt)
        {
            var water = m_Water;
            if (IsUnderwater && water.Specks > 0f)
            {
                if (m_Specks == null)
                    m_Specks = new WaterParticles("Underwater Specks", null, WaterPixelArt.Speck, 900, 0f, 0f, 0.04f, WaterParticles.Side.Underwater, false, null);

                m_SpeckBudget += water.Specks * dt;
                float range = water.SpeckRange;
                for (int tries = 0; m_SpeckBudget >= 1f && tries < 32; tries++)
                {
                    m_SpeckBudget -= 1f;
                    Vector3 spot = head + Random.insideUnitSphere * range;
                    if (spot.y > water.RestHeight - water.Waves.Amplitude - 0.02f || spot.y < water.Bottom || !water.IsOverWater(spot)) continue;
                    Color color = water.SpeckColor;
                    color.r *= Random.Range(0.8f, 1.1f);
                    color.g *= Random.Range(0.8f, 1.1f);
                    m_Specks.Emit(spot, Random.insideUnitSphere * water.SpeckDrift, Random.Range(water.SpeckSize.x, water.SpeckSize.y), Random.Range(3f, 7f), color);
                }
                m_SpeckBudget = Mathf.Min(m_SpeckBudget, 4f);
            }
            m_Specks?.Cull(head, (water != null ? water.SpeckRange : 3f) * 1.6f);
        }
    }
}
