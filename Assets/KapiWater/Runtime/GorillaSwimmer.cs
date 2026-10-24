using System;
using GorillaLocomotion;
using KapiRope;
using KapiVR;
using UnityEngine;
using UnityEngine.Audio;

namespace KapiWater
{
    /// <summary>
    /// Gorilla Tag swimming. Pull your arms back through the water and you glide the way you're looking; bringing
    /// them forward again is free, so you stroke over and over to keep going. Very low gravity underwater means you
    /// sink slowly when you stop. Your hands rumble against the water while you pull, slap the surface, fizz with
    /// bubbles, and throw pixel splashes. Put it next to GorillaLocomotion.Player, or let any Water Volume add it when
    /// you press Play. Hands touching walls or the floor underwater still climb and push off like normal.
    /// </summary>
    [DefaultExecutionOrder(40)] // After GorillaLocomotion.Player (0) moves the body, before the rope climber (50).
    [DisallowMultipleComponent]
    [AddComponentMenu("Kapi Water/Gorilla Swimmer")]
    public sealed class GorillaSwimmer : MonoBehaviour
    {
        const float k_SurfaceHysteresis = 0.035f;
        const float k_TeleportDistance = 1f;
        const int k_Voices = 8;

        sealed class Hand
        {
            public VRHand side;
            public SwimHaptics haptics;
            public bool tracked;
            public Vector3 lastLocal;
            public Vector3 armVelocity;
            public Vector3 point;
            public bool underwater;
            public float pull;
            public float bubbleBudget;
            public float splashCooldown;
            public AudioSource rush;
            public float rushLevel;
        }

        [Tooltip("The Gorilla Locomotion player to move. Found automatically.")]
        [SerializeField] Player m_Player;

        [Header("Water Physics")]
        [Tooltip("Gravity underwater, as a fraction of normal gravity. Tiny = you sink slowly when you stop swimming. 0 = you hang in place.")]
        [SerializeField, Range(0f, 1f)] float m_WaterGravity = 0.04f;

        [Tooltip("How quickly the water slows you down, per second. Higher = thicker water and shorter glides.")]
        [SerializeField, Range(0f, 10f)] float m_WaterDrag = 0.9f;

        [Tooltip("Fastest your strokes can take you, in meters per second. Diving in or pushing off a wall can still go faster; the water just brakes you harder until you're back under this.")]
        [SerializeField, Range(0.5f, 30f)] float m_MaxSwimSpeed = 6f;

        [Tooltip("How far below your head your body reaches, in meters. The water's low gravity and drag fade in as you sink this far, so with your head just out of the water you still float half in it.")]
        [SerializeField, Range(0.1f, 2f)] float m_BodyLength = 0.7f;

        [Tooltip("Multiplies your upward speed when you burst out of the water head first, for dolphin leaps. 1 = no boost.")]
        [SerializeField, Range(1f, 3f)] float m_LeapBoost = 1.15f;

        [Header("Strokes")]
        [Tooltip("How much speed each pull gives you.")]
        [SerializeField, Range(0f, 30f)] float m_StrokePower = 3f;

        [Tooltip("1 = the speed you gain only depends on how far you pull. Higher = fast, hard pulls count a lot more than slow ones.")]
        [SerializeField, Range(0.5f, 3f)] float m_PullCurve = 1.3f;

        [Tooltip("Hand movement slower than this (meters per second) never pushes, so drifting or shaky hands don't move you.")]
        [SerializeField, Range(0f, 2f)] float m_MinPullSpeed = 0.35f;

        [Tooltip("How much a pull pushes you, by which way it sends you compared with where you're looking.\nLeft end (-1): pulls that would push you backward, which is what swinging your arms forward does. Keep it at 0 so the forward swing is free.\nMiddle (0): pushes up, down or sideways.\nRight end (1): pushes straight where you're looking.")]
        [SerializeField] AnimationCurve m_PowerByDirection = DefaultPowerByDirection();

        [Tooltip("How much your own speed works against your pull. 1 = like real water: once you move as fast as your hands, pulling at that speed stops helping. 0 = every pull adds speed no matter how fast you already go.")]
        [SerializeField, Range(0f, 1f)] float m_YourSpeedCounts = 0.6f;

        [Tooltip("How deep a hand has to be, in meters, to push at full power. Hands just under the surface push less, hands out of the water don't push.")]
        [SerializeField, Range(0.01f, 0.5f)] float m_HandDepthForFullPower = 0.12f;

        [Tooltip("Smooths hand speed over this many seconds so tracking jitter doesn't add up.")]
        [SerializeField, Range(0f, 0.1f)] float m_HandSmoothing = 0.025f;

