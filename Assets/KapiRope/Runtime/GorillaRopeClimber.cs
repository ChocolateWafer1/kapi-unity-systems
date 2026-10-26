using System;
using GorillaLocomotion;
using KapiVR;
using UnityEngine;
using UnityEngine.InputSystem;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRNode = UnityEngine.XR.XRNode;

namespace KapiRope
{
    /// <summary>
    /// Lets a Gorilla Locomotion player grab ropes with grip, hang, swing, climb hand over hand, and slide down by
    /// loosening their grip. Your hands, head and body also shove ropes they touch. Put it next to
    /// GorillaLocomotion.Player; your free hand keeps normal Gorilla Tag movement, so you can push off walls mid-swing.
    /// </summary>
    [DefaultExecutionOrder(50)] // After GorillaLocomotion.Player (0) moves the body, before ropes simulate (100).
    [DisallowMultipleComponent]
    [AddComponentMenu("Kapi Rope/Gorilla Rope Climber")]
    public sealed class GorillaRopeClimber : MonoBehaviour
    {
        const float k_Skin = 0.01f;
        const float k_TeleportDistance = 3f;

        static int s_ActiveCount;

        /// <summary>How many climbers are running. Ropes add one to the player if this is zero.</summary>
        public static int ActiveCount => s_ActiveCount;

        sealed class Hand
        {
            public VRHand side;
            public InputAction gripAction;
            public bool ownsAction;
            public float grip;
            public float previousGrip;
            public Vector3 point;
            public Vector3 offset;
            public Vector3 previousOffset;
            public Vector3 held;
            public int order;
            public RopeGrip ropeGrip;
            public float slideSpeed;
            public float hapticTimer;
            public float bumpCooldown;
            public RopePusher pusher;
            public AudioSource slideAudio;
        }

        [Tooltip("The Gorilla Locomotion player to move. Found automatically on this object.")]
        [SerializeField] Player m_Player;

        [Header("Grip Input")]
        [Tooltip("Optional: your own grip action for the left hand. Leave empty to use the controller's grip.")]
        [SerializeField] InputActionProperty m_LeftGripAction;

        [Tooltip("Optional: your own grip action for the right hand. Leave empty to use the controller's grip.")]
        [SerializeField] InputActionProperty m_RightGripAction;

        [Tooltip("Squeeze grip past this to grab a rope.")]
        [SerializeField, Range(0.05f, 1f)] float m_GrabThreshold = 0.55f;

        [Tooltip("At or above this squeeze you hold tight. Between this and the release threshold you slide down (on ropes that allow sliding).")]
        [SerializeField, Range(0.05f, 1f)] float m_HoldThreshold = 0.85f;

        [Tooltip("Below this squeeze you let go.")]
        [SerializeField, Range(0f, 1f)] float m_ReleaseThreshold = 0.25f;

        [Tooltip("Holding grip before your hand reaches a rope grabs it on contact, so you can catch ropes mid-jump.")]
        [SerializeField] bool m_CatchWhileHolding = true;

        [Tooltip("If your real hand ends up this far (m) from where it holds the rope, for example because the floor stops your body from following your arm, you let go.")]
        [SerializeField, Min(0.1f)] float m_LetGoDistance = 0.5f;

        [Header("Bumping")]
        [Tooltip("Size of each hand when shoving ropes. Match your hand spheres (a 0.1 scale sphere is 0.05).")]
        [SerializeField, Min(0.005f)] float m_HandRadius = 0.05f;

        [Tooltip("Your head and body push ropes out of the way too.")]
        [SerializeField] bool m_BodyPushesRopes = true;

        [Header("Feel")]
        [Tooltip("Controller vibration when grabbing, sliding and smacking ropes.")]
        [SerializeField] bool m_Haptics = true;

        [SerializeField, Range(0f, 1f)] float m_GrabHaptics = 0.45f;
        [SerializeField, Range(0f, 1f)] float m_SlideHaptics = 0.25f;
        [SerializeField, Range(0f, 1f)] float m_BumpHaptics = 0.3f;

