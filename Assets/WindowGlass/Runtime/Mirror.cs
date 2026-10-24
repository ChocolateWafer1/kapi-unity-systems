using UnityEngine;

namespace KapiWindowGlass
{
    /// <summary>
    /// Optional settings for a mirror. Any object with a Kapi/Mirror material already reflects, in the editor and in
    /// Play mode; add this component to change its quality or what it reflects.
    /// Works on flat objects such as a Quad, a Plane, or a thin Cube.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    [AddComponentMenu("Kapi/Mirror")]
    public sealed class Mirror : MonoBehaviour
    {
        [Header("Quality")]
        [Tooltip("Reflection resolution on PC, as a fraction of the headset (or screen) resolution. 1 = pixel-perfect.")]
        [SerializeField, Range(0.25f, 1f)] float m_Resolution = 1f;
        [Tooltip("Reflection resolution on Quest and other Android devices.")]
        [SerializeField, Range(0.25f, 1f)] float m_QuestResolution = 0.75f;
        [Tooltip("Draw shadows in the reflection on PC.")]
        [SerializeField] bool m_Shadows = true;
        [Tooltip("Draw shadows in the reflection on Quest. Off saves a lot of performance.")]
        [SerializeField] bool m_QuestShadows;

        [Header("What It Reflects")]
        [Tooltip("Layers that show up in the mirror. This is separate from what the main camera sees, so a player body hidden from your own view can still appear in mirrors.")]
        [SerializeField] LayerMask m_ReflectLayers = ~0;
        [Tooltip("Stop updating the reflection when the camera is farther away than this, in meters. 0 = always update.")]
        [SerializeField, Min(0f)] float m_MaxDistance;
        [Tooltip("Leave at 0 so a hand touching the mirror meets its reflection exactly. Positive values hide things very close to the surface.")]
        [SerializeField, Range(-0.02f, 0.02f)] float m_ClipOffset;

        static bool quest => Application.platform == RuntimePlatform.Android;

        public float resolution => quest ? m_QuestResolution : m_Resolution;
        public bool shadows => quest ? m_QuestShadows : m_Shadows;
        public LayerMask reflectLayers => m_ReflectLayers;
        public float maxDistance => m_MaxDistance;
        public float clipOffset => m_ClipOffset;

        void OnEnable()
        {
            var renderer = GetComponent<Renderer>();
            MirrorSystem.SetSettings(renderer, this);

            if (!MirrorSystem.UsesMirrorMaterial(renderer))
                Debug.LogWarning($"[Kapi] '{name}' has a Mirror component but no material using the Kapi/Mirror shader, so it won't reflect.", this);
        }

        void OnDisable()
        {
            MirrorSystem.SetSettings(GetComponent<Renderer>(), null);
        }
    }
}
