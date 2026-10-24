using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace KapiVR
{
    /// <summary>
    /// A laser from the controller that points at world-space UI. It sends normal UI pointer events, so any
    /// Button, Toggle or other Selectable reacts to it like it would to a mouse; the trigger (or A button) clicks.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kapi VR/VR Laser Pointer")]
    public sealed class VRLaserPointer : MonoBehaviour, ISerializationCallbackReceiver
    {
        /// <summary>Pointer id the laser uses for UI events, so buttons can tell it apart from the mouse.</summary>
        public const int PointerId = -4242;

        static readonly int s_ColorId = Shader.PropertyToID("_Color");
        static readonly int s_IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int s_RingId = Shader.PropertyToID("_Ring");
        static readonly int s_RingRadiusId = Shader.PropertyToID("_RingRadius");

        [Header("Parts")]
        [SerializeField] LineRenderer m_Line;
        [Tooltip("The glowing dot where the laser lands. Keep it outside the hand so it doesn't wobble.")]
        [SerializeField] Transform m_Cursor;
        [SerializeField] VRHand m_Hand = VRHand.Right;

        [Header("Laser")]
        [Tooltip("How far the laser reaches when it isn't hitting anything, in meters.")]
        [SerializeField, Min(0.5f)] float m_MaxLength = 150f;
        [Tooltip("Thickness of the beam at the hand, in meters.")]
        [SerializeField, Range(0.0005f, 0.02f)] float m_Width = 0.005f;
        [Tooltip("Thickness of the beam at the hand while it's on a button.")]
        [SerializeField, Range(0.0005f, 0.03f)] float m_HoverWidth = 0.0075f;
        [Tooltip("Widen the beam with distance so it looks just as thick far away as it does at your hand, instead of thinning out to nothing.")]
        [SerializeField] bool m_SameThicknessFar = true;
        [SerializeField] Color m_Color = new Color(1f, 0.05f, 0.04f, 1f);
        [SerializeField] Color m_HoverColor = new Color(1f, 0.18f, 0.12f, 1f);
        [Tooltip("Brightness of the beam. Above about 1 the color starts turning pale, which makes it harder to see against a bright sky.")]
        [SerializeField, Range(0f, 2f)] float m_Intensity = 1f;
        [SerializeField, Range(0f, 2f)] float m_HoverIntensity = 1.15f;
        [Tooltip("Colliders on these layers stop the laser.")]
        [SerializeField] LayerMask m_PhysicsLayers = Physics.DefaultRaycastLayers;

        [Header("Cursor")]
        [Tooltip("Cursor size per meter of distance, so it looks the same size near and far.")]
        [SerializeField, Range(0.001f, 0.05f)] float m_CursorSize = 0.014f;

        [Header("Clicking")]
        [Tooltip("How far the trigger must be pulled to press.")]
        [SerializeField, Range(0.1f, 1f)] float m_PressThreshold = 0.75f;
        [Tooltip("How far the trigger must be let go to release (and click).")]
        [SerializeField, Range(0f, 0.9f)] float m_ReleaseThreshold = 0.4f;
        [Tooltip("Let the A button press too.")]
        [SerializeField] bool m_PrimaryButtonClicks = true;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] float m_HoverHaptic = 0.25f;
        [SerializeField, Range(0f, 1f)] float m_PressHaptic = 0.6f;

        // Lets older saved lasers pick up improved defaults without losing values someone changed on purpose.
        const int k_SettingsVersion = 3;
        const float k_HandToEyeDistance = 0.45f;
        [SerializeField, HideInInspector] int m_SettingsVersion;

        struct UIHit
        {
            public Graphic graphic;
            public Canvas canvas;
            public BaseRaycaster module;
            public float distance;
            public Vector3 point;
            public Vector3 normal;
        }

        struct PatchedModule
        {
            public InputSystemUIInputModule module;
            public InputActionAsset original;
            public InputActionAsset copy;
        }

        static readonly List<Canvas> s_Canvases = new List<Canvas>();
        static float s_NextCanvasRefresh;

        readonly List<PatchedModule> m_PatchedModules = new List<PatchedModule>();
        readonly GradientColorKey[] m_ColorKeys = new GradientColorKey[2];
        readonly GradientAlphaKey[] m_AlphaKeys = new GradientAlphaKey[3];
        readonly Gradient m_Gradient = new Gradient();
        readonly Keyframe[] m_WidthKeys = new Keyframe[2];
        readonly AnimationCurve m_WidthCurve = new AnimationCurve();

        InputAction m_Trigger;
        InputAction m_PrimaryButton;
        PointerEventData m_EventData;
        EventSystem m_EventSystem;
        MaterialPropertyBlock m_LineProperties;
        MaterialPropertyBlock m_CursorProperties;
        Renderer m_CursorRenderer;
        Vector3 m_HitNormal;
        float m_Length;
        float m_Hover;
        float m_Flash;
        float m_Reveal;
        float m_CursorPop;
        float m_TrackedTime;
        float m_StatusTime;
        bool m_AimChecked;
        bool m_AimMoved;
        bool m_StatusLogged;
        Vector3 m_StartAimPosition;
        Quaternion m_StartAimRotation;

        public static VRLaserPointer current { get; private set; }

        /// <summary>Turn off to stop the laser from pointing (it retracts into the hand).</summary>
        public bool interactable { get; set; } = true;

        public bool hasHit { get; private set; }
        public Vector3 hitPoint { get; private set; }
        public VRHand hand => m_Hand;

        /// <summary>The UI object the laser is on, if any.</summary>
        public GameObject hoveredObject => m_EventData != null ? m_EventData.pointerEnter : null;

        /// <summary>The interactable button (or other Selectable) the laser is on, if any.</summary>
        public Selectable hoveredSelectable { get; private set; }

        /// <summary>Raw trigger value, 0 to 1.</summary>
        public float triggerValue { get; private set; }

        /// <summary>How close the trigger is to pressing: 0 = let go, 1 = pressed.</summary>
        public float pressAmount { get; private set; }

        public bool isPressed { get; private set; }

        /// <summary>True if the laser is on this object or one of its children.</summary>
        public bool IsHovering(GameObject target)
        {
            return m_EventData != null && target != null && m_EventData.hovered.Contains(target);
        }

        void Awake()
        {
            m_LineProperties = new MaterialPropertyBlock();
            m_CursorProperties = new MaterialPropertyBlock();
            m_StartAimPosition = transform.localPosition;
            m_StartAimRotation = transform.localRotation;

            if (m_Line != null)
            {
                m_Line.useWorldSpace = false;
                m_Line.positionCount = 2;
                m_Line.SetPosition(0, Vector3.zero);
                m_Line.enabled = false;
            }

            if (m_Cursor != null)
            {
                m_CursorRenderer = m_Cursor.GetComponent<Renderer>();
                if (m_CursorRenderer != null) m_CursorRenderer.enabled = false;
            }
        }

        void OnEnable()
        {
            current = this;

            string device = VRInputBindings.DevicePath(m_Hand);
            m_Trigger = new InputAction("Laser Trigger", InputActionType.Value, device + "/trigger", expectedControlType: "Axis");
            m_PrimaryButton = new InputAction("Laser Button", InputActionType.Button, device + "/primaryButton");
            m_Trigger.Enable();
            m_PrimaryButton.Enable();

            DetachControllersFromUIModules();
        }

        void OnDisable()
        {
            ClearPointer();

            m_Trigger?.Dispose();
            m_PrimaryButton?.Dispose();
            m_Trigger = null;
            m_PrimaryButton = null;

            RestoreUIModules();

            if (m_Line != null) m_Line.enabled = false;
            if (m_CursorRenderer != null) m_CursorRenderer.enabled = false;
            if (current == this) current = null;
        }

        void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            bool tracked = VRInputBindings.IsTracked(m_Hand);
            CheckAimPose(tracked, deltaTime);
            LogStatusOnce(tracked, deltaTime);

            float buttonValue = m_PrimaryButtonClicks && m_PrimaryButton.IsPressed() ? 1f : 0f;
            triggerValue = Mathf.Max(m_Trigger.ReadValue<float>(), buttonValue);

            bool active = tracked && interactable && !VRSceneTransition.isTransitioning;
            m_Reveal = Mathf.MoveTowards(m_Reveal, active ? 1f : 0f, deltaTime / (active ? 0.25f : 0.35f));

            if (!active)
            {
                ClearPointer();
                hasHit = false;
                pressAmount = 0f;
                UpdateVisuals(deltaTime, m_Length);
                return;
            }

            var ray = new Ray(transform.position, transform.forward);
            bool onUI = RaycastUI(ray, m_MaxLength, out var uiHit);
            bool onCollider = Physics.Raycast(ray, out var physicsHit, m_MaxLength, m_PhysicsLayers, QueryTriggerInteraction.Ignore);

            // A collider in front of the UI blocks it.
            if (onUI && onCollider && physicsHit.distance < uiHit.distance - 0.001f) onUI = false;

            float length = m_MaxLength;
            hasHit = onUI || onCollider;
            if (onUI)
            {
                hitPoint = uiHit.point;
                m_HitNormal = uiHit.normal;
                length = uiHit.distance;
            }
            else if (onCollider)
            {
                hitPoint = physicsHit.point;
                m_HitNormal = physicsHit.normal;
                length = physicsHit.distance;
            }

            UpdatePointer(onUI ? uiHit.graphic.gameObject : null, uiHit);
            UpdatePress();
            UpdateVisuals(deltaTime, length);
        }

        // ---------------------------------------------------------------- UI events

        void UpdatePointer(GameObject target, in UIHit hit)
        {
            var data = GetEventData();
            if (target != null)
            {
                var camera = EventCamera(hit.canvas);
                Vector2 screen = camera != null ? (Vector2)camera.WorldToScreenPoint(hit.point) : Vector2.zero;
                data.delta = screen - data.position;
                data.position = screen;
                data.pointerCurrentRaycast = new RaycastResult
                {
                    gameObject = target,
                    module = hit.module,
                    distance = hit.distance,
                    worldPosition = hit.point,
                    worldNormal = hit.normal,
                    screenPosition = screen,
                    depth = hit.graphic.depth,
                    sortingLayer = hit.canvas.sortingLayerID,
                    sortingOrder = hit.canvas.sortingOrder,
                };
            }
            else
            {
                data.pointerCurrentRaycast = default;
            }

            SetHovered(data, target);

            var selectable = target != null ? target.GetComponentInParent<Selectable>() : null;
            if (selectable != null && !selectable.IsInteractable()) selectable = null;
            if (selectable != hoveredSelectable)
            {
                hoveredSelectable = selectable;
                if (selectable != null)
                {
                    VRHaptics.Pulse(m_Hand, m_HoverHaptic, 0.03f);
                    m_Flash = 1f;
                    m_CursorPop = 1f;
                }
            }
        }

        void UpdatePress()
        {
            if (!isPressed && triggerValue >= m_PressThreshold)
            {
                isPressed = true;
                Press();
            }
            else if (isPressed && triggerValue <= m_ReleaseThreshold)
            {
                isPressed = false;
                Release();
            }

            pressAmount = isPressed ? 1f : Mathf.Clamp01(triggerValue / m_PressThreshold);
        }

        void Press()
        {
            var data = GetEventData();
            var target = data.pointerCurrentRaycast.gameObject;
            VRHaptics.Pulse(m_Hand, target != null ? m_PressHaptic : m_PressHaptic * 0.3f, 0.05f);
            if (target == null) return;

            m_Flash = 1f;
            data.eligibleForClick = true;
            data.delta = Vector2.zero;
            data.dragging = false;
            data.useDragThreshold = true;
            data.pressPosition = data.position;
            data.pointerPressRaycast = data.pointerCurrentRaycast;

            var pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            if (pressed == null) pressed = clickHandler;

            data.pointerPress = pressed;
            data.rawPointerPress = target;
            data.clickTime = Time.unscaledTime;
            data.clickCount = 1;
        }

        void Release()
        {
            var data = GetEventData();
            if (data.pointerPress != null)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);

                // Like a mouse: it only clicks if the laser is still on the thing that was pressed.
                var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(data.pointerCurrentRaycast.gameObject);
                if (data.eligibleForClick && data.pointerPress == clickHandler)
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
            }

            data.eligibleForClick = false;
            data.pointerPress = null;
            data.rawPointerPress = null;

            // Don't leave the button stuck in its "selected" look after clicking.
            if (m_EventSystem != null && m_EventSystem.currentSelectedGameObject != null)
                m_EventSystem.SetSelectedGameObject(null);
        }

        void ClearPointer()
        {
            if (m_EventData == null) return;

            if (isPressed)
            {
                isPressed = false;
                m_EventData.pointerCurrentRaycast = default;
                Release();
            }

            m_EventData.pointerCurrentRaycast = default;
            SetHovered(m_EventData, null);
            hoveredSelectable = null;
        }

        PointerEventData GetEventData()
        {
            var eventSystem = EventSystem.current;
            if (m_EventData != null && m_EventSystem == eventSystem) return m_EventData;

            if (m_EventData != null)
            {
                // Let go of everything under the old event system before switching (sets it first so this doesn't recurse).
                m_EventSystem = eventSystem;
                ClearPointer();
            }

            m_EventSystem = eventSystem;
            m_EventData = new PointerEventData(eventSystem)
            {
                pointerId = PointerId,
                button = PointerEventData.InputButton.Left,
            };
            return m_EventData;
        }

        // Sends exit events to what the laser left and enter events to what it reached, like the mouse does.
        static void SetHovered(PointerEventData data, GameObject target)
        {
            if (data.pointerEnter == target) return;

            var common = CommonRoot(data.pointerEnter, target);
            if (data.pointerEnter != null)
            {
                for (var t = data.pointerEnter.transform; t != null && (common == null || t.gameObject != common); t = t.parent)
                {
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerExitHandler);
                    data.hovered.Remove(t.gameObject);
                }
            }

            data.pointerEnter = target;
            if (target != null)
            {
                for (var t = target.transform; t != null && t.gameObject != common; t = t.parent)
                {
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerEnterHandler);
                    data.hovered.Add(t.gameObject);
                }
            }
        }

        static GameObject CommonRoot(GameObject a, GameObject b)
        {
            if (a == null || b == null) return null;

            for (var ta = a.transform; ta != null; ta = ta.parent)
                for (var tb = b.transform; tb != null; tb = tb.parent)
                    if (ta == tb) return ta.gameObject;
            return null;
        }

        // ---------------------------------------------------------------- UI raycast

        bool RaycastUI(Ray ray, float maxDistance, out UIHit best)
        {
            best = default;
            bool found = false;

            if (Time.unscaledTime >= s_NextCanvasRefresh)
            {
                s_NextCanvasRefresh = Time.unscaledTime + 0.5f;
                s_Canvases.Clear();
                s_Canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsSortMode.None));
            }

            foreach (var canvas in s_Canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace) continue;

                // Only canvases set up for clicking (they have a Graphic Raycaster), same as with a mouse.
                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.isActiveAndEnabled) continue;

                var eventCamera = EventCamera(canvas);
                var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
                for (int i = 0; i < graphics.Count; i++)
                {
                    var graphic = graphics[i];
                    if (graphic.depth == -1 || !graphic.raycastTarget || graphic.canvasRenderer.cull) continue;

                    var rect = graphic.rectTransform;
                    Vector3 forward = rect.forward;
                    float facing = Vector3.Dot(ray.direction, forward);
                    if (Mathf.Abs(facing) < 1e-4f) continue;

                    float distance = Vector3.Dot(rect.position - ray.origin, forward) / facing;
                    if (distance < 0f || distance > maxDistance) continue;
                    if (found && distance > best.distance + 0.001f) continue;

                    Vector3 point = ray.GetPoint(distance);
                    Vector2 local = rect.InverseTransformPoint(point);
                    Rect area = rect.rect;
                    Vector4 padding = graphic.raycastPadding;
                    if (local.x < area.xMin + padding.x || local.x > area.xMax - padding.z ||
                        local.y < area.yMin + padding.y || local.y > area.yMax - padding.w) continue;

                    // Respects Canvas Groups, Masks and alpha hit testing, like the regular UI raycaster.
                    if (eventCamera != null && !graphic.Raycast(eventCamera.WorldToScreenPoint(point), eventCamera)) continue;

                    bool closer = !found || distance < best.distance - 0.001f;
                    bool drawnOnTop = found && !closer &&
                        (canvas.sortingOrder > best.canvas.sortingOrder || (canvas == best.canvas && graphic.depth > best.graphic.depth));
                    if (!closer && !drawnOnTop) continue;

                    found = true;
                    best = new UIHit
                    {
                        graphic = graphic,
                        canvas = canvas,
                        module = raycaster,
                        distance = distance,
                        point = point,
                        normal = facing > 0f ? -forward : forward,
                    };
                }
            }
            return found;
        }

        static Camera EventCamera(Canvas canvas)
        {
            if (canvas != null && canvas.worldCamera != null) return canvas.worldCamera;
            var rig = VRPlayerRig.current;
            return rig != null && rig.head != null ? rig.head : Camera.main;
        }

        // ---------------------------------------------------------------- Visuals

        void UpdateVisuals(float deltaTime, float targetLength)
        {
            float hovering = hoveredSelectable != null ? 1f : 0f;
            m_Hover += (hovering - m_Hover) * (1f - Mathf.Exp(-18f * deltaTime));
            m_Flash = Mathf.MoveTowards(m_Flash, 0f, deltaTime * 4f);
            m_CursorPop = Mathf.MoveTowards(m_CursorPop, 0f, deltaTime * 5f);

            // Snap to whatever the laser hits; ease out to full length when it hits nothing.
            m_Length = hasHit ? targetLength : Mathf.Lerp(m_Length, targetLength, 1f - Mathf.Exp(-10f * deltaTime));
            float reveal = 1f - Mathf.Pow(1f - m_Reveal, 3f);
            float length = Mathf.Max(m_Length * reveal, 0.0001f);

            var color = Color.Lerp(m_Color, m_HoverColor, m_Hover);
            float charge = pressAmount * m_Hover;
            // Kept modest: pushing the brightness up turns the red pale, which is what makes it vanish against the sky.
            float intensity = Mathf.Lerp(m_Intensity, m_HoverIntensity, m_Hover) * (1f + 0.15f * m_Flash);

            if (m_Line != null)
            {
                m_Line.enabled = reveal > 0.001f;
                if (m_Line.enabled)
                {
                    m_Line.SetPosition(1, new Vector3(0f, 0f, length));

                    // Something thin far away covers less than a pixel and vanishes. Growing the width in step with
                    // the distance from the eyes keeps the beam looking equally thick all the way out.
                    float width = Mathf.Lerp(m_Width, m_HoverWidth, m_Hover) * (1f + 0.6f * m_Flash + 0.2f * charge);
                    float farWidth = m_SameThicknessFar ? width * (1f + length / k_HandToEyeDistance) : width;
                    float slope = farWidth - width;
                    m_WidthKeys[0] = new Keyframe(0f, width, slope, slope);
                    m_WidthKeys[1] = new Keyframe(1f, farWidth, slope, slope);
                    m_WidthCurve.keys = m_WidthKeys;
                    m_Line.widthCurve = m_WidthCurve;
                    m_Line.widthMultiplier = 1f;

                    // Fade in out of the hand over the first two centimeters, then fully solid all the way out.
                    float fadeIn = Mathf.Clamp(0.02f / length, 0f, 0.3f);
                    m_ColorKeys[0] = new GradientColorKey(color, 0f);
                    m_ColorKeys[1] = new GradientColorKey(color, 1f);
                    m_AlphaKeys[0] = new GradientAlphaKey(0f, 0f);
                    m_AlphaKeys[1] = new GradientAlphaKey(1f, fadeIn);
                    m_AlphaKeys[2] = new GradientAlphaKey(1f, 1f);
                    m_Gradient.SetKeys(m_ColorKeys, m_AlphaKeys);
                    m_Line.colorGradient = m_Gradient;

                    m_Line.GetPropertyBlock(m_LineProperties);
                    m_LineProperties.SetFloat(s_IntensityId, intensity);
                    m_Line.SetPropertyBlock(m_LineProperties);
                }
            }

            if (m_Cursor != null && m_CursorRenderer != null)
            {
                bool show = hasHit && m_Reveal > 0.6f;
                m_CursorRenderer.enabled = show;
                if (show)
                {
                    float distance = Vector3.Distance(transform.position, hitPoint);
                    float size = m_CursorSize * Mathf.Max(distance, 0.25f)
                        * (1f + 0.6f * m_Hover + 0.5f * m_CursorPop) * (1f - 0.25f * charge);

                    m_Cursor.position = hitPoint + m_HitNormal * 0.002f;
                    m_Cursor.rotation = Quaternion.LookRotation(-m_HitNormal);
                    m_Cursor.localScale = Vector3.one * size;

                    m_CursorRenderer.GetPropertyBlock(m_CursorProperties);
                    m_CursorProperties.SetColor(s_ColorId, color);
                    m_CursorProperties.SetFloat(s_IntensityId, intensity);
                    // A ring appears around the dot on buttons and closes in as the trigger is squeezed.
                    m_CursorProperties.SetFloat(s_RingId, m_Hover);
                    m_CursorProperties.SetFloat(s_RingRadiusId, Mathf.Lerp(0.95f, 0.42f, charge));
                    m_CursorRenderer.SetPropertyBlock(m_CursorProperties);
                }
            }
        }

        // ---------------------------------------------------------------- Setup helpers

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_SettingsVersion >= k_SettingsVersion) return;

            // Upgrade only values still at an older version's defaults, so anything changed by hand is kept.
            if (m_SettingsVersion < 2)
            {
                // Version 1 stopped at 12 m and was too faint.
                if (Mathf.Approximately(m_MaxLength, 12f)) m_MaxLength = 150f;
                if (Mathf.Approximately(m_Width, 0.0035f)) m_Width = 0.004f;
                if (Mathf.Approximately(m_Intensity, 1.3f)) m_Intensity = 2f;
                if (Mathf.Approximately(m_HoverIntensity, 2.8f)) m_HoverIntensity = 3.5f;
            }

            if (m_SettingsVersion < 3)
            {
                // Version 2 was so bright it bloomed into a pale haze that vanished against a bright sky.
                if (Mathf.Approximately(m_Width, 0.004f)) m_Width = 0.005f;
                if (Mathf.Approximately(m_HoverWidth, 0.0065f)) m_HoverWidth = 0.0075f;
                if (Mathf.Approximately(m_Intensity, 2f)) m_Intensity = 1f;
                if (Mathf.Approximately(m_HoverIntensity, 3.5f)) m_HoverIntensity = 1.15f;
                if (m_HoverColor == new Color(1f, 0.3f, 0.22f, 1f)) m_HoverColor = new Color(1f, 0.18f, 0.12f, 1f);
            }

            m_SettingsVersion = k_SettingsVersion;
        }

        // Some setups don't report a separate pointing pose. If the aim hasn't moved from where it started after the
        // controller has been tracked for a moment, follow the grip pose instead.
        void CheckAimPose(bool tracked, float deltaTime)
        {
            if (!m_AimMoved && (transform.localPosition != m_StartAimPosition || transform.localRotation != m_StartAimRotation))
                m_AimMoved = true;

            if (m_AimChecked || !tracked) return;

            m_TrackedTime += deltaTime;
            if (m_TrackedTime < 0.75f) return;

            m_AimChecked = true;
            if (m_AimMoved) return;

            var driver = GetComponent<TrackedPoseDriver>();
            if (driver == null) return;

            VRInputBindings.Configure(driver, m_Hand == VRHand.Left ? VRPoseSource.LeftHand : VRPoseSource.RightHand);
            Debug.Log("[Kapi VR] The controller isn't reporting a pointing direction, so the laser follows the grip instead.", this);
        }

        // One line in the Console a few seconds in, saying what the laser sees. Makes "I can't see it" quick to solve.
        void LogStatusOnce(bool tracked, float deltaTime)
        {
            if (m_StatusLogged) return;

            m_StatusTime += deltaTime;
            if (m_StatusTime < 3f) return;
            m_StatusLogged = true;

            var controller = VRInputBindings.FindController(m_Hand);
            string verdict =
                controller == null ? "No controller found for this hand. Is it on and paired, and is OpenXR running?" :
                !tracked ? "The controller is found but not tracked. Check it's awake and in view of the headset." :
                !interactable ? "The laser is switched off (interactable = false)." :
                VRSceneTransition.isTransitioning ? "A scene transition is running." :
                !m_AimMoved ? "The laser's aim isn't moving yet." :
                "The laser should be showing.";

            Debug.Log($"[Kapi VR] Laser check ({m_Hand} hand): controller = {(controller != null ? controller.name : "none")}, " +
                $"tracked = {tracked}, aim moving = {m_AimMoved}, drawn = {m_Line != null && m_Line.enabled}, " +
                $"position = {transform.position}. {verdict}", this);
        }

        // The scene's EventSystem also treats VR controllers as UI pointers by default. While the laser is here it
        // does the pointing, so give the UI module a copy of its actions with the controller bindings switched off.
        // This only lasts while this scene is loaded; nothing on disk changes.
        void DetachControllersFromUIModules()
        {
            foreach (var module in FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var original = module.actionsAsset;
                if (original == null) continue;

                var copy = Instantiate(original);
                copy.name = original.name + " (laser)";
                foreach (var action in copy)
                {
                    var bindings = action.bindings;
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        string path = bindings[i].path;
                        if (!string.IsNullOrEmpty(path) &&
                            (path.Contains("XRController") || path.Contains("XRHMD") || path.Contains("TrackedDevice")))
                            action.ApplyBindingOverride(i, string.Empty);
                    }
                }

                module.actionsAsset = copy;
                m_PatchedModules.Add(new PatchedModule { module = module, original = original, copy = copy });
            }
        }

        void RestoreUIModules()
        {
            foreach (var patched in m_PatchedModules)
            {
                if (patched.module != null && patched.module.actionsAsset == patched.copy)
                    patched.module.actionsAsset = patched.original;
                if (patched.copy != null) Destroy(patched.copy);
            }
            m_PatchedModules.Clear();
        }
    }
}