        [Tooltip("Hand speed through the water (meters per second) that counts as a full-strength pull, for the rumble, sound and bubbles.")]
        [SerializeField, Range(0.5f, 8f)] float m_FullPullSpeed = 2.5f;

        [Header("Haptics")]
        [SerializeField] bool m_Haptics = true;

        [Tooltip("Scales every vibration below.")]
        [SerializeField, Range(0f, 2f)] float m_HapticStrength = 1f;

        [Tooltip("Steady rumble at the lightest pull that counts.")]
        [SerializeField, Range(0f, 1f)] float m_PullRumbleMin = 0.08f;

        [Tooltip("Steady rumble at a full-strength pull. It grows smoothly from the minimum, so harder pulls feel heavier.")]
        [SerializeField, Range(0f, 1f)] float m_PullRumbleMax = 0.6f;

        [Tooltip("How quickly the rumble builds when you start pulling, in seconds.")]
        [SerializeField, Range(0f, 0.3f)] float m_RumbleAttack = 0.035f;

        [Tooltip("How quickly the rumble fades when the pull ends, in seconds.")]
        [SerializeField, Range(0f, 0.5f)] float m_RumbleRelease = 0.12f;

        [Tooltip("The faint feel of water on the free forward swing, or any hand moving underwater without pushing. 0 = nothing.")]
        [SerializeField, Range(0f, 0.3f)] float m_RecoveryRumble = 0.04f;

        [Tooltip("Water rushing past your hands while you glide fast.")]
        [SerializeField, Range(0f, 0.3f)] float m_GlideRumble = 0.05f;

        [Tooltip("How grainy and bubbly the rumble feels. 0 = perfectly smooth.")]
        [SerializeField, Range(0f, 1f)] float m_BubblyTexture = 0.5f;

        [Tooltip("Tiny ticks as bubbles leave your hand.")]
        [SerializeField, Range(0f, 1f)] float m_BubbleTicks = 0.22f;

        [Tooltip("Bump when a hand slaps through the water surface at full speed. Slower hands bump softer.")]
        [SerializeField, Range(0f, 1f)] float m_SurfaceSlap = 0.85f;

        [Tooltip("How long a surface slap lasts, in seconds.")]
        [SerializeField, Range(0.01f, 0.3f)] float m_SlapLength = 0.08f;

        [Tooltip("Rumble in both hands when your head goes under.")]
        [SerializeField, Range(0f, 1f)] float m_PlungeRumble = 0.55f;

        [Header("Sounds")]
        [SerializeField, Range(0f, 1f)] float m_Volume = 1f;

        [Tooltip("Looping rush of water while you pull, louder and higher the harder you pull. Empty = generated.")]
        [SerializeField] AudioClip m_PullSound;

        [SerializeField, Range(0f, 1f)] float m_PullVolume = 0.12f;

        [Tooltip("Hand splashes; one is picked at random each time. Empty = generated.")]
        [SerializeField] AudioClip[] m_SplashSounds;

        [SerializeField, Range(0f, 1f)] float m_SplashVolume = 0.3f;

        [Tooltip("Your head going under. Empty = generated.")]
        [SerializeField] AudioClip m_PlungeSound;

        [SerializeField, Range(0f, 1f)] float m_PlungeVolume = 0.35f;

        [Tooltip("Single bubble bloops; one is picked at random each time. Empty = generated.")]
        [SerializeField] AudioClip[] m_BubbleSounds;

        [SerializeField, Range(0f, 1f)] float m_BubbleVolume = 0.12f;

        [Tooltip("Each sound's pitch goes randomly up or down by up to this much (0.12 = 12%).")]
        [SerializeField, Range(0f, 0.5f)] float m_PitchVariation = 0.12f;

        [Tooltip("Optional mixer group for every swim sound.")]
        [SerializeField] AudioMixerGroup m_Output;

        [Header("Bubbles")]
        [SerializeField] bool m_HandBubbles = true;

        [Tooltip("Bubbles per second from a hand pulling at full strength.")]
        [SerializeField, Range(0f, 80f)] float m_BubbleRate = 22f;

        [SerializeField] Color m_BubbleColor = new Color(0.85f, 0.95f, 1f, 1f);

        [Tooltip("Smallest and biggest bubble, in meters.")]
        [SerializeField] Vector2 m_BubbleSize = new Vector2(0.012f, 0.04f);

        [Tooltip("How fast bubbles float up, in meters per second.")]
        [SerializeField, Range(0f, 3f)] float m_BubbleRise = 0.55f;

        [Tooltip("Little streams of bubbles from your mouth now and then while your head is underwater.")]
        [SerializeField] bool m_BreathBubbles = true;

