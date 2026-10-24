using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace KapiVR
{
    /// <summary>
    /// Distorts everything a camera sees, in both eyes. The full-screen pass only runs while <see cref="strength"/>
    /// is above zero, so it costs nothing once the effect has cleared, and it doesn't change any renderer settings.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class VRScreenDistortion : MonoBehaviour
    {
        const string k_ShaderResource = "KapiVR_ScreenDistortion";
        static readonly int s_StrengthId = Shader.PropertyToID("_Strength");
        static readonly int s_TimeId = Shader.PropertyToID("_DistortionTime");

        Camera m_Camera;
        Material m_Material;
        DistortionPass m_Pass;
        float m_Strength;
        float m_StartTime;

        /// <summary>0 = clean view, 1 = fully distorted.</summary>
        public float strength
        {
            get => m_Strength;
            set => m_Strength = Mathf.Clamp01(value);
        }

        /// <summary>Adds a distortion effect to the camera (or returns the one already there).</summary>
        public static VRScreenDistortion Attach(Camera camera)
        {
            if (camera == null) return null;

            var distortion = camera.GetComponent<VRScreenDistortion>();
            if (distortion == null) distortion = camera.gameObject.AddComponent<VRScreenDistortion>();
            distortion.m_Camera = camera;
            return distortion;
        }

        void Awake()
        {
            m_StartTime = Time.unscaledTime;

            var shader = Resources.Load<Shader>(k_ShaderResource);
            if (shader == null)
            {
                Debug.LogWarning($"[Kapi VR] Can't distort the view: '{k_ShaderResource}' wasn't found in a Resources folder.", this);
                return;
            }

            m_Material = new Material(shader) { name = "Screen Distortion (Instance)", hideFlags = HideFlags.HideAndDontSave };
            m_Pass = new DistortionPass(m_Material);
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        void OnDestroy()
        {
            if (m_Material != null) Destroy(m_Material);
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera || m_Pass == null || m_Strength <= 0f) return;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData == null || cameraData.scriptableRenderer == null) return;

            m_Material.SetFloat(s_StrengthId, m_Strength);
            m_Material.SetFloat(s_TimeId, Time.unscaledTime - m_StartTime);
            cameraData.scriptableRenderer.EnqueuePass(m_Pass);
        }

        // Copies the finished frame (after post-processing, so UI and everything else is included) through the
        // distortion shader and makes the result the camera's image.
        sealed class DistortionPass : ScriptableRenderPass
        {
            const string k_PassName = "Kapi VR Screen Distortion";
            readonly Material m_Material;

            public DistortionPass(Material material)
            {
                m_Material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;

                var source = resources.activeColorTexture;
                var description = renderGraph.GetTextureDesc(source);
                description.name = k_PassName;
                description.clearBuffer = false;
                var destination = renderGraph.CreateTexture(description);

                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, m_Material, 0), k_PassName);
                resources.cameraColor = destination;
            }
        }
    }
}
