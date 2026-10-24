using System;
using System.Collections.Generic;
using GorillaLocomotion;
using UnityEngine;
using UnityEngine.Audio;

namespace KapiVR
{
    /// <summary>
    /// Put on a hand controller. When that hand smacks into anything solid it plays the hit sound and vibrates that
    /// controller, louder and stronger the harder you hit. A hand resting or dragging on something doesn't keep
    /// re-triggering; a real slap always counts, even if the hand only lifted a little.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kapi VR/Hand Hit Feedback")]
    public sealed class HandHitFeedback : MonoBehaviour
    {
        const float k_Skin = 0.01f;
        const float k_TeleportDistance = 0.75f;
        const float k_SpeedWindow = 0.05f;
        const float k_OnsetThreshold = 0.05f;
        const float k_OnsetLead = 0.002f;
        const int k_VelocitySamples = 8;
        const int k_Voices = 4;

        static readonly RaycastHit[] s_Hits = new RaycastHit[16];
        static readonly Collider[] s_Overlaps = new Collider[32];

        [Tooltip("Which controller vibrates. Set automatically when you add the script.")]
        [SerializeField] VRHand m_Hand = VRHand.Right;

        [Tooltip("Where the hand really is. Set automatically to the Gorilla Locomotion hand follower (the visible hand that stays planted on surfaces). Empty = this object.")]
        [SerializeField] Transform m_HandPoint;

        [Header("Hit Detection")]
        [Tooltip("Layers that count as hits. Trigger colliders never do.")]
        [SerializeField] LayerMask m_HitLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Size of the hand. Match Gorilla Locomotion's Minimum Raycast Distance (0.05).")]
        [SerializeField, Min(0.005f)] float m_HandRadius = 0.05f;

        [Tooltip("Swinging your arm back down onto a surface faster than this (meters per second) is always a new hit, however little the hand lifted. Slower than this, the hand has to lift Lift To Hit Again first, so resting, dragging and your body bouncing on a planted hand don't re-trigger.")]
        [SerializeField, Min(0f)] float m_QuickTapSpeed = 0.6f;

        [Tooltip("How far the hand has to lift off a surface before even a gentle touch on it is a new hit.")]
        [SerializeField, Min(0f)] float m_LiftToHitAgain = 0.03f;

        [Tooltip("Shortest time between two hits on this hand, in seconds.")]
        [SerializeField, Min(0f)] float m_MinTimeBetweenHits = 0.06f;

        [Tooltip("Colliders under this object never count (your own head, body and hands). Set to the player rig's root.")]
        [SerializeField] Transform m_IgnoreCollidersUnder;

        [Header("Hit Strength")]
        [Tooltip("Hits at or below this speed (meters per second, into the surface) play at Min Volume and Min Vibration.")]
        [SerializeField, Min(0f)] float m_SoftHitSpeed = 0.3f;

        [Tooltip("Hits at or above this speed play at Max Volume and Max Vibration.")]
        [SerializeField, Min(0.01f)] float m_HardHitSpeed = 4f;

        [Header("Sound")]
        [Tooltip("The sound every hit plays. Its Load Type should be Decompress On Load (the default) so the script can tidy it up.")]
        [SerializeField] AudioClip m_HitSound;

        [SerializeField, Range(0f, 1f)] float m_MinVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] float m_MaxVolume = 1f;

        [Tooltip("Cuts the silence at the start of the sound so it plays the instant your hand lands.")]
        [SerializeField] bool m_TrimSilence = true;

        [Tooltip("Makes a quiet sound louder. 1 = as recorded (just brought up to full level). Higher squeezes the loud peak so the rest can come up, without harsh distortion.")]
        [SerializeField, Range(1f, 6f)] float m_Boost = 2f;

        [Tooltip("Each hit's pitch goes randomly up or down by up to this much (0.08 = 8%). 0 turns it off.")]
        [SerializeField, Range(0f, 0.5f)] float m_PitchVariation = 0.08f;

        [Tooltip("0 = same in both ears, 1 = comes from where your hand hit.")]
        [SerializeField, Range(0f, 1f)] float m_SpatialBlend = 1f;

        [Tooltip("Optional mixer group for the hit sounds.")]
        [SerializeField] AudioMixerGroup m_Output;

        [Header("Vibration")]
        [SerializeField] bool m_Vibrate = true;
        [SerializeField, Range(0f, 1f)] float m_MinVibration = 0.1f;
        [SerializeField, Range(0f, 1f)] float m_MaxVibration = 0.8f;

        [Tooltip("How long every hit vibrates, in seconds.")]
        [SerializeField, Min(0f)] float m_VibrationLength = 0.06f;

