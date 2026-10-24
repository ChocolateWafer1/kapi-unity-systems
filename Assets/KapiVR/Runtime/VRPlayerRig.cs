using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace KapiVR
{
    /// <summary>
    /// A ready-to-go VR player: head tracking, two tracked controllers, and a laser on the right hand.
    /// Place this object where the player's feet should be and point its blue arrow the way they should face;
    /// at startup the player lands exactly there, wherever they are standing in their real room.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Kapi VR/VR Player")]
    public sealed class VRPlayerRig : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("Moved and turned at startup so the player lands on this object. Leave it at zero in the editor.")]
        [SerializeField] Transform m_TrackingSpace;
        [SerializeField] Camera m_Head;
        [SerializeField] VRLaserPointer m_Laser;

        [Header("Start Spot")]
        [Tooltip("Put the player exactly on this object, facing its blue arrow, no matter where they stand in their room.")]
        [SerializeField] bool m_SnapToStartSpot = true;
        [Tooltip("Snap back to the start spot when the player recenters their headset.")]
        [SerializeField] bool m_SnapAfterRecenter = true;
        [Tooltip("Head height used if the headset can't report where the floor is.")]
        [SerializeField, Range(1f, 2.2f)] float m_FallbackEyeHeight = 1.65f;

        [Header("Fade In")]
        [Tooltip("Start black and fade in once tracking is ready, which also hides the snap to the start spot.")]
        [SerializeField] bool m_FadeInOnStart = true;
        [SerializeField, Min(0f)] float m_FadeInDuration = 1.2f;
        [SerializeField] Material m_FadeMaterial;

        [Header("Opening Distortion")]
        [Tooltip("When the game first opens, the view starts fully distorted and quickly clears. Plays once per launch.")]
        [SerializeField] bool m_OpeningDistortion = true;
        [Tooltip("How warped the view starts.")]
        [SerializeField, Range(0f, 1f)] float m_DistortionStrength = 1f;
        [Tooltip("Seconds the view stays fully distorted before it starts to clear.")]
        [SerializeField, Min(0f)] float m_DistortionHold = 1f;
        [Tooltip("Seconds it takes to clear. Most of it clears right away, then it settles.")]
        [SerializeField, Min(0.05f)] float m_DistortionClearTime = 1.8f;
        [Tooltip("Fade from black this quickly when the distortion plays, so it's seen from the very start.")]
        [SerializeField, Min(0f)] float m_DistortionFadeIn = 0.35f;

        static readonly List<XRInputSubsystem> s_Subsystems = new List<XRInputSubsystem>();
        static bool s_OpeningPlayed;

        XRInputSubsystem m_Subsystem;
        VRScreenFader m_Fader;
        VRScreenDistortion m_Distortion;
        bool m_PlayOpening;
        float m_HeightOffset;
        float m_WaitTime;
        bool m_Snapped;
        bool m_SnapPending;

        public static VRPlayerRig current { get; private set; }

        public Camera head => m_Head;
        public Transform trackingSpace => m_TrackingSpace;
        public VRLaserPointer laser => m_Laser;
        public Material fadeMaterial => m_FadeMaterial;
        public VRScreenFader fader => m_Fader;

        void Awake()
        {
            EnsureParts();

            m_Fader = VRScreenFader.Attach(m_Head, m_FadeMaterial);
            if (m_Fader != null && m_FadeInOnStart && !VRSceneTransition.isTransitioning)
                m_Fader.alpha = 1f;

            // Only the first time the game opens, not when coming back to this scene later.
            m_PlayOpening = m_OpeningDistortion && !s_OpeningPlayed && !VRSceneTransition.isTransitioning && m_DistortionStrength > 0f;
            if (m_PlayOpening)
            {
                s_OpeningPlayed = true;
                m_Distortion = VRScreenDistortion.Attach(m_Head);
                if (m_Distortion != null) m_Distortion.strength = m_DistortionStrength;
            }
        }

        // Without a domain reload between Play sessions in the editor, statics would carry over; start fresh each time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_OpeningPlayed = false;
        }

        void OnEnable()
        {
            current = this;
        }

        void OnDisable()
        {
            if (current == this) current = null;
            SetSubsystem(null);
        }

        void Update()
        {
            FindSubsystem();

            if (!m_Snapped)
            {
                // Wait for the headset to start tracking, but never leave the player in the dark if it doesn't
                // (or if there's no headset at all, like pressing Play with VR off).
                m_WaitTime += Time.unscaledDeltaTime;
                if (HeadIsTracking() || m_WaitTime > (XRSettings.isDeviceActive ? 3f : 0.1f))
                {
                    m_Snapped = true;
                    SnapToStartSpot();
                    if (m_FadeInOnStart && m_Fader != null && !VRSceneTransition.isTransitioning)
                        StartCoroutine(FadeIn());
                    if (m_PlayOpening && m_Distortion != null)
                        StartCoroutine(ClearOpeningDistortion());
                }
            }
            else if (m_SnapPending)
            {
                m_SnapPending = false;
                SnapToStartSpot();
            }
        }

        /// <summary>
        /// Moves the tracking space so the player's head is straight above this object, facing its forward.
        /// Real-world height is kept, so a tall player is tall in the game too.
        /// </summary>
        public void SnapToStartSpot()
        {
            if (m_TrackingSpace == null || m_Head == null) return;

            if (!m_SnapToStartSpot)
            {
                m_TrackingSpace.localPosition = new Vector3(0f, m_HeightOffset, 0f);
                m_TrackingSpace.localRotation = Quaternion.identity;
                return;
            }

            Vector3 headPosition = m_Head.transform.localPosition;
            Vector3 headForward = m_Head.transform.localRotation * Vector3.forward;
            headForward.y = 0f;
            float yaw = headForward.sqrMagnitude > 1e-6f ? Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg : 0f;

            var turn = Quaternion.Euler(0f, -yaw, 0f);
            Vector3 turnedHead = turn * headPosition;
            m_TrackingSpace.localRotation = turn;
            m_TrackingSpace.localPosition = new Vector3(-turnedHead.x, m_HeightOffset, -turnedHead.z);
        }

        IEnumerator FadeIn()
        {
            // Let the first tracked frames settle before revealing the scene.
            yield return new WaitForSecondsRealtime(0.15f);
            yield return m_Fader.FadeTo(0f, m_PlayOpening ? m_DistortionFadeIn : m_FadeInDuration);
        }

        IEnumerator ClearOpeningDistortion()
        {
            yield return new WaitForSecondsRealtime(0.15f + m_DistortionHold);

            // Ease out: most of the distortion snaps away at once, then the last bit settles.
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / m_DistortionClearTime)
            {
                float remaining = 1f - t;
                m_Distortion.strength = m_DistortionStrength * remaining * remaining * remaining;
                yield return null;
            }

            Destroy(m_Distortion);
            m_Distortion = null;
        }

        bool HeadIsTracking()
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            return hmd != null && hmd.added && hmd.isTracked.isPressed && m_Head.transform.localPosition.sqrMagnitude > 1e-6f;
        }

        void FindSubsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running) return;

            SubsystemManager.GetSubsystems(s_Subsystems);
            XRInputSubsystem found = null;
            foreach (var subsystem in s_Subsystems)
            {
                if (subsystem.running)
                {
                    found = subsystem;
                    break;
                }
            }
            SetSubsystem(found);
        }

        void SetSubsystem(XRInputSubsystem subsystem)
        {
            if (m_Subsystem == subsystem) return;

            if (m_Subsystem != null) m_Subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            m_Subsystem = subsystem;
            if (m_Subsystem == null) return;

            m_Subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;

            // Floor tracking gives real standing height. If the headset can't do that, add a typical eye height.
            if ((m_Subsystem.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                m_Subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            m_HeightOffset = m_Subsystem.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor ? 0f : m_FallbackEyeHeight;

            if (m_Snapped) m_SnapPending = true;
        }

        void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            // Fires when the player recenters. The new head pose arrives next frame, so snap then.
            if (m_SnapAfterRecenter && m_Snapped) m_SnapPending = true;
        }

        void EnsureParts()
        {
            if (m_Head == null) m_Head = GetComponentInChildren<Camera>(true);
            if (m_Laser == null) m_Laser = GetComponentInChildren<VRLaserPointer>(true);

            if (m_TrackingSpace == null)
            {
                // Everything tracked has to sit under a child we can move, never under this object itself.
                m_TrackingSpace = new GameObject("Tracking Space").transform;
                m_TrackingSpace.SetParent(transform, false);
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i);
                    if (child != m_TrackingSpace) child.SetParent(m_TrackingSpace, false);
                }
            }

            if (m_Head == null) return;

            var headPose = m_Head.GetComponent<TrackedPoseDriver>();
            if (headPose == null) headPose = m_Head.gameObject.AddComponent<TrackedPoseDriver>();
            if (!VRInputBindings.IsConfigured(headPose)) VRInputBindings.Configure(headPose, VRPoseSource.Head);
        }

        void OnDrawGizmos()
        {
            // Feet and facing direction, so the start spot is easy to place in the Scene view.
            Gizmos.color = new Color(1f, 0.25f, 0.6f, 0.9f);
            var position = transform.position;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Gizmos.DrawLine(position + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.3f,
                    position + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.3f);
            }
            Gizmos.DrawLine(position, position + transform.forward * 0.6f);
            Gizmos.DrawLine(position + transform.forward * 0.6f, position + transform.forward * 0.45f + transform.right * 0.1f);
            Gizmos.DrawLine(position + transform.forward * 0.6f, position + transform.forward * 0.45f - transform.right * 0.1f);
        }
    }
}
