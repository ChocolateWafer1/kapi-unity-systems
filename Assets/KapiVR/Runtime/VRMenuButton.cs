using System.Collections.Generic;
using TextFX;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KapiVR
{
    /// <summary>
    /// Makes a world-space button feel great to use in VR: it springs toward the player, glows and tips toward
    /// the laser when hovered, sinks as the trigger is squeezed, then chimes, glows brighter and buzzes the
    /// controller when clicked. Optionally fades to black and loads a scene on click. Works with the mouse too.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    [AddComponentMenu("Kapi VR/VR Menu Button")]
    public sealed class VRMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("On Click")]
        [Tooltip("Scene to fade to when this button is clicked. Leave empty for a button that only plays its effects.")]
        [SerializeField] SceneReference m_SceneToLoad = new SceneReference();
        [SerializeField, Min(0f)] float m_FadeOutTime = 0.9f;
        [SerializeField, Min(0f)] float m_FadeInTime = 1f;

        [Header("Hover")]
        [SerializeField, Range(1f, 1.3f)] float m_HoverScale = 1.08f;
        [Tooltip("How far the button pops out toward the player, in meters.")]
        [SerializeField, Range(0f, 0.1f)] float m_HoverLift = 0.025f;
        [Tooltip("How far the button tips toward where the laser points, in degrees.")]
        [SerializeField, Range(0f, 20f)] float m_Tilt = 7f;
        [SerializeField] Color m_GlowColor = new Color(1f, 0.3f, 0.85f, 1f);
        [Tooltip("How far the glow reaches past the button, as a fraction of its height.")]
        [SerializeField, Range(0.05f, 1f)] float m_GlowSize = 0.35f;
        [Tooltip("Speeds up any Text Effects on the label while hovered.")]
        [SerializeField, Range(1f, 5f)] float m_TextSpeedBoost = 2.5f;

        [Header("Press")]
        [Tooltip("How far the button sinks as the trigger is squeezed, in meters.")]
        [SerializeField, Range(0f, 0.05f)] float m_PressDepth = 0.012f;
        [SerializeField, Range(0.7f, 1f)] float m_PressScale = 0.95f;
        [Tooltip("Controller vibration strength on click.")]
        [SerializeField, Range(0f, 1f)] float m_ClickHaptic = 0.9f;

        [Header("Sound")]
        [SerializeField, Range(0f, 1f)] float m_Volume = 0.6f;
        [Tooltip("Optional. Leave empty to use the built-in sound.")]
        [SerializeField] AudioClip m_HoverSound;
        [Tooltip("Optional. Leave empty to use the built-in sound.")]
        [SerializeField] AudioClip m_PressSound;
        [Tooltip("Optional. Leave empty to use the built-in sound.")]
        [SerializeField] AudioClip m_ClickSound;

        [Header("Fade")]
        [Tooltip("Optional. The VR Player's fade material is used when empty.")]
        [SerializeField] Material m_FadeMaterial;

        struct Spring
        {
            public float value;
            public float velocity;

            // A damped spring; damping below 1 overshoots a little, which is what makes things feel bouncy.
            public void Step(float target, float frequency, float damping, float deltaTime)
            {
                float omega = 2f * Mathf.PI * frequency;
                int steps = Mathf.CeilToInt(deltaTime / (1f / 240f));
                float step = deltaTime / Mathf.Max(steps, 1);
                for (int i = 0; i < steps; i++)
                {
                    velocity += (omega * omega * (target - value) - 2f * damping * omega * velocity) * step;
                    value += velocity * step;
                }
            }
        }

        readonly HashSet<int> m_Pointers = new HashSet<int>();

        Selectable m_Selectable;
        RectTransform m_Rect;
        AudioSource m_Audio;
        Image m_Glow;
        TextEffects[] m_TextEffects;
        float[] m_TextSpeeds;

        Vector3 m_BaseScale;
        Quaternion m_BaseRotation;
        float m_BaseZ;
        bool m_Initialized;

        Spring m_Hover;
        Spring m_Press;
        Spring m_TiltX;
        Spring m_TiltY;
        Vector2 m_Aim;
        float m_ClickGlow;
        bool m_PointerDown;
        bool m_Confirmed;

        public SceneReference sceneToLoad => m_SceneToLoad;

        void Awake()
        {
            m_Selectable = GetComponent<Selectable>();
            m_Rect = (RectTransform)transform;
            m_BaseScale = m_Rect.localScale;
            m_BaseRotation = m_Rect.localRotation;
            m_BaseZ = m_Rect.localPosition.z;
            m_Initialized = true;

            if (m_Selectable is Button button) button.onClick.AddListener(OnClicked);

            m_TextEffects = GetComponentsInChildren<TextEffects>(true);
            m_TextSpeeds = new float[m_TextEffects.Length];
            for (int i = 0; i < m_TextEffects.Length; i++) m_TextSpeeds[i] = m_TextEffects[i].speed;

            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;
            m_Audio.minDistance = 1.5f;
            m_Audio.maxDistance = 30f;
            m_Audio.dopplerLevel = 0f;

            BuildEffects();
        }

        void OnDisable()
        {
            if (!m_Initialized) return;

            m_Pointers.Clear();
            m_PointerDown = false;
            m_Hover = default;
            m_Press = default;
            m_TiltX = default;
            m_TiltY = default;
            m_Rect.localScale = m_BaseScale;
            m_Rect.localRotation = m_BaseRotation;
            var position = m_Rect.localPosition;
            position.z = m_BaseZ;
            m_Rect.localPosition = position;

            for (int i = 0; i < m_TextEffects.Length; i++)
                if (m_TextEffects[i] != null) m_TextEffects[i].speed = m_TextSpeeds[i];
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            m_SceneToLoad?.RefreshPath();
        }