        // Surfaces the hand has touched and not yet lifted away from.
        readonly List<Collider> m_Engaged = new List<Collider>();
        // Surfaces within the hand's reach last frame, and this frame.
        List<Collider> m_Touching = new List<Collider>();
        List<Collider> m_TouchingNow = new List<Collider>();
        readonly Vector3[] m_Velocities = new Vector3[k_VelocitySamples];
        readonly Vector3[] m_ArmVelocities = new Vector3[k_VelocitySamples];
        readonly float[] m_VelocityTimes = new float[k_VelocitySamples];
        int m_VelocityIndex;
        Transform m_Point;
        Transform m_IgnoreRoot;
        Vector3 m_LastPosition;
        Vector3 m_LastArmPosition;
        float m_NextHitTime;
        AudioSource[] m_Voices;
        int m_NextVoice;
        AudioClip m_Prepared;
        AudioClip m_PreparedFrom;
        bool m_PreparedTrim;
        float m_PreparedBoost;

        /// <summary>Called on every hit with where it landed and how hard it was (0 = soft, 1 = hard).</summary>
        public event Action<Vector3, float> Hit;

        public VRHand Hand => m_Hand;

        void Reset()
        {
            var player = FindFirstObjectByType<Player>();
            if (player != null) m_HandRadius = player.minimumRaycastDistance;

            if (player != null && player.leftHandTransform == transform) m_Hand = VRHand.Left;
            else if (player != null && player.rightHandTransform == transform) m_Hand = VRHand.Right;
            else if (name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0) m_Hand = VRHand.Left;
            else if (name.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0) m_Hand = VRHand.Right;
            else m_Hand = LayerMask.LayerToName(gameObject.layer) == "LeftHand" ? VRHand.Left : VRHand.Right;

            m_HandPoint = FollowerFor(player);
            m_IgnoreCollidersUnder = transform.root;
        }

        void OnValidate()
        {
            m_HardHitSpeed = Mathf.Max(m_HardHitSpeed, m_SoftHitSpeed + 0.01f);
        }

        void OnEnable()
        {
            m_Point = m_HandPoint != null ? m_HandPoint : FollowerFor(FindFirstObjectByType<Player>());
            if (m_Point == null) m_Point = transform;
            m_IgnoreRoot = m_IgnoreCollidersUnder != null ? m_IgnoreCollidersUnder : transform.root;
            m_LastPosition = m_Point.position;
            m_LastArmPosition = ArmPosition();
            StartOver(m_LastPosition);

            // Get the sound ready now; the first hit shouldn't wait for it to load.
            PreparedClip();
        }

        void OnDestroy()
        {
            if (m_Prepared != null && m_Prepared != m_PreparedFrom) Destroy(m_Prepared);
            if (m_Voices == null) return;
            foreach (var voice in m_Voices)
                if (voice != null) Destroy(voice.gameObject);
        }

        // LateUpdate so Gorilla Locomotion has already planted the hand on whatever it touched this frame.
        void LateUpdate()
        {
            if (m_Point == null) return;

            Vector3 position = m_Point.position;
            Vector3 move = position - m_LastPosition;
            m_LastPosition = position;
            Vector3 armPosition = ArmPosition();
            Vector3 armMove = armPosition - m_LastArmPosition;
            m_LastArmPosition = armPosition;

            if (move.sqrMagnitude > k_TeleportDistance * k_TeleportDistance)
            {
                // Respawned, or Gorilla Locomotion unstuck the hand: start fresh without a hit.
                StartOver(position);
                return;
            }
            if (Time.deltaTime > 0f) RecordVelocity(move / Time.deltaTime, ArmToWorld(armMove) / Time.deltaTime);

            float radius = m_HandRadius + k_Skin;
            ForgetLiftedSurfaces(position, radius + m_LiftToHitAgain);
            if (!FindHit(position - move, position, radius, out var point, out float speed)) return;
            if (Time.time < m_NextHitTime) return;

            m_NextHitTime = Time.time + m_MinTimeBetweenHits;
            PlayHit(point, Mathf.InverseLerp(m_SoftHitSpeed, m_HardHitSpeed, speed));
        }

        Transform FollowerFor(Player player)
        {
            if (player == null) return null;
            if (player.leftHandTransform == transform) return player.leftHandFollower;
            if (player.rightHandTransform == transform) return player.rightHandFollower;
            return null;
        }

        bool IsOwn(Collider collider) => m_IgnoreRoot != null && collider.transform.IsChildOf(m_IgnoreRoot);

        // The controller relative to the body it's tracked from, so only the arm's own swing counts, not the body moving.
        Vector3 ArmPosition() => transform.parent != null ? transform.parent.InverseTransformPoint(transform.position) : transform.position;
        Vector3 ArmToWorld(Vector3 armMove) => transform.parent != null ? transform.parent.TransformVector(armMove) : armMove;

