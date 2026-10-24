using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KapiWater
{
    /// <summary>
    /// A box of swimmable water. This object sits in the middle of the water's surface and the box reaches down by
    /// Size Y. It draws the wavy surface with its Kapi/Water material, tells swimmers where the waves are, and holds
    /// how the water looks and sounds from inside. Drag a Water prefab into a scene, size it with the handles, press Play:
    /// it adds a Gorilla Swimmer to your Gorilla Locomotion player if there isn't one yet.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Kapi Water/Water Volume")]
    public sealed class WaterVolume : MonoBehaviour
    {
        const int k_MaxCells = 254; // Keeps the surface grid under 65k vertices.

        static readonly List<WaterVolume> s_Active = new List<WaterVolume>();

        [Header("Size")]
        [Tooltip("Width (X), depth (Y, down from the surface) and length (Z) of the water in meters. Drag the handles in the Scene view to resize. This object sits in the middle of the surface.")]
        [SerializeField] Vector3 m_Size = new Vector3(10f, 3f, 10f);

        [Header("Surface")]
        [Tooltip("Spacing between the surface's grid points in meters. Smaller = smoother waves but more vertices (capped at 254 x 254 cells).")]
        [SerializeField, Range(0.05f, 2f)] float m_GridSpacing = 0.25f;

        [Tooltip("Within this many meters of the edges the waves only bob up and down instead of swaying sideways, so the water stays against your walls.")]
        [SerializeField, Min(0f)] float m_EdgePinning = 0.75f;

        [Tooltip("Draw the four sides of the box so the water looks like a solid body of water from outside, with its top edge riding the waves. Sides hidden inside your pool walls or the ground cost next to nothing, and you never see them from inside the water.")]
        [SerializeField] bool m_ShowSides = true;

        [Tooltip("Also draw the bottom of the box, for water floating in the air.")]
        [SerializeField] bool m_ShowBottom;

        [Header("Look Scale")]
        [Tooltip("The material's sizes are in meters (1 unit = 1 m). On = scaling this object up also scales up its ripples, waves, caustics, foam and how far you can see through it, so water in a map built bigger than real life still looks right.")]
        [SerializeField] bool m_ScaleLookWithObject = true;

        [Tooltip("Multiplies the size of the ripples, waves, caustics and foam and how far you can see through the water (on top of the object's scale when that's on). Raise it if the water looks too tiled or too deep blue for your map's size.")]
        [SerializeField, Range(0.1f, 20f)] float m_LookScale = 1f;

        [Header("Underwater View")]
        [Tooltip("How much your view wobbles while underwater.")]
        [SerializeField, Range(0f, 3f)] float m_Distortion = 1f;

        [Tooltip("How fast the wobble moves.")]
        [SerializeField, Range(0f, 4f)] float m_DistortionSpeed = 1f;

        [Tooltip("Higher = smaller, busier wobble waves.")]
        [SerializeField, Range(0.2f, 4f)] float m_DistortionDetail = 1f;

        [Tooltip("Darkens the edges of your view underwater.")]
        [SerializeField, Range(0f, 1f)] float m_Vignette = 0.35f;

        [Tooltip("How quickly colors fade with distance underwater. Reds go first, then greens, like real water.")]
        [SerializeField, Range(0f, 2f)] float m_ColorAbsorption = 1f;

        [Tooltip("Every this many meters deeper, the light underwater halves. 0 = it never gets darker.")]
        [SerializeField, Min(0f)] float m_DarkenWithDepth = 10f;

        [Tooltip("Thickness in pixels of the dark line where the water crosses your eyes when your head is half in.")]
        [SerializeField, Range(0f, 12f)] float m_WaterlineThickness = 3f;

        [SerializeField, Range(0f, 1f)] float m_WaterlineDarkness = 0.4f;

        [Tooltip("Caustic light dancing over the floor and walls while you're underwater. Uses the material's Caustics settings.")]
        [SerializeField] bool m_UnderwaterCaustics = true;

        [Header("Underwater Specks")]
        [Tooltip("How many tiny pixel specks appear around you per second while underwater. 0 = none.")]
        [SerializeField, Range(0f, 400f)] float m_Specks = 40f;

        [SerializeField] Color m_SpeckColor = new Color(0.85f, 0.95f, 0.9f, 0.9f);

        [Tooltip("Smallest and biggest speck size in meters.")]
        [SerializeField] Vector2 m_SpeckSize = new Vector2(0.008f, 0.02f);

        [Tooltip("How far from your head specks show up, in meters.")]
        [SerializeField, Range(0.5f, 10f)] float m_SpeckRange = 3f;

        [Tooltip("How fast specks drift around, in meters per second.")]
        [SerializeField, Range(0f, 1f)] float m_SpeckDrift = 0.05f;

        [Header("Underwater Sound")]
        [Tooltip("The rest of the game sounds muffled while your head is underwater. Lower = more muffled. 22000 = not muffled.")]
        [SerializeField, Range(200f, 22000f)] float m_MuffleCutoff = 750f;

        [Tooltip("Looping sound you hear underwater. Empty = a generated deep water hum.")]
        [SerializeField] AudioClip m_Ambience;

        [SerializeField, Range(0f, 1f)] float m_AmbienceVolume = 0.18f;

        [Header("Rendering")]
        [Tooltip("The water needs the camera depth and opaque textures for refraction, shore foam and caustics. On = this water turns them on for every camera that renders, even if your URP asset has them off.")]
        [SerializeField] bool m_TurnOnCameraTextures = true;

        MeshFilter m_Filter;
        MeshRenderer m_Renderer;
        Mesh m_Mesh;
        Vector3 m_BuiltSize;
        Vector3 m_BuiltScale;
        float m_BuiltSpacing = -1f;
        float m_BuiltPinning = -1f;
        float m_BuiltAmplitude = -1f;
        bool m_BuiltSides;
        bool m_BuiltBottom;
        float m_BuiltLook = -1f;
        WaterWaves m_Waves;
        int m_WavesFrame = -1;

        /// <summary>Every enabled water in the loaded scenes.</summary>
        public static IReadOnlyList<WaterVolume> Active => s_Active;

        /// <summary>Width, depth and length of the water in this object's local units (scale multiplies them).</summary>
        public Vector3 Size
        {
            get => m_Size;
            set
            {
                m_Size = Vector3.Max(value, new Vector3(0.1f, 0.05f, 0.1f));
                RebuildMeshIfNeeded(true);
            }
        }

        /// <summary>The Kapi/Water material drawing the surface. The wave, fog and caustics settings come from here.</summary>
        public Material SurfaceMaterial
        {
            get => Renderer != null ? Renderer.sharedMaterial : null;
            set { if (Renderer != null) Renderer.sharedMaterial = value; }
        }

        /// <summary>The surface's height when the water is calm.</summary>
        public float RestHeight => transform.position.y;

        /// <summary>World height of the bottom of the water box.</summary>
        public float Bottom => RestHeight - m_Size.y * AbsScale.y;

        /// <summary>The material's waves at this water's Look Scale, read once per frame.</summary>
        public WaterWaves Waves
        {
            get
            {
                if (m_WavesFrame != Time.frameCount)
                {
                    m_WavesFrame = Time.frameCount;
                    m_Waves = WaterWaves.FromMaterial(SurfaceMaterial).Scaled(LookScale);
                }
                return m_Waves;
            }
        }

        /// <summary>How much bigger than the material's meter-based sizes this water's details are.</summary>
        public float LookScale
        {
            get
            {
                float look = m_LookScale;
                if (m_ScaleLookWithObject)
                {
                    Vector3 scale = AbsScale;
                    look *= (scale.x + scale.z) * 0.5f;
                }
                return Mathf.Max(look, 0.01f);
            }
        }

        public float Distortion => m_Distortion;
        public float DistortionSpeed => m_DistortionSpeed;
        public float DistortionDetail => m_DistortionDetail;
        public float Vignette => m_Vignette;
        public float ColorAbsorption => m_ColorAbsorption;
        public float DarkenWithDepth => m_DarkenWithDepth;
        public float WaterlineThickness => m_WaterlineThickness;
        public float WaterlineDarkness => m_WaterlineDarkness;
        public bool UnderwaterCaustics => m_UnderwaterCaustics;
        public float Specks => m_Specks;
        public Color SpeckColor => m_SpeckColor;
        public Vector2 SpeckSize => m_SpeckSize;
        public float SpeckRange => m_SpeckRange;
        public float SpeckDrift => m_SpeckDrift;
        public float MuffleCutoff => m_MuffleCutoff;
        public AudioClip Ambience => m_Ambience;
        public float AmbienceVolume => m_AmbienceVolume;

        /// <summary>Turns a world point into the water's level, unscaled box space (rest surface at y = 0).</summary>
        public Matrix4x4 WorldToBox => Matrix4x4.TRS(transform.position, LevelRotation, AbsScale).inverse;

        /// <summary>Half the width, the full depth and half the length, in box space.</summary>
        public Vector3 BoxExtents => new Vector3(m_Size.x * 0.5f, m_Size.y, m_Size.z * 0.5f);

        MeshRenderer Renderer
        {
            get
            {
                if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
                return m_Renderer;
            }
        }

        Quaternion LevelRotation => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        Vector3 AbsScale
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return new Vector3(Mathf.Max(Mathf.Abs(s.x), 1e-4f), Mathf.Max(Mathf.Abs(s.y), 1e-4f), Mathf.Max(Mathf.Abs(s.z), 1e-4f));
            }
        }

        void OnEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            m_Filter = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();
            RebuildMeshIfNeeded(true);
            CameraHook.Refresh();
        }

        void OnDisable()
        {
            s_Active.Remove(this);
            CameraHook.Refresh();
        }

        void OnDestroy()
        {
            if (m_Mesh == null) return;
            if (Application.isPlaying) Destroy(m_Mesh);
            else DestroyImmediate(m_Mesh);
        }

        void Start()
        {
            if (Application.isPlaying) GorillaSwimmer.EnsureOnPlayer();
        }

        void OnValidate()
        {
            m_Size = Vector3.Max(m_Size, new Vector3(0.1f, 0.05f, 0.1f));
            m_SpeckSize = new Vector2(Mathf.Max(0.001f, m_SpeckSize.x), Mathf.Max(m_SpeckSize.x, m_SpeckSize.y));
        }

        void Update()
        {
            RebuildMeshIfNeeded(false);
        }

        /// <summary>True if the point is above or below the water's footprint (ignores height).</summary>
        public bool IsOverWater(Vector3 point)
        {
            Vector3 local = WorldToBox.MultiplyPoint3x4(point);
            return Mathf.Abs(local.x) <= m_Size.x * 0.5f && Mathf.Abs(local.z) <= m_Size.z * 0.5f;
        }

        /// <summary>True if the point is in the water, under the waves. above lets points up to that far above the surface count.</summary>
        public bool Contains(Vector3 point, float above = 0f)
        {
            if (!IsOverWater(point) || point.y < Bottom) return false;
            if (point.y > RestHeight + Waves.Amplitude + above) return false;
            return point.y <= SurfaceHeight(point) + above;
        }

        /// <summary>World height of the wavy surface above or below this point.</summary>
        public float SurfaceHeight(Vector3 point) => SurfaceHeight(point, out _);

        public float SurfaceHeight(Vector3 point, out Vector3 normal)
        {
            var waves = Waves;
            return RestHeight + waves.Height(new Vector2(point.x, point.z), waves.Clock, PinAt(point), out normal);
        }

        /// <summary>The water whose box holds this point (or whose surface is at most above meters under it), else null.</summary>
        public static WaterVolume Find(Vector3 point, float above = 0f)
        {
            WaterVolume best = null;
            float bestSurface = float.NegativeInfinity;
            for (int i = 0; i < s_Active.Count; i++)
            {
                var water = s_Active[i];
                if (!water.IsOverWater(point) || point.y < water.Bottom) continue;
                if (point.y > water.RestHeight + water.Waves.Amplitude + above) continue;
                float surface = water.SurfaceHeight(point);
                if (point.y > surface + above || surface <= bestSurface) continue;
                best = water;
                bestSurface = surface;
            }
            return best;
        }

        /// <summary>How freely the waves may sway sideways here: 0 at the edges, 1 past the Edge Pinning distance.</summary>
        public float PinAt(Vector3 point)
        {
            Vector3 local = WorldToBox.MultiplyPoint3x4(point);
            return Pin(local.x, local.z, AbsScale);
        }

        float Pin(float localX, float localZ, Vector3 scale)
        {
            if (m_EdgePinning <= 0f) return 1f;
            float toEdge = Mathf.Min((m_Size.x * 0.5f - Mathf.Abs(localX)) * scale.x, (m_Size.z * 0.5f - Mathf.Abs(localZ)) * scale.z);
            float t = Mathf.Clamp01(toEdge / m_EdgePinning);
            return t * t * (3f - 2f * t);
        }

        void RebuildMeshIfNeeded(bool force)
        {
            if (m_Filter == null) return;
            Vector3 scale = AbsScale;
            float amplitude = Waves.Amplitude;
            bool shape = force || m_Mesh == null || m_BuiltSize != m_Size || m_BuiltScale != scale
                || !Mathf.Approximately(m_BuiltSpacing, m_GridSpacing) || !Mathf.Approximately(m_BuiltPinning, m_EdgePinning)
                || m_BuiltSides != m_ShowSides || m_BuiltBottom != m_ShowBottom || !Mathf.Approximately(m_BuiltLook, LookScale);

            if (shape) BuildMesh(scale);
            if (shape || !Mathf.Approximately(m_BuiltAmplitude, amplitude))
            {
                // Waves move the surface on the GPU, so the bounds have to leave room for them.
                m_BuiltAmplitude = amplitude;
                var extra = new Vector3(amplitude / scale.x, amplitude / scale.y, amplitude / scale.z) * 2f;
                var bounds = new Bounds(Vector3.zero, new Vector3(m_Size.x, 0.1f, m_Size.z) + extra);
                if (m_ShowSides || m_ShowBottom) bounds.Encapsulate(new Vector3(0f, -m_Size.y, 0f));
                m_Mesh.bounds = bounds;
            }
            if (m_Filter.sharedMesh != m_Mesh) m_Filter.sharedMesh = m_Mesh;
        }

        // Vertex colors tell the shader what each vertex is: r = how far waves may sway it sideways, g = 1 if it rides
        // the waves up and down, b = 1 for the top surface (0 for sides and bottom). UV1 holds the box size so the
        // shader knows where the water ends, plus whether the box bottom acts as a floor.
        void BuildMesh(Vector3 scale)
        {
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Water Surface", hideFlags = HideFlags.HideAndDontSave };

            float spacing = Mathf.Max(m_GridSpacing, 0.01f);
            int cellsX = Mathf.Clamp(Mathf.CeilToInt(m_Size.x * scale.x / spacing), 1, k_MaxCells);
            int cellsZ = Mathf.Clamp(Mathf.CeilToInt(m_Size.z * scale.z / spacing), 1, k_MaxCells);
            int columns = cellsX + 1;
            var mesh = new MeshParts();

            for (int z = 0; z <= cellsZ; z++)
            {
                float localZ = (z / (float)cellsZ - 0.5f) * m_Size.z;
                for (int x = 0; x <= cellsX; x++)
                {
                    float localX = (x / (float)cellsX - 0.5f) * m_Size.x;
                    mesh.Add(new Vector3(localX, 0f, localZ), Vector3.up, Pin(localX, localZ, scale), true, true);
                }
            }
            for (int z = 0; z < cellsZ; z++)
            {
                for (int x = 0; x < cellsX; x++)
                {
                    int i = z * columns + x;
                    mesh.Quad(i, i + columns, i + columns + 1, i + 1); // Clockwise seen from above, so the top faces up.
                }
            }

            float hx = m_Size.x * 0.5f;
            float hz = m_Size.z * 0.5f;
            float depth = m_Size.y;
            if (m_ShowSides)
            {
                // Each wall's top edge has the same points as the surface's edge, so it rides the same waves.
                Wall(mesh, new Vector3(-hx, 0f, -hz), new Vector3(m_Size.x, 0f, 0f), cellsX, Vector3.back, depth);
                Wall(mesh, new Vector3(hx, 0f, hz), new Vector3(-m_Size.x, 0f, 0f), cellsX, Vector3.forward, depth);
                Wall(mesh, new Vector3(hx, 0f, -hz), new Vector3(0f, 0f, m_Size.z), cellsZ, Vector3.right, depth);
                Wall(mesh, new Vector3(-hx, 0f, hz), new Vector3(0f, 0f, -m_Size.z), cellsZ, Vector3.left, depth);
            }
            if (m_ShowBottom)
            {
                int first = mesh.Count;
                mesh.Add(new Vector3(hx, -depth, -hz), Vector3.down, 0f, false, false);
                mesh.Add(new Vector3(hx, -depth, hz), Vector3.down, 0f, false, false);
                mesh.Add(new Vector3(-hx, -depth, hz), Vector3.down, 0f, false, false);
                mesh.Add(new Vector3(-hx, -depth, -hz), Vector3.down, 0f, false, false);
                mesh.Quad(first, first + 1, first + 2, first + 3);
            }

            m_Mesh.Clear();
            m_Mesh.indexFormat = mesh.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m_Mesh.SetVertices(mesh.vertices);
            m_Mesh.SetNormals(mesh.normals);
            m_Mesh.SetColors(mesh.colors);
            // w = 1: with no bottom drawn, anything past the bottom of the box doesn't count as being under this water,
            // so a pool without a floor (or a camera without a depth texture) still gets a floor to look at.
            var box = new Vector4(hx, depth, hz, m_ShowBottom ? 0f : 1f);
            var boxes = new List<Vector4>(mesh.Count);
            for (int i = 0; i < mesh.Count; i++) boxes.Add(box);
            m_Mesh.SetUVs(1, boxes);
            float look = LookScale;
            var looks = new List<Vector2>(mesh.Count);
            for (int i = 0; i < mesh.Count; i++) looks.Add(new Vector2(look, 0f));
            m_Mesh.SetUVs(2, looks);
            m_Mesh.SetTriangles(mesh.triangles, 0);

            m_BuiltSize = m_Size;
            m_BuiltScale = scale;
            m_BuiltSpacing = m_GridSpacing;
            m_BuiltPinning = m_EdgePinning;
            m_BuiltSides = m_ShowSides;
            m_BuiltBottom = m_ShowBottom;
            m_BuiltLook = look;
        }

        // One side of the box. start is the wall's top left corner and along runs to its top right, both as seen from
        // outside, so the outside is the front face.
        static void Wall(MeshParts mesh, Vector3 start, Vector3 along, int cells, Vector3 normal, float depth)
        {
            int first = mesh.Count;
            for (int i = 0; i <= cells; i++)
            {
                Vector3 top = start + along * (i / (float)cells);
                mesh.Add(top + Vector3.down * depth, normal, 0f, false, false);
                mesh.Add(top, normal, 0f, true, false);
            }
            for (int i = 0; i < cells; i++)
            {
                int bottomLeft = first + i * 2;
                mesh.Quad(bottomLeft, bottomLeft + 1, bottomLeft + 3, bottomLeft + 2);
            }
        }

        sealed class MeshParts
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<int> triangles = new List<int>();

            public int Count => vertices.Count;

            public void Add(Vector3 position, Vector3 normal, float pin, bool ridesWaves, bool top)
            {
                vertices.Add(position);
                normals.Add(normal);
                colors.Add(new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(pin) * 255f), (byte)(ridesWaves ? 255 : 0), (byte)(top ? 255 : 0), 255));
            }

            /// <summary>Two triangles, corners given clockwise as seen from the front: bottom left, top left, top right, bottom right.</summary>
            public void Quad(int bottomLeft, int topLeft, int topRight, int bottomRight)
            {
                triangles.Add(bottomLeft);
                triangles.Add(topLeft);
                triangles.Add(topRight);
                triangles.Add(bottomLeft);
                triangles.Add(topRight);
                triangles.Add(bottomRight);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, LevelRotation, AbsScale);
            Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.35f);
            Gizmos.DrawWireCube(new Vector3(0f, -m_Size.y * 0.5f, 0f), m_Size);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, LevelRotation, AbsScale);
            Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.08f);
            Gizmos.DrawCube(new Vector3(0f, -m_Size.y * 0.5f, 0f), m_Size);
        }

        /// <summary>
        /// Runs for every camera (and every eye) while any water is around. Asks for the depth and opaque textures with
        /// an empty render pass that only declares what it needs (so nothing in your URP asset or cameras changes), and
        /// draws the underwater look for any camera that's under the water, the Scene view too.
        /// </summary>
        static class CameraHook
        {
            static bool s_Hooked;
            static RequirementsPass s_Pass;

            public static void Refresh()
            {
                bool hook = s_Active.Count > 0;
                if (hook == s_Hooked) return;
                s_Hooked = hook;
                if (hook) RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                else RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            }

            static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.VR) return;
                // The Scene view camera (and any camera without URP's camera settings) uses the default renderer.
                ScriptableRenderer renderer = null;
                if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) renderer = data.scriptableRenderer;
                if (renderer == null && UniversalRenderPipeline.asset != null) renderer = UniversalRenderPipeline.asset.scriptableRenderer;
                if (renderer == null) return;

                for (int i = 0; i < s_Active.Count; i++)
                {
                    if (!s_Active[i].m_TurnOnCameraTextures) continue;
                    s_Pass ??= new RequirementsPass();
                    renderer.EnqueuePass(s_Pass);
                    break;
                }

                if (!UnderwaterView.TurnsOffLook(camera)) UnderwaterRendering.Draw(camera);
            }

            sealed class RequirementsPass : ScriptableRenderPass
            {
                public RequirementsPass()
                {
                    renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                    ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
                }

                public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { }
            }
        }
    }
}