        [Tooltip("Shortest and longest wait between breaths, in seconds.")]
        [SerializeField] Vector2 m_BreathInterval = new Vector2(3.5f, 7f);

        [Header("Splashes")]
        [SerializeField] bool m_Splashes = true;

        [SerializeField] Color m_SplashColor = new Color(0.92f, 0.97f, 1f, 1f);

        [Tooltip("Smallest and biggest droplet, in meters.")]
        [SerializeField] Vector2 m_DropletSize = new Vector2(0.014f, 0.035f);

        [Tooltip("Multiplies how many droplets every splash throws.")]
        [SerializeField, Range(0f, 4f)] float m_SplashAmount = 1f;

        [Tooltip("Slowest a hand can cross the surface and still splash, in meters per second. Slower touches just get a soft tick.")]
        [SerializeField, Range(0f, 5f)] float m_MinSplashSpeed = 0.6f;

        [Tooltip("Slowest your head can go under or come up, in meters per second, and still splash. Bobbing at the surface or waves washing over your head stay quiet.")]
        [SerializeField, Range(0f, 5f)] float m_MinHeadSplashSpeed = 1f;

        [Tooltip("Speed (meters per second) of the biggest, loudest, strongest splash.")]
        [SerializeField, Range(0.5f, 15f)] float m_FullSplashSpeed = 4f;

        readonly Hand m_Left = new Hand { side = VRHand.Left };
        readonly Hand m_Right = new Hand { side = VRHand.Right };
        Rigidbody m_Body;
        GorillaRopeClimber m_Climber;
        UnderwaterView m_View;
        WaterVolume m_Water;
        float m_Submerged;
        bool m_HeadUnder;
        float m_BreathTimer;
        float m_LastHeadHeight = float.NaN;
        float m_HeadSplashCooldown;
        int m_BreathLeft;
        float m_BreathGap;
        float m_BubbleSoundCooldown;
        WaterParticles m_Bubbles;
        WaterParticles m_Droplets;
        Transform m_SoundRoot;
        AudioSource[] m_Voices;
        int m_NextVoice;

        /// <summary>Your head just went under. Gives the water.</summary>
        public event Action<WaterVolume> HeadWentUnder;

        /// <summary>Your head just came back out. Gives the water.</summary>
        public event Action<WaterVolume> HeadCameUp;

        /// <summary>A hand or your head crossed the surface: where, and how hard (0 = gentle, 1 = biggest splash).</summary>
        public event Action<Vector3, float> Splashed;

        /// <summary>True while the water is holding you up at all.</summary>
        public bool IsSwimming => m_Submerged > 0.01f;

        /// <summary>0 = out of the water, 1 = all the way in (head under).</summary>
        public float Submerged => m_Submerged;

        public bool HeadUnderwater => m_HeadUnder;

        /// <summary>The water your body is in, or null.</summary>
        public WaterVolume Water => m_Water;

        /// <summary>The underwater look and sound on your head camera.</summary>
        public UnderwaterView View => m_View;

        /// <summary>How hard a hand is pulling right now (0-1).</summary>
        public float GetPull(VRHand hand) => (hand == VRHand.Left ? m_Left : m_Right).pull;

        /// <summary>Finds the Gorilla Locomotion player and gives it a swimmer if it doesn't have one yet.</summary>
        public static GorillaSwimmer EnsureOnPlayer()
        {
            var existing = FindFirstObjectByType<GorillaSwimmer>();
            if (existing != null) return existing;
            var player = Player.Instance != null ? Player.Instance : FindFirstObjectByType<Player>();
            if (player == null) return null;
            return player.gameObject.AddComponent<GorillaSwimmer>();
        }

        static AnimationCurve DefaultPowerByDirection()
        {
            return new AnimationCurve(
                new Keyframe(-1f, 0f, 0f, 0f),
                new Keyframe(-0.3f, 0f, 0f, 0f),
                new Keyframe(0.1f, 0.5f, 1.1f, 1.1f),
                new Keyframe(1f, 1f, 0f, 0f));
        }

        void Reset()
        {
            m_Player = GetComponent<Player>();
        }

        void OnValidate()
        {
            m_PullRumbleMax = Mathf.Max(m_PullRumbleMax, m_PullRumbleMin);
            m_FullSplashSpeed = Mathf.Max(m_FullSplashSpeed, m_MinSplashSpeed + 0.1f);
            m_BubbleSize = new Vector2(Mathf.Max(0.001f, m_BubbleSize.x), Mathf.Max(m_BubbleSize.x, m_BubbleSize.y));
            m_DropletSize = new Vector2(Mathf.Max(0.001f, m_DropletSize.x), Mathf.Max(m_DropletSize.x, m_DropletSize.y));
            m_BreathInterval = new Vector2(Mathf.Max(0.2f, m_BreathInterval.x), Mathf.Max(m_BreathInterval.x, m_BreathInterval.y));
        }