        [Tooltip("Optional sound played at the hand when grabbing.")]
        [SerializeField] AudioClip m_GrabSound;

        [Tooltip("Optional looping sound while sliding; its volume follows how fast you slide.")]
        [SerializeField] AudioClip m_SlideSound;

        [SerializeField, Range(0f, 1f)] float m_Volume = 0.6f;

        readonly Hand m_Left = new Hand { side = VRHand.Left };
        readonly Hand m_Right = new Hand { side = VRHand.Right };
        readonly Vector3[] m_PullHistory = new Vector3[6];
        RopePusher m_HeadPusher;
        RopePusher m_BodyPusher;
        Rigidbody m_Body;
        bool m_Hanging;
        Vector3 m_Velocity;
        Vector3 m_LastPosition;
        Vector3 m_FrameMove;
        int m_PullIndex;
        int m_GrabCounter;
        Hand m_Lead;
        bool m_Counted;
        bool m_SavedGravity;
        bool m_SavedDisableMovement;
        RigidbodyInterpolation m_SavedInterpolation;

        /// <summary>Called when a hand grabs a rope.</summary>
        public event Action<RopeGrip, VRHand> GrabbedRope;

        /// <summary>Called when a hand lets go of a rope.</summary>
        public event Action<RopeGrip, VRHand> ReleasedRope;

        /// <summary>True while at least one hand is holding a rope.</summary>
        public bool IsHanging => m_Hanging;

        /// <summary>Your velocity while hanging (what you'd leave the rope with, before the release boost).</summary>
        public Vector3 SwingVelocity => m_Velocity;

        public RopeGrip GetGrip(VRHand hand) => (hand == VRHand.Left ? m_Left : m_Right).ropeGrip;

        /// <summary>How hard the grip is squeezed right now (0-1), as the climber reads it.</summary>
        public float GetGripValue(VRHand hand) => (hand == VRHand.Left ? m_Left : m_Right).grip;

        /// <summary>The hand position used for grabbing (the controller, limited to arm's reach).</summary>
        public Vector3 GetHandPoint(VRHand hand) => (hand == VRHand.Left ? m_Left : m_Right).point;

        public float GrabThreshold => m_GrabThreshold;

        void Reset()
        {
            m_Player = GetComponent<Player>();
        }

        void OnEnable()
        {
            if (m_Player == null) m_Player = GetComponent<Player>();
            if (m_Player == null) m_Player = FindFirstObjectByType<Player>();
            if (m_Player == null)
            {
                Debug.LogWarning("Gorilla Rope Climber needs a GorillaLocomotion.Player in the scene.", this);
                enabled = false;
                return;
            }

            m_Body = m_Player.GetComponent<Rigidbody>();
            SetUpHand(m_Left, m_LeftGripAction, "<XRController>{LeftHand}");
            SetUpHand(m_Right, m_RightGripAction, "<XRController>{RightHand}");

            m_HeadPusher ??= new RopePusher(this);
            m_BodyPusher ??= new RopePusher(this);
            if (m_BodyPushesRopes)
            {
                m_HeadPusher.Register();
                m_BodyPusher.Register();
            }
            UpdatePushers(true);
            s_ActiveCount++;
            m_Counted = true;
        }

        void OnDisable()
        {
            if (m_Counted)
            {
                s_ActiveCount--;
                m_Counted = false;
            }
            LetGo(m_Left, false);
            LetGo(m_Right, false);
            TearDownHand(m_Left);
            TearDownHand(m_Right);
            m_HeadPusher?.Unregister();
            m_BodyPusher?.Unregister();
        }

        void SetUpHand(Hand hand, InputActionProperty property, string device)
        {
            var action = property.action;
            if (action != null && action.bindings.Count > 0)
            {
                hand.gripAction = action;
                hand.ownsAction = false;
            }
            else
            {
                action = new InputAction($"{hand.side} Rope Grip", InputActionType.Value, device + "/{Grip}", expectedControlType: "Axis");
                action.AddBinding(device + "/grip");
                hand.gripAction = action;
                hand.ownsAction = true;
            }
            hand.gripAction.Enable();

            hand.pusher ??= new RopePusher(this);
            hand.pusher.Register();
        }

