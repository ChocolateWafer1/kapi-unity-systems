using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace KapiWindowGlass
{
    /// <summary>
    /// Finds every object using a Kapi/Mirror material and renders its reflection, in Play mode and in the editor
    /// (Scene and Game views). Nothing is added to the scene; a Mirror component is only needed to change settings.
    /// </summary>
    static class MirrorSystem
    {
        static readonly int s_TextureLeftId = Shader.PropertyToID("_KapiMirrorTexL");
        static readonly int s_TextureRightId = Shader.PropertyToID("_KapiMirrorTexR");
        static readonly int s_ViewProjectionLeftId = Shader.PropertyToID("_KapiMirrorVPL");
        static readonly int s_ViewProjectionRightId = Shader.PropertyToID("_KapiMirrorVPR");
        static readonly int s_EyeLeftId = Shader.PropertyToID("_KapiMirrorEyeL");
        static readonly int s_EyeRightId = Shader.PropertyToID("_KapiMirrorEyeR");
        static readonly int s_ActiveId = Shader.PropertyToID("_KapiMirrorActive");

        // Mirroring flips which way triangles wind. Flipping the image sideways flips it back, so faces still draw
        // the right way round without touching global culling state; the shader reads the image the same way.
        static readonly Matrix4x4 s_FlipX = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

        static readonly SkyboxPass s_SkyboxPass = new SkyboxPass();
        static readonly List<Target> s_Targets = new List<Target>();
        static readonly List<XRDisplaySubsystem> s_Displays = new List<XRDisplaySubsystem>();
        static readonly Plane[] s_FrustumPlanes = new Plane[6];
        static readonly Matrix4x4[] s_EyeViews = new Matrix4x4[2];
        static readonly Matrix4x4[] s_EyeProjections = new Matrix4x4[2];
        static Camera s_ReflectionCamera;
        static UniversalAdditionalCameraData s_ReflectionCameraData;
        static int s_RenderCount;
        static float s_NextScan;
        static bool s_Hooked;

        // One mirror surface and its reflection images.
        sealed class Target
        {
            public readonly Renderer renderer;
            public readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            public readonly Matrix4x4[] viewProjections = new Matrix4x4[2];
            public readonly Vector4[] eyes = new Vector4[2];
            public Mirror settings;
            public RenderTexture left;
            public RenderTexture right;
            public bool hdr;
            public int lastRender = -1;
            public Camera lastCamera;
            public bool announced;

            public Target(Renderer renderer) => this.renderer = renderer;
        }

        // ------------------------------------------------------------------ Startup

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ReleaseAll();
            s_Hooked = false;
            s_NextScan = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RuntimeStartup() => Hook();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorStartup()
        {
            Hook();
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
        }
#endif

        static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => s_NextScan = 0f;

        /// <summary>Called by Mirror components so their settings are used for their object.</summary>
        internal static void SetSettings(Renderer renderer, Mirror settings)
        {
            Hook();
            var target = Find(renderer);
            if (target == null)
            {
                if (settings == null) return;
                target = new Target(renderer);
                s_Targets.Add(target);
            }
            target.settings = settings;
        }

        // ------------------------------------------------------------------ Finding mirrors

        static void Scan()
        {
            s_NextScan = Time.realtimeSinceStartup + (Application.isPlaying ? 2f : 0.5f);

            for (int i = s_Targets.Count - 1; i >= 0; i--)
            {
                var target = s_Targets[i];
                if (target.renderer != null && UsesMirrorMaterial(target.renderer)) continue;
                Release(target);
                s_Targets.RemoveAt(i);
            }

            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!UsesMirrorMaterial(renderer) || Find(renderer) != null) continue;
                s_Targets.Add(new Target(renderer) { settings = renderer.GetComponent<Mirror>() });
            }
        }

        static Target Find(Renderer renderer)
        {
            foreach (var target in s_Targets)
                if (target.renderer == renderer) return target;
            return null;
        }

        internal static bool UsesMirrorMaterial(Renderer renderer)
        {
            foreach (var material in renderer.sharedMaterials)
                if (IsMirrorMaterial(material)) return true;
            return false;
        }

        // Matched by shader name; the "KapiMirror" tag also counts, for custom shaders built on GlassCore.
        static bool IsMirrorMaterial(Material material)
        {
            if (material == null || material.shader == null) return false;
            return material.shader.name == "Kapi/Mirror" || material.GetTag("KapiMirror", false) == "True";
        }

        // ------------------------------------------------------------------ Rendering

        static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            s_RenderCount++;
            if (Time.realtimeSinceStartup >= s_NextScan) Scan();
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == s_ReflectionCamera || s_Targets.Count == 0) return;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;

            int eyeCount = GetEyes(camera, out int width, out int height);
            for (int i = 0; i < s_Targets.Count; i++)
                Render(s_Targets[i], context, camera, eyeCount, width, height);
        }

        // The exact view and projection the headset renders each eye with, straight from the XR display, so the
        // reflection lines up pixel for pixel. Falls back to the camera itself when not in VR.
        static int GetEyes(Camera camera, out int width, out int height)
        {
            if (camera.cameraType == CameraType.Game && camera.targetTexture == null && camera.stereoTargetEye != StereoTargetEyeMask.None)
            {
                var display = ActiveDisplay();
                var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                if (display != null && (cameraData == null || cameraData.allowXRRendering))
                {
                    int count = 0;
                    for (int pass = 0; pass < display.GetRenderPassCount() && count < 2; pass++)
                    {
                        display.GetRenderPass(pass, out var renderPass);
                        for (int i = 0; i < renderPass.GetRenderParameterCount() && count < 2; i++)
                        {
                            renderPass.GetRenderParameter(camera, i, out var parameter);
                            s_EyeViews[count] = parameter.view;
                            s_EyeProjections[count] = parameter.projection;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        width = XRSettings.eyeTextureWidth;
                        height = XRSettings.eyeTextureHeight;
                        return count;
                    }
                }
            }

            s_EyeViews[0] = camera.worldToCameraMatrix;
            s_EyeProjections[0] = camera.projectionMatrix;
            width = camera.pixelWidth;
            height = camera.pixelHeight;
            return 1;
        }

        static XRDisplaySubsystem ActiveDisplay()
        {
            SubsystemManager.GetSubsystems(s_Displays);
            foreach (var display in s_Displays)
                if (display.running) return display;
            return null;
        }

        static void Render(Target target, ScriptableRenderContext context, Camera camera, int eyeCount, int width, int height)
        {
            var renderer = target.renderer;
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
            if (target.lastRender == s_RenderCount && target.lastCamera == camera) return;

            var settings = target.settings;
            Vector3 viewer = camera.transform.position;
            Bounds bounds = renderer.bounds;
            float maxDistance = settings != null ? settings.maxDistance : 0f;
            if (maxDistance > 0f && (bounds.ClosestPoint(viewer) - viewer).sqrMagnitude > maxDistance * maxDistance) return;

            GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix, s_FrustumPlanes);
            if (!GeometryUtility.TestPlanesAABB(s_FrustumPlanes, bounds)) return;

            target.lastRender = s_RenderCount;
            target.lastCamera = camera;

            GetSurface(renderer, viewer, out Vector3 point, out Vector3 normal);
            Matrix4x4 reflection = ReflectionMatrix(normal, -Vector3.Dot(normal, point));

            bool quest = Application.platform == RuntimePlatform.Android;
            float scale = settings != null ? settings.resolution : quest ? 0.75f : 1f;
            int textureWidth = Mathf.Max(16, Mathf.RoundToInt(width * scale));
            int textureHeight = Mathf.Max(16, Mathf.RoundToInt(height * scale));
            bool hdr = camera.allowHDR;
            EnsureTexture(ref target.left, textureWidth, textureHeight, hdr, target.hdr, renderer.name + " Left");
            if (eyeCount > 1) EnsureTexture(ref target.right, textureWidth, textureHeight, hdr, target.hdr, renderer.name + " Right");
            target.hdr = hdr;

            var reflectionCamera = GetReflectionCamera();
            bool shadows = settings != null ? settings.shadows : !quest;
            bool drawSky = ConfigureReflectionCamera(reflectionCamera, camera, settings != null ? (int)settings.reflectLayers : ~0, shadows);
            float clipOffset = settings != null ? settings.clipOffset : 0f;

            // Don't let the mirror draw itself into its own reflection.
            bool wasHidden = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;

            for (int eye = 0; eye < eyeCount; eye++)
            {
                Matrix4x4 eyeView = s_EyeViews[eye];
                Matrix4x4 view = eyeView * reflection;

                // Clip at the mirror's surface so nothing behind the mirror shows up in it.
                Vector4 clipPlane = CameraSpacePlane(view, point, normal, clipOffset);
                Matrix4x4 projection = s_FlipX * ObliqueProjection(s_EyeProjections[eye], clipPlane);

                reflectionCamera.worldToCameraMatrix = view;
                reflectionCamera.projectionMatrix = projection;

                // Place the camera at the mirrored eye too, for anything that reads its position (fog, specular).
                Matrix4x4 eyeToWorld = eyeView.inverse;
                Vector3 eyePosition = eyeToWorld.GetColumn(3);
                Vector3 forward = reflection.MultiplyVector(-(Vector3)eyeToWorld.GetColumn(2));
                Vector3 up = reflection.MultiplyVector(eyeToWorld.GetColumn(1));
                reflectionCamera.transform.SetPositionAndRotation(reflection.MultiplyPoint(eyePosition), Quaternion.LookRotation(forward, up));
                reflectionCamera.targetTexture = eye == 0 ? target.left : target.right;
                if (drawSky)
                {
                    // The sky is infinitely far away, so it skips the mirror-surface clip (which would cut it off).
                    s_SkyboxPass.projection = s_FlipX * s_EyeProjections[eye];
                    s_SkyboxPass.view = view;
                    s_ReflectionCameraData.scriptableRenderer.EnqueuePass(s_SkyboxPass);
                }

#pragma warning disable CS0618 // Still the way to render a camera from inside another camera's frame in this URP version.
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618

                target.viewProjections[eye] = projection * view;
                target.eyes[eye] = eyePosition;
            }

            reflectionCamera.targetTexture = null;
            renderer.forceRenderingOff = wasHidden;

            int right = eyeCount > 1 ? 1 : 0;
            var block = target.block;
            renderer.GetPropertyBlock(block);
            block.SetTexture(s_TextureLeftId, target.left);
            block.SetTexture(s_TextureRightId, right == 1 ? target.right : target.left);
            block.SetMatrix(s_ViewProjectionLeftId, target.viewProjections[0]);
            block.SetMatrix(s_ViewProjectionRightId, target.viewProjections[right]);
            block.SetVector(s_EyeLeftId, target.eyes[0]);
            block.SetVector(s_EyeRightId, target.eyes[right]);
            block.SetFloat(s_ActiveId, 1f);
            renderer.SetPropertyBlock(block);

            if (!target.announced)
            {
                target.announced = true;
                Debug.Log($"[Kapi] Mirror '{renderer.name}' is reflecting ({textureWidth}x{textureHeight}, {(eyeCount > 1 ? "both eyes" : "one view")}).", renderer);
            }
        }

        // Returns true when the sky should be drawn by SkyboxPass instead of the camera.
        static bool ConfigureReflectionCamera(Camera reflectionCamera, Camera source, int layers, bool shadows)
        {
            // Left to itself, the camera draws the sky un-mirrored, so it would swing the wrong way as you turn.
            // SkyboxPass draws the mirrored sky over it (Unity only draws a sky for a camera set to show one).
            bool drawSky = source.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null;
            reflectionCamera.clearFlags = source.clearFlags;
            reflectionCamera.backgroundColor = source.backgroundColor;
            reflectionCamera.cullingMask = layers;
            reflectionCamera.nearClipPlane = source.nearClipPlane;
            reflectionCamera.farClipPlane = source.farClipPlane;
            reflectionCamera.allowHDR = source.allowHDR;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.useOcclusionCulling = false;

            // No post-processing here: the main camera's post-processing already applies to the mirror on screen,
            // so doing it twice would make the reflection look different from the real thing.
            var data = s_ReflectionCameraData;
            data.renderShadows = shadows;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            data.allowXRRendering = false;
            return drawSky;
        }

        // Draws the sky with the reflection camera's exact (mirrored) view and projection, the same way URP draws
        // the sky for VR eyes. URP's own sky for a plain camera works out its view from the camera's rotation,
        // which can't be mirrored.
        sealed class SkyboxPass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public RendererListHandle sky;
            }

            public Matrix4x4 projection;
            public Matrix4x4 view;

            public SkyboxPass()
            {
                // Right after URP's own (un-mirrored) sky, so this one covers it.
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Kapi Mirror Sky", out var data))
                {
                    data.sky = renderGraph.CreateSkyboxRendererList(cameraData.camera, projection, view);
                    builder.UseRendererList(data.sky);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData passData, RasterGraphContext context) => context.cmd.DrawRendererList(passData.sky));
                }
            }
        }

        static Camera GetReflectionCamera()
        {
            if (s_ReflectionCamera != null) return s_ReflectionCamera;

            var cameraObject = new GameObject("[Kapi Mirror Camera]") { hideFlags = HideFlags.HideAndDontSave };
            s_ReflectionCamera = cameraObject.AddComponent<Camera>();
            s_ReflectionCamera.enabled = false;
            s_ReflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
            s_ReflectionCameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            return s_ReflectionCamera;
        }

        // The mirror surface is the glass's thinnest side: a Quad's face, a Plane's top, or a thin Cube's front.
        // Uses the side facing the viewer, at its front face, so thick mirrors line up with their surface too.
        // Works from the object's full matrix, so glass tilted inside a stretched parent (which Unity skews) still
        // lines up exactly.
        static void GetSurface(Renderer renderer, Vector3 viewer, out Vector3 point, out Vector3 normal)
        {
            Bounds local = GetMirrorBounds(renderer);
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            Vector3 center = toWorld.MultiplyPoint3x4(local.center);

            normal = renderer.transform.forward;
            float thinnest = float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                // A face's normal is square to the two edges lying in it, which stays true when skewed.
                Vector3 across = toWorld.GetColumn((axis + 1) % 3);
                Vector3 along = toWorld.GetColumn((axis + 2) % 3);
                Vector3 faceNormal = Vector3.Cross(across, along);
                if (faceNormal.sqrMagnitude < 1e-12f) continue;
                faceNormal.Normalize();

                float thickness = Mathf.Abs(Vector3.Dot(faceNormal, toWorld.GetColumn(axis))) * local.size[axis];
                if (thickness >= thinnest) continue;
                thinnest = thickness;
                normal = faceNormal;
            }

            if (Vector3.Dot(viewer - center, normal) < 0f) normal = -normal;
            point = center + normal * (thinnest < float.MaxValue ? thinnest * 0.5f : 0f);
        }

        // Just the part of the mesh drawn with the mirror material, so a mirror that is one material on a bigger
        // model (glass in a carved frame) uses the glass, not the whole model.
        static Bounds GetMirrorBounds(Renderer renderer)
        {
            if (renderer is MeshRenderer && !renderer.isPartOfStaticBatch && renderer.TryGetComponent(out MeshFilter filter))
            {
                var mesh = filter.sharedMesh;
                if (mesh != null && mesh.subMeshCount > 1)
                {
                    var materials = renderer.sharedMaterials;
                    bool found = false;
                    Bounds bounds = default;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (!IsMirrorMaterial(materials[i])) continue;
                        // Extra materials past the last sub-mesh draw that last sub-mesh again.
                        Bounds part = mesh.GetSubMesh(Mathf.Min(i, mesh.subMeshCount - 1)).bounds;
                        if (found) bounds.Encapsulate(part);
                        else bounds = part;
                        found = true;
                    }
                    if (found && bounds.size.sqrMagnitude > 0f) return bounds;
                }
            }
            return renderer.localBounds;
        }

        static void EnsureTexture(ref RenderTexture texture, int width, int height, bool hdr, bool wasHdr, string name)
        {
            if (texture != null && texture.width == width && texture.height == height && hdr == wasHdr) return;

            ReleaseTexture(ref texture);
            texture = new RenderTexture(width, height, 24, hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)
            {
                name = "Mirror Reflection " + name,
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
        }

        // ------------------------------------------------------------------ Cleanup

        static void Release(Target target)
        {
            ReleaseTexture(ref target.left);
            ReleaseTexture(ref target.right);
            if (target.renderer == null) return;

            // Back to the reflection-probe look until it reflects again.
            target.renderer.GetPropertyBlock(target.block);
            target.block.SetFloat(s_ActiveId, 0f);
            target.renderer.SetPropertyBlock(target.block);
        }

        static void ReleaseAll()
        {
            foreach (var target in s_Targets) Release(target);
            s_Targets.Clear();

            if (s_ReflectionCamera != null) DestroyObject(s_ReflectionCamera.gameObject);
            s_ReflectionCamera = null;
            s_ReflectionCameraData = null;
        }

        static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            DestroyObject(texture);
            texture = null;
        }

        static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        // ------------------------------------------------------------------ Math

        // Mirrors points across the plane dot(normal, x) + distance = 0.
        static Matrix4x4 ReflectionMatrix(Vector3 n, float d)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f - 2f * n.x * n.x;
            m.m01 = -2f * n.x * n.y;
            m.m02 = -2f * n.x * n.z;
            m.m03 = -2f * d * n.x;
            m.m10 = -2f * n.y * n.x;
            m.m11 = 1f - 2f * n.y * n.y;
            m.m12 = -2f * n.y * n.z;
            m.m13 = -2f * d * n.y;
            m.m20 = -2f * n.z * n.x;
            m.m21 = -2f * n.z * n.y;
            m.m22 = 1f - 2f * n.z * n.z;
            m.m23 = -2f * d * n.z;
            return m;
        }

        // The mirror's plane in the reflection camera's space, nudged by the clip offset.
        static Vector4 CameraSpacePlane(Matrix4x4 view, Vector3 point, Vector3 normal, float offset)
        {
            Vector3 cameraPoint = view.MultiplyPoint(point + normal * offset);
            Vector3 cameraNormal = view.MultiplyVector(normal).normalized;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPoint, cameraNormal));
        }

        // Replaces the projection's near plane with the given clip plane (Lengyel's oblique near-plane clipping).
        static Matrix4x4 ObliqueProjection(Matrix4x4 projection, Vector4 clipPlane)
        {
            Vector4 q = projection.inverse * new Vector4(Mathf.Sign(clipPlane.x), Mathf.Sign(clipPlane.y), 1f, 1f);
            Vector4 c = clipPlane * (2f / Vector4.Dot(clipPlane, q));
            projection[2] = c.x - projection[3];
            projection[6] = c.y - projection[7];
            projection[10] = c.z - projection[11];
            projection[14] = c.w - projection[15];
            return projection;
        }
    }
}
