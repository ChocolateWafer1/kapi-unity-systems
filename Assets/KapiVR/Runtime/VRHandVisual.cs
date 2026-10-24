using UnityEngine;
using UnityEngine.InputSystem;

namespace KapiVR
{
    /// <summary>
    /// The visible hand: shown only while its controller is tracked, and squeezes and glows a little as the
    /// trigger or grip is pulled.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kapi VR/VR Hand Visual")]
    public sealed class VRHandVisual : MonoBehaviour
    {
        static readonly int s_GlowId = Shader.PropertyToID("_Glow");

        [SerializeField] VRHand m_Hand = VRHand.Left;
        [Tooltip("The object to show, hide and squeeze (the hand sphere).")]
        [SerializeField] Transform m_Visual;
        [Tooltip("Size of the hand when the trigger or grip is fully pulled.")]
        [SerializeField, Range(0.5f, 1f)] float m_SqueezeScale = 0.82f;
        [Tooltip("Extra brightness when the trigger or grip is fully pulled.")]
        [SerializeField, Range(0f, 2f)] float m_SqueezeGlow = 0.6f;

        InputAction m_Squeeze;
        Renderer m_Renderer;
        MaterialPropertyBlock m_Properties;
        Vector3 m_BaseScale = Vector3.one;
        float m_Amount;

        public VRHand hand => m_Hand;

        void Awake()
        {
            if (m_Visual == null && transform.childCount > 0) m_Visual = transform.GetChild(0);
            if (m_Visual == null) return;

            m_BaseScale = m_Visual.localScale;
            m_Renderer = m_Visual.GetComponent<Renderer>();
            m_Properties = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            string device = VRInputBindings.DevicePath(m_Hand);
            m_Squeeze = new InputAction("Squeeze", InputActionType.Value, device + "/trigger", expectedControlType: "Axis");
            m_Squeeze.AddBinding(device + "/grip");
            m_Squeeze.Enable();
        }

        void OnDisable()
        {
            m_Squeeze?.Dispose();
            m_Squeeze = null;
        }

        void Update()
        {
            if (m_Visual == null) return;

            bool tracked = VRInputBindings.IsTracked(m_Hand);
            if (m_Visual.gameObject.activeSelf != tracked) m_Visual.gameObject.SetActive(tracked);
            if (!tracked) return;

            float target = m_Squeeze != null ? m_Squeeze.ReadValue<float>() : 0f;
            m_Amount = Mathf.Lerp(m_Amount, target, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
            m_Visual.localScale = m_BaseScale * Mathf.Lerp(1f, m_SqueezeScale, m_Amount);

            if (m_Renderer != null)
            {
                m_Renderer.GetPropertyBlock(m_Properties);
                m_Properties.SetFloat(s_GlowId, m_Amount * m_SqueezeGlow);
                m_Renderer.SetPropertyBlock(m_Properties);
            }
        }
    }
}