        void TearDownHand(Hand hand)
        {
            if (hand.ownsAction && hand.gripAction != null)
            {
                hand.gripAction.Disable();
                hand.gripAction.Dispose();
                hand.gripAction = null;
            }
            hand.pusher?.Unregister();
            if (hand.slideAudio != null) hand.slideAudio.Stop();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_Player == null || dt <= 0f) return;

            Vector3 body = m_Player.transform.position;
            UpdateHand(m_Left, m_Player.leftHandTransform, m_Player.leftHandOffset, body, dt);
            UpdateHand(m_Right, m_Player.rightHandTransform, m_Player.rightHandOffset, body, dt);

            m_FrameMove = Vector3.zero;
            if (m_Hanging) Hang(dt);

            UpdatePushers(false);
            BumpFeedback(m_Left);
            BumpFeedback(m_Right);
        }

        void LateUpdate()
        {
            if (!m_Hanging) return;
            PlaceFollower(m_Left, m_Player.leftHandFollower);
            PlaceFollower(m_Right, m_Player.rightHandFollower);
        }

        void PlaceFollower(Hand hand, Transform follower)
        {
            if (follower == null) return;
            // The held hand sits on the rope; the free hand rides along with the swing instead of lagging a frame.
            if (hand.ropeGrip != null) follower.position = hand.ropeGrip.Rope.GetPoint(hand.ropeGrip.Distance);
            else follower.position += m_FrameMove;
        }

        void UpdateHand(Hand hand, Transform controller, Vector3 offset, Vector3 body, float dt)
        {
            ReadGrip(hand);
            hand.point = HandPoint(controller, offset);
            hand.offset = hand.point - body;
            hand.bumpCooldown -= dt;

            if (hand.ropeGrip == null)
            {
                bool squeezed = hand.grip >= m_GrabThreshold && (hand.previousGrip < m_GrabThreshold || m_CatchWhileHolding);
                if (squeezed) TryGrab(hand);
                return;
            }

            var grip = hand.ropeGrip;
            if (!grip.IsHeld || grip.Rope == null || !grip.Rope.isActiveAndEnabled || hand.grip < m_ReleaseThreshold)
            {
                LetGo(hand, true);
                return;
            }
            // Only the hand you're hanging from slides down; the other one already rides along the rope.
            if (hand == m_Lead) Slide(hand, dt);
        }

        void ReadGrip(Hand hand)
        {
            hand.previousGrip = hand.grip;
            float value = 0f;
            if (hand.gripAction != null && hand.gripAction.enabled) value = hand.gripAction.ReadValue<float>();

            // Some XR plug-ins only report grip through the XR input subsystem, so check there too.
            if (hand.ownsAction)
            {
                var device = XRInputDevices.GetDeviceAtXRNode(hand.side == VRHand.Left ? XRNode.LeftHand : XRNode.RightHand);
                if (device.isValid && device.TryGetFeatureValue(XRCommonUsages.grip, out float xrGrip)) value = Mathf.Max(value, xrGrip);
            }
            hand.grip = value;
        }

        /// <summary>The controller position, limited to arm's reach the same way Gorilla Locomotion does it.</summary>
        Vector3 HandPoint(Transform controller, Vector3 offset)
        {
            Vector3 point = controller.position + controller.rotation * offset;
            Vector3 head = m_Player.headCollider.transform.position;
            Vector3 reach = point - head;
            float max = m_Player.maxArmLength;
            return reach.sqrMagnitude < max * max ? point : head + reach.normalized * max;
        }

