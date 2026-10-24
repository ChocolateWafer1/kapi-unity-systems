using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace KapiVR
{
    /// <summary>
    /// Fades a VR camera to a color. Screen-space overlays don't show up in a headset, so this draws a quad just
    /// past the camera's near plane that renders on top of everything, in both eyes.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class VRScreenFader : MonoBehaviour
    {
        const string k_ShaderName = "Hidden/KapiVR/ScreenFade";
        static readonly int s_ColorId = Shader.PropertyToID("_Color");
        static Mesh s_QuadMesh;

        Camera m_Camera;
        Transform m_Quad;
        MeshRenderer m_Renderer;
        Material m_Material;
        Color m_Color = Color.black;
        float m_Alpha;

        public float alpha
        {
            get => m_Alpha;
            set
            {
                m_Alpha = Mathf.Clamp01(value);
                Apply();
            }
        }

        public Color color
        {
            get => m_Color;
            set
            {
                m_Color = value;
                Apply();
            }
        }

        /// <summary>Adds a fader to the camera (or returns the one already there).</summary>
        public static VRScreenFader Attach(Camera camera, Material material = null)
        {
            if (camera == null) return null;

            var fader = camera.GetComponent<VRScreenFader>();
            if (fader == null) fader = camera.gameObject.AddComponent<VRScreenFader>();
            fader.Build(camera, material);
            return fader;
        }

        /// <summary>Animates the fade with a smooth ease, using unscaled time so it works while paused.</summary>
        public IEnumerator FadeTo(float target, float duration)
        {
            float start = m_Alpha;
            if (duration <= 0f)
            {
                alpha = target;
                yield break;
            }

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
            {
                alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            alpha = target;
        }

        void Build(Camera camera, Material material)
        {
            m_Camera = camera;
            if (m_Quad != null) return;

            if (material != null)
            {
                m_Material = new Material(material);
            }
            else
            {
                var shader = Shader.Find(k_ShaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[Kapi VR] Can't fade: shader '{k_ShaderName}' wasn't found or included in the build.", this);
                    return;
                }
                m_Material = new Material(shader);
            }
            m_Material.name = "Screen Fade (Instance)";

            var quad = new GameObject("Screen Fade");
            quad.layer = PickVisibleLayer(camera);
            m_Quad = quad.transform;
            m_Quad.SetParent(camera.transform, false);

            quad.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            m_Renderer = quad.AddComponent<MeshRenderer>();
            m_Renderer.sharedMaterial = m_Material;
            m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.lightProbeUsage = LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_Renderer.allowOcclusionWhenDynamic = false;

            Apply();
        }

        void LateUpdate()
        {
            if (m_Quad == null || m_Camera == null) return;

            // Just past the near plane, and wide enough to cover even very wide headset fields of view.
            float distance = m_Camera.nearClipPlane * 1.5f + 0.01f;
            m_Quad.localPosition = new Vector3(0f, 0f, distance);
            m_Quad.localRotation = Quaternion.identity;
            m_Quad.localScale = Vector3.one * (distance * 8f);
        }

        void Apply()
        {
            if (m_Renderer == null) return;

            var c = m_Color;
            c.a *= m_Alpha;
            m_Material.SetColor(s_ColorId, c);
            m_Renderer.enabled = c.a > 0.001f;
            LateUpdate();
        }

        void OnDestroy()
        {
            if (m_Quad != null) Destroy(m_Quad.gameObject);
            if (m_Material != null) Destroy(m_Material);
        }

        static int PickVisibleLayer(Camera camera)
        {
            int layer = camera.gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) != 0) return layer;

            for (int i = 0; i < 32; i++)
                if ((camera.cullingMask & (1 << i)) != 0) return i;
            return layer;
        }

        static Mesh QuadMesh()
        {
            if (s_QuadMesh != null) return s_QuadMesh;

            s_QuadMesh = new Mesh
            {
                name = "Screen Fade Quad",
                hideFlags = HideFlags.DontSave,
                vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(1f, -1f, 0f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            // Generous bounds so the quad is never culled, even in odd stereo setups.
            s_QuadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return s_QuadMesh;
        }
    }
}
