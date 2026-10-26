using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KapiRope
{
    public enum RopeEndCap
    {
        Flat,
        Rounded
    }

    public enum RopeSegmentMode
    {
        /// <summary>A physics point every Segment Length. Best for long ropes that coil on the floor.</summary>
        Automatic,

        /// <summary>An exact number of straight segments, like Gorilla Tag ropes.</summary>
        FixedCount
    }

    /// <summary>
    /// A physics rope hanging from this object. It swings, gets shoved by players, piles up on the floor when
    /// it's longer than the drop, and can be grabbed, climbed and swung on with a <see cref="GorillaRopeClimber"/>.
    /// The rope draws itself as a smooth tube mesh, so this object's position is the top of the rope.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)] // After GorillaLocomotion.Player (0) and GorillaRopeClimber (50) have moved the hands.
    [AddComponentMenu("Kapi Rope/Rope")]
    public sealed partial class Rope : MonoBehaviour
    {
        static readonly List<Rope> s_Active = new List<Rope>();

        /// <summary>Every enabled rope in the loaded scenes.</summary>
        public static IReadOnlyList<Rope> Active => s_Active;

        [Header("Size")]
        [Tooltip("Rope length in meters, from this object (the anchor) to the free end. Change it any time, even in Play mode; rope longer than the drop piles up on the floor.")]
        [SerializeField, Min(0.1f)] float m_Length = 4f;

        [Tooltip("Rope thickness (radius) in meters.")]
        [SerializeField, Min(0.005f)] float m_Radius = 0.03f;

        [Tooltip("Automatic: a physics point every Segment Length, best for long ropes that coil. Fixed Count: an exact number of rigid straight segments, like Gorilla Tag ropes.")]
        [SerializeField] RopeSegmentMode m_SegmentMode = RopeSegmentMode.Automatic;

        [Tooltip("Spacing between physics points (Automatic mode). Smaller bends and coils more naturally but costs more CPU.")]
        [SerializeField, Range(0.02f, 0.5f)] float m_SegmentLength = 0.07f;

        [Tooltip("Exact number of segments (Fixed Count mode). Each segment stays perfectly straight and only bends at the joints. 4 gives a chunky Gorilla Tag style rope.")]
        [SerializeField, Range(1, 300)] int m_SegmentCount = 4;

        [Tooltip("Thickness multiplier from the anchor (0) to the free end (1). Add a bump near 1 for a knot at the bottom.")]
        [SerializeField] AnimationCurve m_ThicknessAlongLength = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Optional: also tie the free end to this object, for ropes strung between two points.")]
        [SerializeField] Transform m_EndAttachment;

        [Header("Physics")]
        [Tooltip("Gravity multiplier.")]
        [SerializeField] float m_Gravity = 1f;

        [Tooltip("How quickly swinging dies down in the air. 0 = swings forever.")]
        [SerializeField, Min(0f)] float m_AirDrag = 0.35f;

        [Tooltip("How much the rope resists bending. 0 = floppy like string, 1 = stiff like a thick ship rope.")]
        [SerializeField, Range(0f, 1f)] float m_Stiffness = 0.15f;

        [Tooltip("Physics steps per second. Higher is steadier when players yank it around, but costs more CPU.")]
        [SerializeField, Range(60, 600)] int m_SimulationRate = 240;

        [Tooltip("Solver passes per step. More = less stretchy.")]
        [SerializeField, Range(1, 8)] int m_Iterations = 2;

        [Tooltip("Stop simulating once the rope is still and nothing is near it. Saves a lot of CPU on Quest.")]
        [SerializeField] bool m_SleepWhenStill = true;

        [Header("Collision")]
        [Tooltip("Layers the rope collides with: floors, walls, props. Player hands, head and body are handled by the Gorilla Rope Climber.")]
        [SerializeField] LayerMask m_CollisionLayers = 1;

        [Tooltip("How much the rope grips surfaces it lies on. High friction keeps coils where they land.")]
        [SerializeField, Range(0f, 1f)] float m_Friction = 0.6f;

        [Tooltip("The rope collides with itself, so extra length piles up instead of passing through itself.")]
        [SerializeField] bool m_SelfCollision = true;

        [Header("Bumping (how much force it takes to move)")]
        [Tooltip("How much the rope resists being pushed. 0 = light as string, gets shoved instantly. 1 = heavy ship rope; hands sink into it and it moves sluggishly.")]
        [SerializeField, Range(0f, 1f)] float m_Heaviness = 0.25f;

        [Tooltip("How much of a hit's speed turns into swing. 0 = it only slides out of the way, 1 = realistic, above 1 = exaggerated.")]
        [SerializeField, Range(0f, 3f)] float m_BumpStrength = 1f;

        [Tooltip("Hits slower than this (m/s) only nudge the rope aside without making it swing. Raise it so only real smacks send it flying.")]
        [SerializeField, Min(0f)] float m_MinBumpSpeed = 0.4f;

        [Tooltip("Caps how fast a single hit can send the rope (m/s).")]
        [SerializeField, Min(0f)] float m_MaxBumpSpeed = 8f;

        [Header("Grabbing")]
        [Tooltip("Players can grab this rope with grip.")]
        [SerializeField] bool m_Grabbable = true;

        [Tooltip("How close (meters from the rope's surface) a hand must be to grab it.")]
        [SerializeField, Min(0f)] float m_GrabRadius = 0.1f;

        [Tooltip("Loosen your grip (half squeeze) to slide down. Full squeeze holds tight, letting go drops you.")]
        [SerializeField] bool m_AllowSliding = true;

        [Tooltip("Fastest you slide with a barely-held grip (m/s).")]
        [SerializeField, Min(0f)] float m_MaxSlideSpeed = 5f;

        [Tooltip("You keep all your swing speed when you let go, multiplied by this, like Gorilla Tag's jump multiplier. 1 = exactly the swing speed.")]
        [SerializeField, Range(0f, 3f)] float m_ReleaseBoost = 1.3f;

        [Tooltip("How much a last pull on the rope flings you when you let go. Pull down hard and release to launch upward.")]
        [SerializeField, Range(0f, 2f)] float m_ClimbFling = 0.8f;

        [Tooltip("Air drag on a player swinging on the rope. 0 = swings forever.")]
        [SerializeField, Min(0f)] float m_SwingDrag = 0.05f;

        [Tooltip("Fastest a player can leave the rope (m/s).")]
        [SerializeField, Min(0f)] float m_MaxReleaseSpeed = 10f;

        [Header("Look")]
        [Tooltip("On: the rope is drawn as one smooth curve through its points. Off: each segment is its own straight piece with a rounded knuckle at every joint, like Gorilla Tag ropes.")]
        [SerializeField] bool m_SmoothBends = true;

        [Tooltip("Vertices around the rope. 8-12 looks round in VR.")]
        [SerializeField, Range(3, 32)] int m_RadialSegments = 10;

        [Tooltip("Extra mesh rings between physics points when Smooth Bends is on, so bends are curves instead of corners.")]
        [SerializeField, Range(1, 8)] int m_Smoothness = 3;

        [Tooltip("Shape of the free (bottom) end. The top is always flat so this object sits exactly at the top of the rope.")]
        [SerializeField] RopeEndCap m_EndCap = RopeEndCap.Rounded;

        [Tooltip("Stretches the texture along the rope. 1 = square weave.")]
        [SerializeField, Min(0.01f)] float m_TextureScale = 1f;

        [Tooltip("Spins the texture around the rope, in turns per meter.")]
        [SerializeField] float m_TextureTwist;

        [Tooltip("Multiplied with the material color, per rope.")]
        [SerializeField] Color m_Tint = Color.white;

        [Tooltip("Colors and weave used by \"Make Braid Material\" in the inspector, and by the fallback material if none is assigned.")]
        [SerializeField] RopeBraidStyle m_BraidStyle = new RopeBraidStyle();

        [Header("Start")]
        [Tooltip("Let the rope fall and settle before the first frame, so it starts at rest instead of dropping.")]
        [SerializeField] bool m_SettleOnStart = true;

        [Tooltip("How long to pre-simulate when settling (seconds).")]
        [SerializeField, Range(0f, 5f)] float m_SettleTime = 1.5f;

        [Tooltip("Simulate in the Scene view without Play mode: drag the rope around, change its length and watch it coil.")]
        [SerializeField] bool m_PreviewInEditor = true;

        [Header("Reset When Stuck")]
        [Tooltip("If the rope ends up snagged or tangled (sitting still, but not hanging the way it started) and nobody is holding it, it smoothly goes back to its starting shape.")]
        [SerializeField] bool m_ReturnWhenStuck = true;

        [Tooltip("Seconds the rope has to sit snagged before it goes back.")]
        [SerializeField, Min(0f)] float m_StuckTime = 3f;

        [Tooltip("How far (m) any part of the rope has to be from where it started to count as snagged.")]
        [SerializeField, Min(0.05f)] float m_StuckDistance = 0.5f;

        [Tooltip("How long the trip back to the starting shape takes (seconds). It passes through anything in the way.")]
        [SerializeField, Min(0.05f)] float m_ReturnTime = 1f;

        readonly List<RopeGrip> m_Grips = new List<RopeGrip>();
        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_PropertyBlock;
        Material m_FallbackMaterial;
        bool m_Started;

        /// <summary>Called when something grabs the rope.</summary>
        public event Action<RopeGrip> Grabbed;

        /// <summary>Called when a grip lets go (or is dropped because the rope was disabled or shortened).</summary>
        public event Action<RopeGrip> Released;

        public float Length
        {
            get => m_Length;
            set
            {
                m_Length = Mathf.Max(0.1f, value);
                WakeUp();
            }
        }

        public float Radius
        {
            get => m_Radius;
            set => m_Radius = Mathf.Max(0.005f, value);
        }

        public RopeSegmentMode SegmentMode
        {
            get => m_SegmentMode;
            set
            {
                m_SegmentMode = value;
                WakeUp();
            }
        }

        /// <summary>Number of segments used in Fixed Count mode.</summary>
        public int SegmentCount
        {
            get => m_SegmentCount;
            set
            {
                m_SegmentCount = Mathf.Clamp(value, 1, 300);
                WakeUp();
            }
        }

        public bool SmoothBends
        {
            get => m_SmoothBends;
            set
            {
                m_SmoothBends = value;
                m_MeshDirty = true;
            }
        }

        public Vector3 AnchorPosition => transform.position;
        public Transform EndAttachment { get => m_EndAttachment; set { m_EndAttachment = value; WakeUp(); } }
        public LayerMask CollisionLayers { get => m_CollisionLayers; set => m_CollisionLayers = value; }
        public float Heaviness { get => m_Heaviness; set => m_Heaviness = Mathf.Clamp01(value); }
        public float BumpStrength { get => m_BumpStrength; set => m_BumpStrength = Mathf.Max(0f, value); }
        public float MinBumpSpeed { get => m_MinBumpSpeed; set => m_MinBumpSpeed = Mathf.Max(0f, value); }
        public bool Grabbable { get => m_Grabbable; set => m_Grabbable = value; }
        public float GrabRadius => m_GrabRadius;
        public bool AllowSliding { get => m_AllowSliding; set => m_AllowSliding = value; }
        public float MaxSlideSpeed => m_MaxSlideSpeed;
        public float ReleaseBoost => m_ReleaseBoost;
        public float ClimbFling => m_ClimbFling;
        public float SwingDrag => m_SwingDrag;
        public float MaxReleaseSpeed => m_MaxReleaseSpeed;
        public RopeBraidStyle BraidStyle => m_BraidStyle;

        /// <summary>The hands (or other things) currently holding the rope.</summary>
        public IReadOnlyList<RopeGrip> Grips => m_Grips;

        /// <summary>True while the rope is resting and skipping physics.</summary>
        public bool IsAsleep => m_Asleep;

        void OnEnable()
        {
            s_Active.Add(this);
            m_Renderer = GetComponent<MeshRenderer>();
            CreateMesh();
            m_Asleep = false;
#if UNITY_EDITOR
            EditorApplication.update += EditorUpdate;
            m_EditorDirty = true;
            m_MaterialChecked = false;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
            s_Active.Remove(this);
            for (int i = m_Grips.Count - 1; i >= 0; i--)
                Release(m_Grips[i]);
            DestroyProbe();
            DestroyMesh();
        }

        void Start()
        {
            m_Started = true;
            if (!Application.isPlaying) return;

#if UNITY_EDITOR
            if (NeedsRopeMaterial() && EditorDefaultMaterial != null) m_Renderer.sharedMaterial = EditorDefaultMaterial();
#endif
            if (m_Renderer.sharedMaterial == null)
            {
                m_FallbackMaterial = RopeBraidTexture.GetRuntimeMaterial(m_BraidStyle);
                if (m_FallbackMaterial != null) m_Renderer.sharedMaterial = m_FallbackMaterial;
            }

            ApplyTint();
            ResetShape();
        }

        void Update()
        {
            if (!Application.isPlaying || !m_Started) return;
            EnsureClimber();
            Tick(Time.deltaTime);
        }

        static int s_ClimberCheckFrame = -1;

        /// <summary>
        /// Ropes can only be grabbed through a GorillaRopeClimber on the player. If the scene doesn't have one
        /// (for example the rope was added by dragging the script on), put one on the Gorilla Locomotion player.
        /// A climber that exists but was turned off on purpose is left alone.
        /// </summary>
        static void EnsureClimber()
        {
            if (s_ClimberCheckFrame == Time.frameCount) return;
            s_ClimberCheckFrame = Time.frameCount;
            if (GorillaRopeClimber.ActiveCount > 0) return;

            var player = GorillaLocomotion.Player.Instance;
            if (player == null || player.GetComponent<GorillaRopeClimber>() != null) return;
            player.gameObject.AddComponent<GorillaRopeClimber>();
            Debug.Log($"Kapi Rope: added a Gorilla Rope Climber to \"{player.name}\" so ropes can be grabbed. Add it in the editor to change its settings.", player);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (m_MeshDirty) BuildMesh();
        }

        /// <summary>Lays the rope out straight down (coiling the extra on the floor) and settles it.</summary>
        public void ResetShape()
        {
            Resize(true);
            if (m_SettleOnStart) Settle(m_SettleTime);
            m_AnchorPrevious = transform.position;
            if (m_EndAttachment != null) m_EndPrevious = m_EndAttachment.position;
            CaptureRest();
            BuildMesh();
        }

        /// <summary>Pre-simulates the rope for a while (no players involved), for example after moving it in code.</summary>
        public void Settle(float seconds)
        {
            int frames = Mathf.CeilToInt(seconds * 60f);
            for (int i = 0; i < frames; i++)
                Simulate(1f / 60f, false);
            m_MeshDirty = true;
        }

        /// <summary>Starts simulating again if the rope was resting. Call it after moving things into a sleeping rope.</summary>
        public void WakeUp()
        {
            m_Asleep = false;
            m_StillTime = 0f;
        }

        /// <summary>
        /// Holds the rope at <paramref name="distance"/> meters from the anchor. Every frame the rope pulls that point
        /// to <see cref="RopeGrip.Target"/>, starting from wherever it is now, so it doesn't snap.
        /// </summary>
        public RopeGrip Grab(float distance, Vector3 target, object owner = null)
        {
            EnsureParticles();
            distance = Mathf.Clamp(distance, 0f, m_Length);
            var grip = new RopeGrip(this, distance, GetPoint(distance), target, owner);
            m_Grips.Add(grip);
            WakeUp();
            Grabbed?.Invoke(grip);
            return grip;
        }

        public void Release(RopeGrip grip)
        {
            if (grip == null || !m_Grips.Remove(grip)) return;
            grip.IsHeld = false;
            WakeUp();
            Released?.Invoke(grip);
        }

        /// <summary>Position on the rope <paramref name="distance"/> meters from the anchor.</summary>
        public Vector3 GetPoint(float distance)
        {
            if (m_Count < 2) return transform.position;
            Locate(distance, out int i, out float t);
            return Vector3.LerpUnclamped(m_Positions[i], m_Positions[i + 1], t);
        }

        /// <summary>Velocity of the rope <paramref name="distance"/> meters from the anchor.</summary>
        public Vector3 GetVelocity(float distance)
        {
            if (m_Count < 2) return Vector3.zero;
            Locate(distance, out int i, out float t);
            return Vector3.LerpUnclamped(m_Velocities[i], m_Velocities[i + 1], t);
        }

        /// <summary>Direction the rope runs (away from the anchor) at <paramref name="distance"/>.</summary>
        public Vector3 GetDirection(float distance)
        {
            if (m_Count < 2) return Vector3.down;
            Locate(distance, out int i, out _);
            var d = m_Positions[i + 1] - m_Positions[i];
            return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.down;
        }

        /// <summary>Finds the point on the rope's center line closest to <paramref name="point"/>.</summary>
        public bool FindClosestPoint(Vector3 point, out float distance, out Vector3 closest, out float sqrDistance)
        {
            distance = 0f;
            closest = transform.position;
            sqrDistance = float.MaxValue;
            if (m_Count < 2) return false;

            for (int i = 0; i < m_Count - 1; i++)
            {
                Vector3 a = m_Positions[i];
                Vector3 ab = m_Positions[i + 1] - a;
                float lengthSq = ab.sqrMagnitude;
                float t = lengthSq > 1e-10f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq) : 0f;
                Vector3 c = a + ab * t;
                float sq = (point - c).sqrMagnitude;
                if (sq < sqrDistance)
                {
                    sqrDistance = sq;
                    closest = c;
                    distance = (i + t) * m_SegmentRest;
                }
            }
            return true;
        }

        /// <summary>Adds velocity to the rope around <paramref name="point"/>, fading out over <paramref name="radius"/>.</summary>
        public void AddImpulse(Vector3 point, Vector3 velocityChange, float radius)
        {
            if (m_Count < 2 || radius <= 0f) return;
            for (int i = 1; i < m_Count; i++)
            {
                if (m_InverseMass[i] == 0f) continue;
                float falloff = 1f - (m_Positions[i] - point).magnitude / radius;
                if (falloff > 0f) m_Velocities[i] += velocityChange * falloff;
            }
            WakeUp();
        }

        void Locate(float distance, out int index, out float t)
        {
            float x = Mathf.Clamp(distance, 0f, m_Length) / m_SegmentRest;
            index = Mathf.Clamp((int)x, 0, m_Count - 2);
            t = Mathf.Clamp01(x - index);
        }

        void Tick(float deltaTime)
        {
            EnsureParticles();
            if (m_Count < 2 || deltaTime <= 0f) return;
            deltaTime = Mathf.Min(deltaTime, k_MaxFrameTime);

            if (m_Returning)
            {
                StepReturn(deltaTime);
                return;
            }

            if (!m_Asleep || ShouldWake())
            {
                Simulate(deltaTime, true);
                m_MeshDirty = true;
            }
            CheckStuck(deltaTime);
        }

        /// <summary>Per-rope color multiplied with the material color.</summary>
        public Color Tint
        {
            get => m_Tint;
            set
            {
                m_Tint = value;
                ApplyTint();
            }
        }

        void ApplyTint()
        {
            if (m_Renderer == null) return;
            if (m_Tint == Color.white)
            {
                if (m_Renderer.HasPropertyBlock()) m_Renderer.SetPropertyBlock(null);
                return;
            }

            var material = m_Renderer.sharedMaterial;
            Color baseColor = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            m_PropertyBlock ??= new MaterialPropertyBlock();
            m_PropertyBlock.SetColor("_BaseColor", baseColor * m_Tint);
            m_Renderer.SetPropertyBlock(m_PropertyBlock);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, m_Radius * 1.6f);
            if (m_EndAttachment != null) Gizmos.DrawWireSphere(m_EndAttachment.position, m_Radius * 1.6f);
        }