        void TryGrab(Hand hand)
        {
            Rope best = null;
            float bestDistance = 0f;
            float bestSq = float.MaxValue;
            var ropes = Rope.Active;
            for (int i = 0; i < ropes.Count; i++)
            {
                var rope = ropes[i];
                if (!rope.Grabbable || !rope.FindClosestPoint(hand.point, out float along, out _, out float sq)) continue;
                float reach = rope.GrabRadius + rope.Radius;
                if (sq > reach * reach || sq >= bestSq) continue;
                best = rope;
                bestDistance = along;
                bestSq = sq;
            }
            if (best == null) return;

            bool startHanging = !m_Hanging;
            hand.ropeGrip = best.Grab(bestDistance, hand.point, this);
            hand.held = hand.point;
            hand.order = ++m_GrabCounter;
            hand.previousOffset = hand.offset;
            hand.slideSpeed = 0f;
            hand.pusher.IgnoredRope = best;
            if (startHanging) BeginHang();

            Pulse(hand.side, m_GrabHaptics, 0.06f);
            if (m_GrabSound != null) AudioSource.PlayClipAtPoint(m_GrabSound, hand.point, m_Volume);
            GrabbedRope?.Invoke(hand.ropeGrip, hand.side);
        }

        void LetGo(Hand hand, bool launch)
        {
            var grip = hand.ropeGrip;
            if (grip == null) return;

            hand.ropeGrip = null;
            hand.slideSpeed = 0f;
            if (m_Lead == hand) m_Lead = null;
            if (hand.pusher != null) hand.pusher.IgnoredRope = null;
            if (hand.slideAudio != null) hand.slideAudio.Stop();
            if (grip.IsHeld && grip.Rope != null) grip.Rope.Release(grip);

            if (m_Hanging && m_Left.ropeGrip == null && m_Right.ropeGrip == null) EndHang(grip.Rope, launch);
            ReleasedRope?.Invoke(grip, hand.side);
        }

        void BeginHang()
        {
            m_Hanging = true;
            m_LastPosition = m_Player.transform.position;
            m_Velocity = m_Body != null ? m_Body.linearVelocity : Vector3.zero;
            Array.Clear(m_PullHistory, 0, m_PullHistory.Length);

            // Gorilla Locomotion would read the climbing moves as hand speed and fire jumps when the free hand
            // brushes something. Its hand sticking still works, so you can still push off walls mid-swing.
            m_SavedDisableMovement = m_Player.disableMovement;
            m_Player.disableMovement = true;

            // While hanging, this script moves the player; the rigidbody just waits.
            if (m_Body == null) return;
            m_SavedGravity = m_Body.useGravity;
            m_SavedInterpolation = m_Body.interpolation;
            m_Body.useGravity = false;
            m_Body.interpolation = RigidbodyInterpolation.None;
            m_Body.linearVelocity = Vector3.zero;
        }

        void EndHang(Rope rope, bool launch)
        {
            m_Hanging = false;
            m_Lead = null;
            if (m_Player != null) m_Player.disableMovement = m_SavedDisableMovement;
            Vector3 velocity = m_Velocity;
            if (launch && rope != null)
            {
                Vector3 pull = Vector3.zero;
                for (int i = 0; i < m_PullHistory.Length; i++)
                    pull += m_PullHistory[i];
                pull /= m_PullHistory.Length;
                velocity = Vector3.ClampMagnitude((velocity + pull * rope.ClimbFling) * rope.ReleaseBoost, rope.MaxReleaseSpeed);
            }

            if (m_Body == null) return;
            m_Body.useGravity = m_SavedGravity;
            m_Body.interpolation = m_SavedInterpolation;
            m_Body.linearVelocity = velocity;
        }