#endif

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!m_Pointers.Add(eventData.pointerId) || m_Pointers.Count > 1) return;
            if (!m_Selectable.IsInteractable() || m_Confirmed) return;

            if (eventData.pointerId != VRLaserPointer.PointerId)
                m_Aim = AimOnButton(eventData.pointerCurrentRaycast.worldPosition);

            Play(m_HoverSound != null ? m_HoverSound : VRSynth.hover, Random.Range(0.97f, 1.06f), 0.7f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_Pointers.Remove(eventData.pointerId);
            if (m_Pointers.Count == 0) m_PointerDown = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !m_Selectable.IsInteractable() || m_Confirmed) return;

            m_PointerDown = true;
            Play(m_PressSound != null ? m_PressSound : VRSynth.press, Random.Range(0.96f, 1.03f), 0.9f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_PointerDown = false;
        }

        void OnClicked()
        {
            if (!isActiveAndEnabled || m_Confirmed) return;

            var laser = VRLaserPointer.current;
            bool byLaser = laser != null && laser.IsHovering(gameObject);

            m_ClickGlow = 1f;
            Play(m_ClickSound != null ? m_ClickSound : VRSynth.confirm, 1f, 1f);
            if (byLaser) VRHaptics.Pulse(laser.hand, m_ClickHaptic, 0.12f);

            if (m_SceneToLoad != null && m_SceneToLoad.isSet)
                m_Confirmed = VRSceneTransition.LoadScene(m_SceneToLoad.path, m_FadeOutTime, m_FadeInTime, m_FadeMaterial);
        }

        void LateUpdate()
        {
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float now = Time.unscaledTime;
            bool interactable = m_Selectable.IsInteractable();
            bool hovered = (m_Pointers.Count > 0 && interactable) || m_Confirmed;

            var laser = VRLaserPointer.current;
            bool laserOn = laser != null && laser.IsHovering(gameObject);
            if (laserOn && laser.hasHit) m_Aim = AimOnButton(laser.hitPoint);

            float press = m_Confirmed ? 0.35f : m_PointerDown ? 1f : laserOn && interactable ? laser.pressAmount : 0f;
            float lean = hovered ? m_Tilt * (0.6f + 0.4f * press) : 0f;

            m_Hover.Step(hovered ? 1f : 0f, 2.6f, 0.42f, deltaTime);
            m_Press.Step(press, 7f, 0.75f, deltaTime);
            // Pointing at the top tips the top away from the player; pointing right tips the right side away.
            m_TiltX.Step(m_Aim.y * lean, 3.5f, 0.6f, deltaTime);
            m_TiltY.Step(-m_Aim.x * lean, 3.5f, 0.6f, deltaTime);

            float hover = m_Hover.value;
            float sink = m_Press.value;

            // Move along the canvas's depth in meters, whatever the canvas is scaled to.
            var position = m_Rect.localPosition;
            position.z = m_BaseZ + (-m_HoverLift * hover + m_PressDepth * sink) / ParentDepthScale();
            m_Rect.localPosition = position;
            m_Rect.localScale = m_BaseScale * ((1f + (m_HoverScale - 1f) * hover) * Mathf.Lerp(1f, m_PressScale, sink));
            m_Rect.localRotation = m_BaseRotation * Quaternion.Euler(m_TiltX.value, m_TiltY.value, 0f);

            float shown = Mathf.Clamp01(hover);
            float pulse = 0.85f + 0.15f * Mathf.Sin(now * 5f);
            m_ClickGlow = Mathf.MoveTowards(m_ClickGlow, 0f, deltaTime * 3f);

            var glow = m_GlowColor;
            glow.a *= Mathf.Clamp01(shown * pulse * (1f + 0.5f * Mathf.Clamp01(sink)) + m_ClickGlow);
            m_Glow.color = glow;

            for (int i = 0; i < m_TextEffects.Length; i++)
                if (m_TextEffects[i] != null)
                    m_TextEffects[i].speed = m_TextSpeeds[i] * Mathf.Lerp(1f, m_TextSpeedBoost, shown);
        }

        void BuildEffects()
        {
            float height = Mathf.Max(m_Rect.rect.height, 1f);

            // Outline glow. Its middle is see-through, so it can sit on top of the button.
            m_Glow = CreateImage("VR Glow", VRUISprites.glowFrame, 0);
            m_Glow.type = Image.Type.Sliced;
            float margin = height * m_GlowSize;
            var canvas = GetComponentInParent<Canvas>();
            float referencePixelsPerUnit = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            m_Glow.pixelsPerUnitMultiplier = VRUISprites.GlowPixels * referencePixelsPerUnit / (100f * margin);
            Stretch(m_Glow.rectTransform, margin);
        }

        Image CreateImage(string name, Sprite sprite, int siblingIndex)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(m_Rect, false);
            rect.SetSiblingIndex(siblingIndex);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }

        static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-margin, -margin);
            rect.offsetMax = new Vector2(margin, margin);
        }

        Vector2 AimOnButton(Vector3 worldPoint)
        {
            Rect area = m_Rect.rect;
            Vector2 local = (Vector2)m_Rect.InverseTransformPoint(worldPoint) - area.center;
            return new Vector2(
                Mathf.Clamp(local.x / Mathf.Max(area.width * 0.5f, 1e-4f), -1f, 1f),
                Mathf.Clamp(local.y / Mathf.Max(area.height * 0.5f, 1e-4f), -1f, 1f));
        }

        float ParentDepthScale()
        {
            var parent = m_Rect.parent;
            if (parent == null) return 1f;

            Vector3 scale = parent.lossyScale;
            float depth = Mathf.Abs(scale.z) > 1e-6f ? scale.z : scale.x;
            return Mathf.Max(Mathf.Abs(depth), 1e-6f);
        }

        void Play(AudioClip clip, float pitch, float volume)
        {
            if (clip == null || m_Audio == null || m_Volume <= 0f) return;
            m_Audio.pitch = pitch;
            m_Audio.PlayOneShot(clip, volume * m_Volume);
        }
    }
}