        void OnEnable()
        {
            if (m_Player == null) m_Player = GetComponent<Player>();
            if (m_Player == null) m_Player = FindFirstObjectByType<Player>();
            if (m_Player == null)
            {
                Debug.LogWarning("Gorilla Swimmer needs a GorillaLocomotion.Player in the scene.", this);
                enabled = false;
                return;
            }

            m_Body = m_Player.GetComponent<Rigidbody>();
            m_Climber = m_Player.GetComponent<GorillaRopeClimber>();
            m_Left.haptics ??= new SwimHaptics(VRHand.Left);
            m_Right.haptics ??= new SwimHaptics(VRHand.Right);
            m_Left.tracked = m_Right.tracked = false;

            var head = m_Player.headCollider != null ? m_Player.headCollider.transform : null;
            Camera camera = head != null ? head.GetComponent<Camera>() : null;
            if (camera == null && head != null) camera = head.GetComponentInParent<Camera>();
            if (camera == null) camera = Camera.main;
            m_View = UnderwaterView.Attach(camera);

            if (m_SoundRoot == null)
            {
                m_SoundRoot = new GameObject("Swim Sounds").transform;
                m_SoundRoot.SetParent(transform, false);
            }
            m_Bubbles ??= new WaterParticles("Swim Bubbles", null, WaterPixelArt.Bubble, 400, 0f, 2.5f, 0.12f,
                WaterParticles.Side.Underwater, true, null);
            m_Droplets ??= new WaterParticles("Swim Splash", null, WaterPixelArt.Droplet, 500, 1f, 0.35f, 0f,
                WaterParticles.Side.AboveWater, true, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.55f)));
        }

        void OnDisable()
        {
            StopHand(m_Left);
            StopHand(m_Right);
            m_Submerged = 0f;
            m_HeadUnder = false;
            m_Water = null;
            m_LastHeadHeight = float.NaN;
        }

        void OnDestroy()
        {
            m_Bubbles?.Destroy();
            m_Droplets?.Destroy();
        }

        static void StopHand(Hand hand)
        {
            hand.tracked = false;
            hand.pull = 0f;
            hand.rushLevel = 0f;
            hand.haptics?.Stop();
            if (hand.rush != null) hand.rush.Stop();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_Player == null || m_Player.headCollider == null || dt <= 0f) return;

            Transform root = m_Player.transform;
            Transform head = m_Player.headCollider.transform;
            Vector3 bodyVelocity = m_Body != null ? m_Body.linearVelocity : Vector3.zero;

            UpdateBody(head.position, bodyVelocity, dt);

            Vector3 push = UpdateHand(m_Left, root, head.forward, bodyVelocity, dt) + UpdateHand(m_Right, root, head.forward, bodyVelocity, dt);
            bool hanging = m_Climber != null && m_Climber.IsHanging;
            if (!hanging && m_Body != null && !m_Body.isKinematic && push != Vector3.zero) AddSwimVelocity(push * dt);

            UpdateBreath(head, dt);
            m_BubbleSoundCooldown -= dt;

            // Bubbles live until the moving surface; droplets until they fall back in.
            m_Bubbles.Gravity = -m_BubbleRise * 2.5f / Mathf.Abs(Physics.gravity.y);
            m_Bubbles.Cull();
            m_Droplets.Cull();
        }

        void FixedUpdate()
        {
            if (m_Body == null || m_Submerged <= 0.0001f || m_Body.isKinematic || !m_Body.useGravity) return;
            if (m_Climber != null && m_Climber.IsHanging) return;

            // Take away most of gravity, then let the water slow you down.
            float dt = Time.fixedDeltaTime;
            float gravity = Mathf.Lerp(1f, m_WaterGravity, m_Submerged);
            m_Body.AddForce(Physics.gravity * (gravity - 1f), ForceMode.Acceleration);

            Vector3 velocity = m_Body.linearVelocity;
            float drag = m_WaterDrag * m_Submerged;
            if (velocity.sqrMagnitude > m_MaxSwimSpeed * m_MaxSwimSpeed) drag *= 2f;
            m_Body.linearVelocity = velocity * Mathf.Exp(-drag * dt);
        }

        void UpdateBody(Vector3 head, Vector3 velocity, float dt)
        {
            var water = WaterVolume.Find(head, m_BodyLength);
            float depth = water != null ? water.SurfaceHeight(head) - head.y : float.NegativeInfinity;
            float target = water != null ? Smooth01((depth + m_BodyLength) / m_BodyLength) : 0f;
            m_Submerged = Mathf.Lerp(m_Submerged, target, 1f - Mathf.Exp(-dt / 0.04f));
            if (m_Submerged < 0.001f) m_Submerged = 0f;
            if (water != null || m_Submerged <= 0f) m_Water = water;

            // How fast your head itself moves up or down (waves washing over a still head don't count).
            float headSpeed = float.IsNaN(m_LastHeadHeight) || Mathf.Abs(head.y - m_LastHeadHeight) > k_TeleportDistance ? 0f : (head.y - m_LastHeadHeight) / dt;
            m_LastHeadHeight = head.y;
            m_HeadSplashCooldown -= dt;

            bool under = m_HeadUnder ? depth > -k_SurfaceHysteresis : depth > k_SurfaceHysteresis;
            if (under == m_HeadUnder) return;
            m_HeadUnder = under;
            var crossed = water != null ? water : m_Water;
            if (under) HeadEntered(crossed, head, velocity, Mathf.Max(0f, -headSpeed));
            else HeadLeft(crossed, head, velocity, Mathf.Max(0f, headSpeed));
        }

        /// <summary>True (and starts the cooldown) if crossing the surface at this speed should splash.</summary>
        bool HeadSplashes(float speed)
        {
            if (speed < m_MinHeadSplashSpeed || m_HeadSplashCooldown > 0f) return false;
            m_HeadSplashCooldown = 0.75f;
            return true;
        }

        void HeadEntered(WaterVolume water, Vector3 head, Vector3 velocity, float speed)
        {
            m_BreathTimer = UnityEngine.Random.Range(0.6f, 1.4f);
            HeadWentUnder?.Invoke(water);
            if (!HeadSplashes(speed)) return;

            float strength = Mathf.InverseLerp(m_MinHeadSplashSpeed, 6f, speed);
            Vector3 at = water != null ? new Vector3(head.x, water.SurfaceHeight(head), head.z) : head;

            PlayAt(m_PlungeSound != null ? m_PlungeSound : WaterSynth.Plunge, head, m_PlungeVolume * Mathf.Lerp(0.3f, 1f, strength), -0.1f * strength, true);
            if (m_Haptics)
            {
                m_Left.haptics.Bump(m_PlungeRumble * Mathf.Lerp(0.35f, 1f, strength), 0.3f);
                m_Right.haptics.Bump(m_PlungeRumble * Mathf.Lerp(0.35f, 1f, strength), 0.3f);
            }
            if (m_Splashes && strength > 0.05f) EmitSplash(at, velocity, Mathf.RoundToInt(Mathf.Lerp(8f, 45f, strength) * m_SplashAmount), strength, 0.22f);
            if (m_HandBubbles)
            {
                int bubbles = Mathf.RoundToInt(Mathf.Lerp(8f, 40f, strength));
                for (int i = 0; i < bubbles; i++)
                    EmitBubble(head + Vector3.down * 0.15f + UnityEngine.Random.insideUnitSphere * 0.35f, velocity * 0.15f, 1.2f);
            }

            Splashed?.Invoke(at, strength);
        }

        void HeadLeft(WaterVolume water, Vector3 head, Vector3 velocity, float speed)
        {
            HeadCameUp?.Invoke(water);
            if (water == null) return;

            bool hanging = m_Climber != null && m_Climber.IsHanging;
            if (!hanging && m_Body != null && !m_Body.isKinematic && velocity.y > 1.5f && m_LeapBoost > 1f)
            {
                Vector3 v = m_Body.linearVelocity;
                v.y *= m_LeapBoost;
                m_Body.linearVelocity = v;
            }

            if (!HeadSplashes(speed)) return;
            float strength = Mathf.InverseLerp(m_MinHeadSplashSpeed, 5f, speed);
            Vector3 at = new Vector3(head.x, water.SurfaceHeight(head), head.z);
            PlayAt(WaterSynth.Pick(m_SplashSounds, WaterSynth.Splashes), at, m_SplashVolume * Mathf.Lerp(0.2f, 0.7f, strength), 0.05f, false);
            if (m_Splashes) EmitSplash(at, velocity, Mathf.RoundToInt(Mathf.Lerp(5f, 28f, strength) * m_SplashAmount), strength * 0.8f, 0.18f);
            Splashed?.Invoke(at, strength * 0.8f);
        }

        Vector3 UpdateHand(Hand hand, Transform root, Vector3 look, Vector3 bodyVelocity, float dt)
        {
            bool left = hand.side == VRHand.Left;
            Transform controller = left ? m_Player.leftHandTransform : m_Player.rightHandTransform;
            if (controller == null) return Vector3.zero;

            Vector3 point = HandPoint(controller, left ? m_Player.leftHandOffset : m_Player.rightHandOffset);
            // Arm movement in the player's own space, so turning or being carried along doesn't count as a stroke.
            Vector3 local = root.InverseTransformPoint(point);
            if (!hand.tracked || (local - hand.lastLocal).sqrMagnitude > k_TeleportDistance * k_TeleportDistance)
            {
                hand.tracked = true;
                hand.lastLocal = local;
                hand.armVelocity = Vector3.zero;
                hand.point = point;
                hand.underwater = WaterVolume.Find(point) != null;
                return Vector3.zero;
            }

            Vector3 armVelocity = root.TransformVector(local - hand.lastLocal) / dt;
            hand.lastLocal = local;
            hand.armVelocity = m_HandSmoothing > 0f ? Vector3.Lerp(hand.armVelocity, armVelocity, 1f - Mathf.Exp(-dt / m_HandSmoothing)) : armVelocity;
            hand.point = point;
            hand.splashCooldown -= dt;

            var water = WaterVolume.Find(point, k_SurfaceHysteresis + 0.02f);
            float depth = water != null ? water.SurfaceHeight(point) - point.y : -1f;
            bool under = hand.underwater ? depth > -k_SurfaceHysteresis : depth > k_SurfaceHysteresis;
            if (under != hand.underwater)
            {
                hand.underwater = under;
                if (water != null) HandCrossedSurface(hand, water, point, hand.armVelocity + bodyVelocity, under);
            }

            float handPower = under ? Mathf.Clamp01(depth / m_HandDepthForFullPower) : 0f;
            bool touching = left ? m_Player.wasLeftHandTouching : m_Player.wasRightHandTouching;
            float swim = Smooth01(m_Submerged / 0.35f);

            Vector3 push = Vector3.zero;
            float pull = 0f;
            float moving = 0f;
            if (handPower > 0f && !touching)
            {
                moving = Mathf.Clamp01((hand.armVelocity.magnitude - m_MinPullSpeed) / m_FullPullSpeed) * handPower;

                // The water pushes back against the hand, so you go the opposite way the hand moves through it.
                Vector3 throughWater = hand.armVelocity + bodyVelocity * m_YourSpeedCounts;
                float speed = throughWater.magnitude;
                float effective = speed - m_MinPullSpeed;
                if (effective > 0f)
                {
                    Vector3 direction = -throughWater / speed;
                    float facing = Mathf.Max(0f, m_PowerByDirection.Evaluate(Vector3.Dot(direction, look)));
                    pull = Mathf.Clamp01(effective / m_FullPullSpeed * facing) * handPower;
                    push = direction * (Mathf.Pow(effective, m_PullCurve) * facing * handPower * swim * m_StrokePower);
                }
            }
            hand.pull = pull;

            Feel(hand, pull, moving, under, bodyVelocity, dt);
            return push;
        }

        Vector3 HandPoint(Transform controller, Vector3 offset)
        {
            // Limited to arm's reach the same way Gorilla Locomotion does it.
            Vector3 point = controller.position + controller.rotation * offset;
            Vector3 head = m_Player.headCollider.transform.position;
            Vector3 reach = point - head;
            float max = m_Player.maxArmLength;
            return reach.sqrMagnitude < max * max ? point : head + reach.normalized * max;
        }

        void AddSwimVelocity(Vector3 change)
        {
            Vector3 velocity = m_Body.linearVelocity;
            Vector3 next = velocity + change;
            // Strokes can steer at any speed, but only add speed up to Max Swim Speed.
            float limit = Mathf.Max(m_MaxSwimSpeed, velocity.magnitude);
            if (next.sqrMagnitude > limit * limit) next = next.normalized * limit;
            m_Body.linearVelocity = next;
        }

        void Feel(Hand hand, float pull, float moving, bool under, Vector3 bodyVelocity, float dt)
        {
            float rumble = pull > 0.001f ? Mathf.Lerp(m_PullRumbleMin, m_PullRumbleMax, Mathf.Pow(pull, 0.8f)) : 0f;
            if (under)
            {
                rumble = Mathf.Max(rumble, m_RecoveryRumble * moving);
                rumble = Mathf.Max(rumble, m_GlideRumble * Mathf.Clamp01(bodyVelocity.magnitude / m_MaxSwimSpeed) * m_Submerged);
            }
            hand.haptics.Rumble(rumble, m_RumbleAttack, m_RumbleRelease, dt);
            if (m_Haptics) hand.haptics.Send(m_BubblyTexture, Mathf.Max(pull, moving), m_HapticStrength, dt);

            float rush = under ? Mathf.Max(Mathf.Pow(pull, 0.9f), moving * 0.25f) : 0f;
            hand.rushLevel = Mathf.MoveTowards(hand.rushLevel, rush, dt * (rush > hand.rushLevel ? 12f : 4f));
            UpdateRush(hand);

            if (m_HandBubbles && under && pull > 0.05f)
            {
                hand.bubbleBudget += m_BubbleRate * pull * dt;
                for (; hand.bubbleBudget >= 1f; hand.bubbleBudget -= 1f)
                {
                    EmitBubble(hand.point + UnityEngine.Random.insideUnitSphere * 0.035f, -hand.armVelocity * 0.25f, 1f);
                    if (m_Haptics && UnityEngine.Random.value < 0.6f) hand.haptics.Bump(m_BubbleTicks * UnityEngine.Random.Range(0.5f, 1f), 0.025f);
                    if (UnityEngine.Random.value < 0.15f) PlayBubble(hand.point, 1f);
                }
            }
            else
            {
                hand.bubbleBudget = 0f;
            }
        }

        void HandCrossedSurface(Hand hand, WaterVolume water, Vector3 point, Vector3 velocity, bool entering)
        {
            // Only the hand's own movement through the surface counts, so waves washing past a still hand (or
            // sliding along the top while you swim) stay quiet.
            float vertical = Mathf.Max(0f, entering ? -velocity.y : velocity.y);
            float speed = vertical + (vertical > 0.3f ? 0.35f * new Vector2(velocity.x, velocity.z).magnitude : 0f);
            if (hand.splashCooldown > 0f) return;
            if (speed < m_MinSplashSpeed)
            {
                if (m_Haptics && entering && speed > 0.15f) hand.haptics.Bump(m_SurfaceSlap * 0.12f, 0.03f);
                return;
            }
            hand.splashCooldown = 0.3f;

            float strength = Mathf.InverseLerp(m_MinSplashSpeed, m_FullSplashSpeed, speed);
            float side = entering ? 1f : 0.5f;
            Vector3 at = new Vector3(point.x, water.SurfaceHeight(point), point.z);

            if (m_Haptics) hand.haptics.Bump(m_SurfaceSlap * Mathf.Lerp(0.3f, 1f, strength) * (entering ? 1f : 0.45f), m_SlapLength * Mathf.Lerp(0.7f, 1.3f, strength));
            PlayAt(WaterSynth.Pick(m_SplashSounds, WaterSynth.Splashes), at, m_SplashVolume * Mathf.Lerp(0.25f, 1f, strength) * side, -0.15f * strength, false);
            if (m_Splashes) EmitSplash(at, velocity, Mathf.RoundToInt(Mathf.Lerp(3f, 18f, strength) * m_SplashAmount * side), strength, 0.06f);
            if (entering && m_HandBubbles)
            {
                int bubbles = Mathf.RoundToInt(Mathf.Lerp(2f, 9f, strength));
                for (int i = 0; i < bubbles; i++)
                    EmitBubble(at + Vector3.down * UnityEngine.Random.Range(0.04f, 0.2f) + UnityEngine.Random.insideUnitSphere * 0.05f, velocity * 0.2f, 1f);
            }
            Splashed?.Invoke(at, strength * side);
        }

        void UpdateBreath(Transform head, float dt)
        {
            if (!m_BreathBubbles || !m_HeadUnder)
            {
                m_BreathLeft = 0;
                return;
            }

            if (m_BreathLeft > 0)
            {
                m_BreathGap -= dt;
                if (m_BreathGap > 0f) return;
                m_BreathLeft--;
                m_BreathGap = UnityEngine.Random.Range(0.03f, 0.09f);
                Vector3 mouth = head.position + head.forward * 0.09f - head.up * 0.07f;
                EmitBubble(mouth + UnityEngine.Random.insideUnitSphere * 0.015f, head.forward * 0.15f + Vector3.up * 0.25f, 1.3f);
                if (UnityEngine.Random.value < 0.5f) PlayBubble(mouth, 0.6f);
                return;
            }

            m_BreathTimer -= dt;
            if (m_BreathTimer > 0f) return;
            m_BreathLeft = UnityEngine.Random.Range(4, 9);
            m_BreathGap = 0f;
            m_BreathTimer = UnityEngine.Random.Range(m_BreathInterval.x, m_BreathInterval.y);
        }

        void EmitBubble(Vector3 position, Vector3 velocity, float sizeScale)
        {
            float size = UnityEngine.Random.Range(m_BubbleSize.x, m_BubbleSize.y) * sizeScale;
            Vector3 drift = UnityEngine.Random.insideUnitSphere * 0.08f + Vector3.up * m_BubbleRise * 0.4f;
            m_Bubbles.Emit(position, velocity + drift, size, UnityEngine.Random.Range(1.5f, 3.5f), m_BubbleColor);
        }

        void EmitSplash(Vector3 at, Vector3 incoming, int count, float strength, float radius)
        {
            Vector3 carry = new Vector3(incoming.x, 0f, incoming.z) * 0.2f;
            for (int i = 0; i < count; i++)
            {
                Vector2 around = UnityEngine.Random.insideUnitCircle;
                Vector3 outward = new Vector3(around.x, 0f, around.y);
                Vector3 position = at + outward * radius + Vector3.up * 0.01f;
                Vector3 velocity = outward * Mathf.Lerp(0.4f, 1.6f, strength) * UnityEngine.Random.Range(0.4f, 1f)
                    + Vector3.up * Mathf.Lerp(1f, 3.4f, strength) * UnityEngine.Random.Range(0.45f, 1f) + carry;
                float size = UnityEngine.Random.Range(m_DropletSize.x, m_DropletSize.y);
                m_Droplets.Emit(position, velocity, size, UnityEngine.Random.Range(0.6f, 1.3f), m_SplashColor);
            }
        }

        void PlayBubble(Vector3 position, float volume)
        {
            if (m_BubbleSoundCooldown > 0f) return;
            m_BubbleSoundCooldown = 0.14f;
            PlayAt(WaterSynth.Pick(m_BubbleSounds, WaterSynth.Bubbles), position, m_BubbleVolume * volume * UnityEngine.Random.Range(0.45f, 1f), 0f, true);
        }

        void UpdateRush(Hand hand)
        {
            if (hand.rush == null)
            {
                if (hand.rushLevel <= 0.001f) return;
                var follower = hand.side == VRHand.Left ? m_Player.leftHandFollower : m_Player.rightHandFollower;
                var go = new GameObject($"{hand.side} Swim Rush");
                go.transform.SetParent(follower != null ? follower : transform, false);
                hand.rush = go.AddComponent<AudioSource>();
                hand.rush.loop = true;
                hand.rush.playOnAwake = false;
                hand.rush.spatialBlend = 1f;
                hand.rush.dopplerLevel = 0f;
                hand.rush.bypassListenerEffects = true; // Already sounds like it's underwater.
                hand.rush.timeSamples = 0;
            }

            var clip = m_PullSound != null ? m_PullSound : WaterSynth.Rush;
            if (hand.rush.clip != clip) hand.rush.clip = clip;
            hand.rush.outputAudioMixerGroup = m_Output;
            hand.rush.volume = m_PullVolume * m_Volume * hand.rushLevel;
            hand.rush.pitch = 0.85f + 0.45f * hand.rushLevel;
            if (hand.rushLevel > 0.001f && !hand.rush.isPlaying)
            {
                // Start somewhere random in the loop so the two hands never sound identical.
                hand.rush.timeSamples = UnityEngine.Random.Range(0, Mathf.Max(1, clip.samples - 1));
                hand.rush.Play();
            }
            else if (hand.rushLevel <= 0.001f && hand.rush.isPlaying)
            {
                hand.rush.Stop();
            }
        }

        void PlayAt(AudioClip clip, Vector3 position, float volume, float pitchShift, bool underwaterSound)
        {
            if (clip == null || volume <= 0f || m_Volume <= 0f) return;
            var voice = NextVoice();
            voice.transform.position = position;
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume * m_Volume);
            voice.pitch = Mathf.Max(0.1f, 1f + pitchShift + UnityEngine.Random.Range(-m_PitchVariation, m_PitchVariation));
            voice.outputAudioMixerGroup = m_Output;
            // Underwater sounds already sound underwater, so the muffling doesn't touch them. Splashes happen in the
            // air, so they get muffled when your head is under.
            voice.bypassListenerEffects = underwaterSound;
            voice.Play();
        }

        // A few voices take turns so quick sounds overlap instead of cutting each other off.
        AudioSource NextVoice()
        {
            if (m_Voices == null)
            {
                m_Voices = new AudioSource[k_Voices];
                for (int i = 0; i < k_Voices; i++)
                {
                    var go = new GameObject("Swim Sound");
                    go.transform.SetParent(m_SoundRoot, false);
                    var source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 1f;
                    source.dopplerLevel = 0f;
                    m_Voices[i] = source;
                }
            }
            var voice = m_Voices[m_NextVoice];
            m_NextVoice = (m_NextVoice + 1) % k_Voices;
            return voice;
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