        void StartOver(Vector3 position)
        {
            for (int i = 0; i < k_VelocitySamples; i++) m_VelocityTimes[i] = float.NegativeInfinity;

            // Whatever the hand is already resting on doesn't count as a hit.
            m_Engaged.Clear();
            m_Touching.Clear();
            int count = Physics.OverlapSphereNonAlloc(position, m_HandRadius + k_Skin, s_Overlaps, m_HitLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsOwn(s_Overlaps[i])) continue;
                m_Engaged.Add(s_Overlaps[i]);
                m_Touching.Add(s_Overlaps[i]);
            }
        }

        void ForgetLiftedSurfaces(Vector3 position, float radius)
        {
            if (m_Engaged.Count == 0) return;
            int near = Physics.OverlapSphereNonAlloc(position, radius, s_Overlaps, m_HitLayers, QueryTriggerInteraction.Ignore);
            for (int i = m_Engaged.Count - 1; i >= 0; i--)
            {
                var collider = m_Engaged[i];
                if (collider == null || Array.IndexOf(s_Overlaps, collider, 0, near) < 0) m_Engaged.RemoveAt(i);
            }
        }

        /// <summary>
        /// Looks for a surface the hand landed on this frame. It's a hit if the hand had lifted away from that surface,
        /// or if it came down onto it fast. Also records what the hand is touching for next frame.
        /// </summary>
        bool FindHit(Vector3 from, Vector3 to, float radius, out Vector3 point, out float speed)
        {
            point = to;
            speed = 0f;
            bool found = false;
            m_TouchingNow.Clear();

            // Sweep from last frame's spot so a fast slap can't skip past a surface between frames.
            Vector3 move = to - from;
            float length = move.magnitude;
            if (length > 1e-5f)
            {
                int count = Physics.SphereCastNonAlloc(from, radius, move / length, s_Hits, length, m_HitLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    var hit = s_Hits[i];
                    // Distance 0 means it was already touching at the start of the frame; the overlap check covers it.
                    if (hit.distance <= 0f || IsOwn(hit.collider)) continue;
                    if (!m_TouchingNow.Contains(hit.collider)) m_TouchingNow.Add(hit.collider);
                    if (hit.distance >= nearest || !IsHit(hit.collider, hit.normal, out float hitSpeed)) continue;
                    nearest = hit.distance;
                    point = hit.point;
                    speed = hitSpeed;
                    found = true;
                }
            }

            // Anything against the hand now: catches slow touches and things that move into your hand.
            int overlaps = Physics.OverlapSphereNonAlloc(to, radius, s_Overlaps, m_HitLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps; i++)
            {
                var collider = s_Overlaps[i];
                if (IsOwn(collider) || m_TouchingNow.Contains(collider)) continue;
                m_TouchingNow.Add(collider);
                if (found) continue;
                SurfaceNear(collider, to, from, out var surfacePoint, out var normal);
                if (!IsHit(collider, normal, out float hitSpeed)) continue;
                point = surfacePoint;
                speed = hitSpeed;
                found = true;
            }

            foreach (var collider in m_TouchingNow)
                if (!m_Engaged.Contains(collider)) m_Engaged.Add(collider);
            (m_Touching, m_TouchingNow) = (m_TouchingNow, m_Touching);
            return found;
        }

        bool IsHit(Collider collider, Vector3 normal, out float speed)
        {
            speed = ImpactSpeed(m_Velocities, normal);
            if (!m_Engaged.Contains(collider)) return true;
            return !m_Touching.Contains(collider) && ImpactSpeed(m_ArmVelocities, normal) >= m_QuickTapSpeed;
        }

        static void SurfaceNear(Collider collider, Vector3 hand, Vector3 previous, out Vector3 point, out Vector3 normal)
        {
            // ClosestPoint only works on primitives and convex meshes.
            bool supported = !(collider is TerrainCollider) && !(collider is MeshCollider mesh && !mesh.convex);
            point = supported ? collider.ClosestPoint(hand) : hand;
            normal = hand - point;
            if (normal.sqrMagnitude < 1e-8f) normal = previous - hand; // Inside it: assume we came in the way we moved.
            normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
        }

        void RecordVelocity(Vector3 velocity, Vector3 armVelocity)
        {
            m_VelocityIndex = (m_VelocityIndex + 1) % k_VelocitySamples;
            m_Velocities[m_VelocityIndex] = velocity;
            m_ArmVelocities[m_VelocityIndex] = armVelocity;
            m_VelocityTimes[m_VelocityIndex] = Time.time;
        }

        /// <summary>
        /// How fast the hand was moving into the surface just before it hit. Looks back a few frames because the
        /// hand has usually already been stopped at the surface by the frame the hit is found.
        /// </summary>
        float ImpactSpeed(Vector3[] velocities, Vector3 normal)
        {
            float speed = 0f;
            for (int i = 0; i < k_VelocitySamples; i++)
                if (Time.time - m_VelocityTimes[i] <= k_SpeedWindow)
                    speed = Mathf.Max(speed, Vector3.Dot(velocities[i], -normal));
            return speed;
        }

        void PlayHit(Vector3 point, float strength)
        {
            var clip = PreparedClip();
            if (clip != null)
            {
                var voice = NextVoice();
                voice.transform.position = point;
                voice.clip = clip;
                voice.volume = Mathf.Lerp(m_MinVolume, m_MaxVolume, strength);
                voice.pitch = 1f + UnityEngine.Random.Range(-m_PitchVariation, m_PitchVariation);
                voice.spatialBlend = m_SpatialBlend;
                voice.outputAudioMixerGroup = m_Output;
                voice.Play();
            }

            if (m_Vibrate) VRHaptics.Pulse(m_Hand, Mathf.Lerp(m_MinVibration, m_MaxVibration, strength), m_VibrationLength);
            Hit?.Invoke(point, strength);
        }

        /// <summary>The hit sound with its leading silence cut and its level brought up. Rebuilt when the settings change.</summary>
        AudioClip PreparedClip()
        {
            if (m_HitSound == null) return null;
            if (m_PreparedFrom == m_HitSound && m_PreparedTrim == m_TrimSilence && Mathf.Approximately(m_PreparedBoost, m_Boost) && m_Prepared != null)
                return m_Prepared;

            if (m_Prepared != null && m_Prepared != m_PreparedFrom) Destroy(m_Prepared);
            m_PreparedFrom = m_HitSound;
            m_PreparedTrim = m_TrimSilence;
            m_PreparedBoost = m_Boost;
            m_Prepared = Prepare(m_HitSound, m_TrimSilence, m_Boost);
            if (m_Prepared == null)
            {
                m_Prepared = m_HitSound;
                if (m_HitSound.loadState == AudioDataLoadState.Unloaded) m_HitSound.LoadAudioData();
            }
            return m_Prepared;
        }

        AudioClip Prepare(AudioClip clip, bool trim, float boost)
        {
            // Compressed or streamed clips don't hand out their samples.
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                Debug.LogWarning($"Hand Hit Feedback can't trim or boost '{clip.name}': set its Load Type to Decompress On Load.", this);
                return null;
            }
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) return null;

            int channels = clip.channels;
            var samples = new float[clip.samples * channels];
            if (!clip.GetData(samples, 0)) return null;

            float peak = 0f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak <= 0f) return null;

            int start = 0;
            if (trim)
            {
                int onset = 0;
                while (onset < samples.Length && Mathf.Abs(samples[onset]) < peak * k_OnsetThreshold) onset++;
                start = Mathf.Max(0, onset / channels - Mathf.RoundToInt(k_OnsetLead * clip.frequency));
            }

            int frames = clip.samples - start;
            var output = new float[frames * channels];
            float shape = boost > 1.001f ? 1f / (float)Math.Tanh(boost) : 1f;
            for (int i = 0; i < output.Length; i++)
            {
                float x = samples[start * channels + i] / peak;
                output[i] = boost > 1.001f ? (float)Math.Tanh(boost * x) * shape : x;
            }

            var prepared = AudioClip.Create(clip.name + " (hit)", frames, channels, clip.frequency, false);
            prepared.SetData(output, 0);
            return prepared;
        }

        // A few voices take turns so quick hits overlap instead of cutting each other off.
        AudioSource NextVoice()
        {
            if (m_Voices == null)
            {
                m_Voices = new AudioSource[k_Voices];
                for (int i = 0; i < k_Voices; i++)
                {
                    var go = new GameObject("Hit Sound");
                    go.transform.SetParent(transform, false);
                    var source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.dopplerLevel = 0f; // Fast hands would otherwise bend the pitch.
                    m_Voices[i] = source;
                }
            }
            var voice = m_Voices[m_NextVoice];
            m_NextVoice = (m_NextVoice + 1) % k_Voices;
            return voice;
        }

        void OnDrawGizmosSelected()
        {
            var point = m_HandPoint != null ? m_HandPoint : transform;
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 1f);
            Gizmos.DrawWireSphere(point.position, m_HandRadius + k_Skin);
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(point.position, m_HandRadius + k_Skin + m_LiftToHitAgain);
        }
    }
}