        /// <summary>
        /// Moves the body while hanging. You hang from one hand (the last one that grabbed): that hand is locked to
        /// a point on the rope, and only your momentum and weight move that point, limited by the rope's length
        /// (that's swinging). Moving your arm never moves the rope; it moves your body the other way instead
        /// (that's climbing), like a hand stuck to a wall in Gorilla Tag. A second holding hand slides along the rope.
        /// </summary>
        void Hang(float dt)
        {
            var lead = LeadHand();
            if (lead != m_Lead)
            {
                // Switching hands (or a new grab): hang from where the new hand already is, so nothing jumps.
                if (m_Lead != null && m_Lead.ropeGrip != null) m_Lead.ropeGrip.Pinning = false;
                lead.held = lead.point;
                lead.previousOffset = lead.offset;
                lead.ropeGrip.Pinning = true;
                m_Lead = lead;
            }

            Transform root = m_Player.transform;
            Vector3 start = root.position;
            // Anything Gorilla Locomotion did this frame, such as your free hand pushing off a wall, carries the
            // rope with you and becomes momentum.
            Vector3 external = start - m_LastPosition;
            if (external.sqrMagnitude > k_TeleportDistance * k_TeleportDistance)
            {
                // Teleported (respawn and so on): let go instead of dragging the rope across the map.
                LetGo(m_Left, false);
                LetGo(m_Right, false);
                return;
            }
            Rope rope = lead.ropeGrip.Rope;

            m_Velocity *= Mathf.Exp(-rope.SwingDrag * dt);
            m_Velocity += Physics.gravity * dt;

            // Swing: the held point and your body move together, but never farther from the anchor than the rope.
            Vector3 swing = TetherMove(lead, external + m_Velocity * dt);
            Vector3 bodySwing = swing - external;
            Vector3 velocity;
            if (SweepBody(start, start + bodySwing, out var hit))
            {
                bodySwing = Clip(bodySwing, hit);
                swing = external + bodySwing;
                velocity = swing / dt;
                float into = Vector3.Dot(velocity, hit.normal);
                if (into < 0f) velocity -= hit.normal * into;
            }
            else
            {
                velocity = swing / dt;
            }

            // Climb: the arm moves the body, and only the body.
            Vector3 swung = start + bodySwing;
            Vector3 climb = lead.previousOffset - lead.offset;
            if (SweepBody(swung, swung + climb, out hit)) climb = Clip(climb, hit);
            Vector3 target = swung + climb;

            root.position = target;
            if (m_Body != null)
            {
                m_Body.position = target;
                m_Body.linearVelocity = Vector3.zero;
            }

            lead.held += swing;
            lead.previousOffset = lead.offset;
            lead.ropeGrip.Target = lead.held;
            m_FrameMove = target - start;
            m_Velocity = velocity;
            m_LastPosition = target;
            m_PullHistory[m_PullIndex] = climb / dt;
            m_PullIndex = (m_PullIndex + 1) % m_PullHistory.Length;

            var other = lead == m_Left ? m_Right : m_Left;
            if (other.ropeGrip != null) RideAlong(other, target);

            // If something stops your body from following your arm (like the floor), your hand pulls off the rope.
            if ((target + lead.offset - lead.held).sqrMagnitude > m_LetGoDistance * m_LetGoDistance) LetGo(lead, false);
        }

        /// <summary>The holding hand that grabbed most recently.</summary>
        Hand LeadHand()
        {
            if (m_Left.ropeGrip == null) return m_Right;
            if (m_Right.ropeGrip == null) return m_Left;
            return m_Left.order > m_Right.order ? m_Left : m_Right;
        }

        /// <summary>A second holding hand slides along the rope with you instead of dragging it around.</summary>
        void RideAlong(Hand hand, Vector3 body)
        {
            var grip = hand.ropeGrip;
            grip.Pinning = false;
            hand.previousOffset = hand.offset;
            Vector3 point = body + hand.offset;
            if (!grip.Rope.FindClosestPoint(point, out float along, out Vector3 closest, out float sq) || sq > m_LetGoDistance * m_LetGoDistance)
            {
                LetGo(hand, false);
                return;
            }
            grip.Distance = along;
            grip.Target = closest;
        }