#if UNITY_EDITOR
        /// <summary>Set by the editor scripts so new ropes get the default braided material.</summary>
        public static Func<Material> EditorDefaultMaterial;

        bool m_EditorDirty;
        bool m_MaterialChecked;
        double m_NextEditorTick;

        void Reset()
        {
            m_MaterialChecked = false;
        }

        /// <summary>
        /// True if the renderer has no material, or only Unity's built-in default one (which isn't a project asset).
        /// Your own materials from the Assets folder are never replaced.
        /// </summary>
        public bool NeedsRopeMaterial()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null) return false;
            var material = renderer.sharedMaterial;
            return material == null || !AssetDatabase.GetAssetPath(material).StartsWith("Assets/");
        }

        /// <summary>Puts a GorillaRopeClimber on the scene's Gorilla Locomotion player if nothing can grab ropes yet.</summary>
        void EnsureClimberInScene()
        {
            if (!gameObject.scene.IsValid() || UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null) return;
            if (FindFirstObjectByType<GorillaRopeClimber>(FindObjectsInactive.Include) != null) return;
            var player = FindFirstObjectByType<GorillaLocomotion.Player>(FindObjectsInactive.Include);
            if (player == null) return;
            Undo.AddComponent<GorillaRopeClimber>(player.gameObject);
            Debug.Log($"Kapi Rope: added a Gorilla Rope Climber to \"{player.name}\" so you can grab ropes with grip. Save the scene to keep it.", player);
        }

        void EnsureRopeMaterial()
        {
            m_MaterialChecked = true;
            if (EditorDefaultMaterial == null || !NeedsRopeMaterial()) return;
            var material = EditorDefaultMaterial();
            if (material == null) return;
            var renderer = GetComponent<MeshRenderer>();
            Undo.RecordObject(renderer, "Assign Rope Material");
            renderer.sharedMaterial = material;
            ApplyTint();
        }

        void OnValidate()
        {
            m_EditorDirty = true;
            m_Asleep = false;
        }

        void EditorUpdate()
        {
            if (Application.isPlaying || this == null) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < m_NextEditorTick) return;
            m_NextEditorTick = now + 1.0 / 60.0;

            if (!m_MaterialChecked)
            {
                EnsureRopeMaterial();
                EnsureClimberInScene();
            }

            if (m_Count < 2 || SettingsChanged())
            {
                m_EditorDirty = false;
                ResetShape();
                ApplyTint();
                SceneView.RepaintAll();
                return;
            }

            if (m_EditorDirty)
            {
                m_EditorDirty = false;
                ApplyTint();
                WakeUp();
                BuildMesh();
                SceneView.RepaintAll();
            }

            if (!m_PreviewInEditor) return;

            bool wasAsleep = m_Asleep;
            Tick(1f / 60f);
            if (m_MeshDirty) BuildMesh();
            if (!wasAsleep || !m_Asleep) SceneView.RepaintAll();
        }
#endif
    }
}
