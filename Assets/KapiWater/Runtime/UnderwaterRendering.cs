using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KapiWater
{
    /// <summary>
    /// Draws the underwater look (water fog, wobble, caustics, darker with depth, waterline) for any camera whose lens
    /// is under or right at a Water Volume's waves, on a quad just in front of the camera. Being an ordinary object it
    /// shows in both eyes in every VR mode. Every camera gets it automatically, the Scene view too; an Underwater View
    /// on a camera adds the sound and specks.
    /// </summary>
    static class UnderwaterRendering
    {
        const string k_ShaderResource = "KapiWater_Underwater";
        const string k_CausticsKeyword = "_KAPI_CAUSTICS";

        static readonly int s_WorldToBox = Shader.PropertyToID("_WaterWorldToBox");
        static readonly int s_Extents = Shader.PropertyToID("_WaterExtents");
        static readonly int s_RestHeight = Shader.PropertyToID("_WaterRestHeight");
        static readonly int s_LensSurface = Shader.PropertyToID("_LensSurface");
        static readonly int s_LensCenter = Shader.PropertyToID("_LensCenter");
        static readonly int s_ColorAbsorption = Shader.PropertyToID("_ColorAbsorption");
        static readonly int s_DarkenWithDepth = Shader.PropertyToID("_DarkenWithDepth");
        static readonly int s_Distortion = Shader.PropertyToID("_Distortion");
        static readonly int s_DistortionSpeed = Shader.PropertyToID("_DistortionSpeed");
        static readonly int s_DistortionDetail = Shader.PropertyToID("_DistortionDetail");
        static readonly int s_Vignette = Shader.PropertyToID("_Vignette");
        static readonly int s_WaterlineThickness = Shader.PropertyToID("_WaterlineThickness");
        static readonly int s_WaterlineDarkness = Shader.PropertyToID("_WaterlineDarkness");
        static readonly int s_CausticsTexture = Shader.PropertyToID("_CausticsTexture");

        // Copied over from the water's material, so the underwater view and the surface agree.
        static readonly int[] s_SharedFloats =
        {
            Shader.PropertyToID("_LightInfluence"), Shader.PropertyToID("_CausticsStrength"),
            Shader.PropertyToID("_CausticsSpeed"), Shader.PropertyToID("_CausticsSplit"),
        };

        // Sizes and distances, multiplied by the water's Look Scale.
        static readonly int[] s_ScaledFloats =
        {
            Shader.PropertyToID("_UnderwaterVisibility"), Shader.PropertyToID("_CausticsSize"), Shader.PropertyToID("_CausticsDepth"),
        };

        static readonly int[] s_SharedColors = { Shader.PropertyToID("_UnderwaterColor"), Shader.PropertyToID("_CausticsColor") };

        sealed class Entry
        {
            public Material material;
        }

        // One material per camera, since each camera sits at a different spot in the water.
        static readonly Dictionary<Camera, Entry> s_Entries = new Dictionary<Camera, Entry>();
        static readonly List<Camera> s_Dead = new List<Camera>();
        static Shader s_Shader;
        static Mesh s_Quad;
        static bool s_Warned;

        /// <summary>How close above the surface a camera can be and still need the waterline drawn.</summary>
        public static float LensReach(Camera camera) => camera.nearClipPlane * 2f + 0.05f;

        /// <summary>Draws the underwater look for this camera's next frame if its lens is in or right at the water.</summary>
        public static bool Draw(Camera camera)
        {
            if (camera == null) return false;
            Transform view = camera.transform;
            Vector3 head = view.position;
            float reach = LensReach(camera);
            var water = WaterVolume.Find(head, reach);
            if (water == null) return false;
            float surface = water.SurfaceHeight(head, out var normal);
            if (surface - head.y <= -reach) return false;

            var entry = GetEntry(camera);
            if (entry == null) return false;
            Apply(entry.material, water, head, surface, normal);

            // Just past the near plane, and wide enough to cover even very wide headset views in both eyes.
            float distance = camera.nearClipPlane * 1.5f + 0.01f;
            var matrix = Matrix4x4.TRS(head + view.forward * distance, view.rotation, Vector3.one * (distance * 8f));
            var parameters = new RenderParams(entry.material)
            {
                camera = camera,
                layer = VisibleLayer(camera),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                worldBounds = new Bounds(head, Vector3.one * (distance * 20f + 1f)),
            };
            Graphics.RenderMesh(parameters, Quad, 0, matrix);
            return true;
        }

        static Mesh Quad
        {
            get
            {
                if (s_Quad != null) return s_Quad;
                s_Quad = new Mesh { name = "Underwater View Quad", hideFlags = HideFlags.HideAndDontSave };
                s_Quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) });
                s_Quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                s_Quad.bounds = new Bounds(Vector3.zero, Vector3.one);
                return s_Quad;
            }
        }

        static int VisibleLayer(Camera camera)
        {
            int layer = camera.gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) != 0) return layer;
            for (int i = 0; i < 32; i++)
                if ((camera.cullingMask & (1 << i)) != 0) return i;
            return layer;
        }

        /// <summary>Frees the material kept for a camera that's going away.</summary>
        public static void Forget(Camera camera)
        {
            if (camera == null || !s_Entries.TryGetValue(camera, out var entry)) return;
            DestroyMaterial(entry.material);
            s_Entries.Remove(camera);
        }

        static Entry GetEntry(Camera camera)
        {
            if (s_Entries.TryGetValue(camera, out var entry)) return entry;

            if (s_Shader == null) s_Shader = Resources.Load<Shader>(k_ShaderResource);
            if (s_Shader == null)
            {
                if (!s_Warned) Debug.LogWarning($"[Kapi Water] Can't show the underwater view: '{k_ShaderResource}' wasn't found in a Resources folder.");
                s_Warned = true;
                return null;
            }

            // Drop materials of cameras that were destroyed.
            s_Dead.Clear();
            foreach (var pair in s_Entries)
                if (pair.Key == null) s_Dead.Add(pair.Key);
            foreach (var dead in s_Dead)
            {
                DestroyMaterial(s_Entries[dead].material);
                s_Entries.Remove(dead);
            }

            var material = new Material(s_Shader) { name = "Underwater View (Instance)", hideFlags = HideFlags.HideAndDontSave };
            entry = new Entry { material = material };
            s_Entries[camera] = entry;
            return entry;
        }

        static void DestroyMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Object.Destroy(material);
            else Object.DestroyImmediate(material);
        }

        static void Apply(Material m, WaterVolume water, Vector3 head, float surface, Vector3 normal)
        {
            m.SetMatrix(s_WorldToBox, water.WorldToBox);
            m.SetVector(s_Extents, water.BoxExtents);
            m.SetFloat(s_RestHeight, water.RestHeight);

            // Over a lens a couple of centimeters wide the waves are a tilted plane.
            float ny = Mathf.Max(normal.y, 0.05f);
            m.SetVector(s_LensSurface, new Vector4(surface, -normal.x / ny, -normal.z / ny, 0f));
            m.SetVector(s_LensCenter, new Vector4(head.x, head.z, 0f, 0f));

            float look = water.LookScale;
            m.SetFloat(s_ColorAbsorption, water.ColorAbsorption);
            m.SetFloat(s_DarkenWithDepth, water.DarkenWithDepth * look);
            m.SetFloat(s_Distortion, water.Distortion);
            m.SetFloat(s_DistortionSpeed, water.DistortionSpeed);
            m.SetFloat(s_DistortionDetail, water.DistortionDetail);
            m.SetFloat(s_Vignette, water.Vignette);
            m.SetFloat(s_WaterlineThickness, water.WaterlineThickness);
            m.SetFloat(s_WaterlineDarkness, water.WaterlineDarkness);

            var source = water.SurfaceMaterial;
            bool caustics = false;
            if (source != null)
            {
                foreach (int id in s_SharedFloats)
                    if (source.HasProperty(id)) m.SetFloat(id, source.GetFloat(id));
                foreach (int id in s_ScaledFloats)
                    if (source.HasProperty(id)) m.SetFloat(id, source.GetFloat(id) * look);
                foreach (int id in s_SharedColors)
                    if (source.HasProperty(id)) m.SetColor(id, source.GetColor(id));
                if (source.HasProperty(s_CausticsTexture))
                {
                    var texture = source.GetTexture(s_CausticsTexture);
                    m.SetTexture(s_CausticsTexture, texture);
                    caustics = texture != null && source.IsKeywordEnabled(k_CausticsKeyword);
                }
            }
            if (caustics && water.UnderwaterCaustics) m.EnableKeyword(k_CausticsKeyword);
            else m.DisableKeyword(k_CausticsKeyword);
        }
    }
}