        /// <summary>Limits a move of the held point so the rope between it and the anchor (or end) can't stretch.</summary>
        static Vector3 TetherMove(Hand hand, Vector3 move)
        {
            var grip = hand.ropeGrip;
            var rope = grip.Rope;
            Vector3 point = hand.held + move;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                bool moved = Tether(ref point, rope.AnchorPosition, grip.Distance);
                if (rope.EndAttachment != null) moved |= Tether(ref point, rope.EndAttachment.position, rope.Length - grip.Distance);
                if (!moved) break;
            }
            return point - hand.held;
        }

        static bool Tether(ref Vector3 point, Vector3 anchor, float length)
        {
            Vector3 d = point - anchor;
            float sq = d.sqrMagnitude;
            if (sq <= length * length) return false;
            point = anchor + d * (length / Mathf.Sqrt(sq));
            return true;
        }

        static Vector3 Clip(Vector3 move, RaycastHit hit)
        {
            float length = move.magnitude;
            return length < 1e-6f ? Vector3.zero : move / length * Mathf.Max(0f, hit.distance - k_Skin);
        }

        void Slide(Hand hand, float dt)
        {
            var grip = hand.ropeGrip;
            var rope = grip.Rope;
            float looseness = rope.AllowSliding ? Mathf.InverseLerp(m_HoldThreshold, m_ReleaseThreshold, hand.grip) : 0f;

            // You only slide while the rope is carrying you, and toward whichever way is downhill along it.
            bool loaded = m_Hanging && IsTaut(hand.held, grip);
            float downhill = Vector3.Dot(rope.GetDirection(grip.Distance), Physics.gravity.normalized);
            float target = loaded ? rope.MaxSlideSpeed * looseness * downhill : 0f;
            float rate = Mathf.Abs(target) < Mathf.Abs(hand.slideSpeed) ? 30f : Physics.gravity.magnitude;
            hand.slideSpeed = Mathf.MoveTowards(hand.slideSpeed, target, rate * dt);

            if (Mathf.Abs(hand.slideSpeed) > 1e-4f)
            {
                float distance = grip.Distance + hand.slideSpeed * dt;
                if (distance >= rope.Length - 0.02f && rope.EndAttachment == null)
                {
                    LetGo(hand, true); // Slid off the bottom.
                    return;
                }
                grip.Distance = distance;
                if (grip.Distance <= 0f || grip.Distance >= rope.Length) hand.slideSpeed = 0f;
            }

            float speed01 = rope.MaxSlideSpeed > 0f ? Mathf.Clamp01(Mathf.Abs(hand.slideSpeed) / rope.MaxSlideSpeed) : 0f;
            hand.hapticTimer -= dt;
            if (speed01 > 0.02f && hand.hapticTimer <= 0f)
            {
                hand.hapticTimer = 0.04f;
                Pulse(hand.side, m_SlideHaptics * (0.3f + 0.7f * speed01), 0.05f);
            }
            UpdateSlideAudio(hand, speed01);
        }

        static bool IsTaut(Vector3 handPoint, RopeGrip grip)
        {
            const float slack = 0.05f;
            float fromAnchor = (handPoint - grip.Rope.AnchorPosition).magnitude;
            if (fromAnchor >= grip.Distance - slack) return true;
            var end = grip.Rope.EndAttachment;
            return end != null && (handPoint - end.position).magnitude >= grip.Rope.Length - grip.Distance - slack;
        }

        void UpdateSlideAudio(Hand hand, float speed01)
        {
            if (m_SlideSound == null) return;
            if (hand.slideAudio == null)
            {
                var follower = hand.side == VRHand.Left ? m_Player.leftHandFollower : m_Player.rightHandFollower;
                var go = new GameObject($"{hand.side} Rope Slide Audio");
                go.transform.SetParent(follower != null ? follower : transform, false);
                hand.slideAudio = go.AddComponent<AudioSource>();
                hand.slideAudio.clip = m_SlideSound;
                hand.slideAudio.loop = true;
                hand.slideAudio.playOnAwake = false;
                hand.slideAudio.spatialBlend = 1f;
            }

            hand.slideAudio.volume = m_Volume * speed01;
            hand.slideAudio.pitch = 0.8f + 0.5f * speed01;
            if (speed01 > 0.02f && !hand.slideAudio.isPlaying) hand.slideAudio.Play();
            else if (speed01 <= 0.02f && hand.slideAudio.isPlaying) hand.slideAudio.Stop();
        }

        /// <summary>Sweeps the head and body colliders against the locomotion layers so swinging never clips into walls.</summary>
        bool SweepBody(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            hit = default;
            Vector3 move = to - from;
            float length = move.magnitude;
            if (length < 1e-5f) return false;
            Vector3 direction = move / length;
            int mask = m_Player.locomotionEnabledLayers.value;
            float best = float.MaxValue;

            var head = m_Player.headCollider;
            if (head != null && head.enabled)
            {
                Vector3 center = head.transform.TransformPoint(head.center);
                float radius = head.radius * MaxScale(head.transform) * 0.95f;
                if (Physics.SphereCast(center, radius, direction, out var headHit, length + k_Skin, mask, QueryTriggerInteraction.Ignore))
                {
                    best = headHit.distance;
                    hit = headHit;
                }
            }

            var capsule = m_Player.bodyCollider;
            if (capsule != null && capsule.enabled)
            {
                CapsuleEnds(capsule, out Vector3 a, out Vector3 b, out float radius);
                if (Physics.CapsuleCast(a, b, radius * 0.95f, direction, out var bodyHit, length + k_Skin, mask, QueryTriggerInteraction.Ignore) && bodyHit.distance < best)
                {
                    best = bodyHit.distance;
                    hit = bodyHit;
                }
            }
            return best < float.MaxValue;
        }

        void UpdatePushers(bool teleport)
        {
            if (m_Player == null) return;
            MoveHandPusher(m_Left, m_Player.leftHandFollower, teleport);
            MoveHandPusher(m_Right, m_Player.rightHandFollower, teleport);

            if (!m_BodyPushesRopes)
            {
                m_HeadPusher.Unregister();
                m_BodyPusher.Unregister();
                return;
            }
            m_HeadPusher.Register();
            m_BodyPusher.Register();

            var head = m_Player.headCollider;
            if (head != null)
            {
                Vector3 center = head.transform.TransformPoint(head.center);
                MovePusher(m_HeadPusher, center, center, head.radius * MaxScale(head.transform), teleport);
            }
            var capsule = m_Player.bodyCollider;
            if (capsule != null)
            {
                CapsuleEnds(capsule, out Vector3 a, out Vector3 b, out float radius);
                MovePusher(m_BodyPusher, a, b, radius, teleport);
            }
        }

        void MoveHandPusher(Hand hand, Transform follower, bool teleport)
        {
            if (hand.pusher == null) return;
            Vector3 point = follower != null ? follower.position + m_FrameMove : hand.point;
            MovePusher(hand.pusher, point, point, m_HandRadius, teleport);
        }

        static void MovePusher(RopePusher pusher, Vector3 a, Vector3 b, float radius, bool teleport)
        {
            teleport |= (a - pusher.PointA).sqrMagnitude > k_TeleportDistance * k_TeleportDistance;
            pusher.Move(a, b, radius, teleport);
        }

        void BumpFeedback(Hand hand)
        {
            if (hand.pusher == null) return;
            float speed = hand.pusher.ConsumeImpactSpeed();
            if (hand.ropeGrip != null || speed < 0.4f || hand.bumpCooldown > 0f) return;
            hand.bumpCooldown = 0.12f;
            Pulse(hand.side, m_BumpHaptics * Mathf.Clamp01(speed / 3f), 0.04f);
        }

        void Pulse(VRHand side, float amplitude, float duration)
        {
            if (m_Haptics) VRHaptics.Pulse(side, amplitude, duration);
        }

        static void CapsuleEnds(CapsuleCollider capsule, out Vector3 a, out Vector3 b, out float radius)
        {
            var t = capsule.transform;
            Vector3 scale = t.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            int axis = capsule.direction;
            Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
            float half = Mathf.Max(0f, capsule.height * 0.5f * scale[axis] - radius);
            Vector3 center = t.TransformPoint(capsule.center);
            Vector3 worldAxis = t.TransformDirection(localAxis);
            a = center + worldAxis * half;
            b = center - worldAxis * half;
        }

        static float MaxScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }
    }
}
