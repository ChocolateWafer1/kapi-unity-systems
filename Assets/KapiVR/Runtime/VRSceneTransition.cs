using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KapiVR
{
    /// <summary>
    /// Fades to black, loads a scene in the background, then fades the new scene in on whatever camera it uses.
    /// Only a tiny helper object survives the load; it deletes itself once the fade-in is done, so nothing from
    /// the menu's VR player carries over.
    /// </summary>
    public static class VRSceneTransition
    {
        public static bool isTransitioning { get; private set; }

        /// <summary>Raised when a transition starts, with the path of the scene being loaded.</summary>
        public static event Action<string> started;

        public static bool LoadScene(string scenePath, float fadeOutDuration = 0.9f, float fadeInDuration = 1f, Material fadeMaterial = null)
        {
            if (isTransitioning) return false;

            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogWarning("[Kapi VR] No scene was chosen to load.");
                return false;
            }

            if (!CanLoad(scenePath))
            {
                Debug.LogError($"[Kapi VR] Can't load '{scenePath}': it isn't in the build's scene list (File > Build Profiles > Scene List).");
                return false;
            }

            isTransitioning = true;
            var runner = new GameObject("[Kapi VR Scene Transition]").AddComponent<Runner>();
            Object.DontDestroyOnLoad(runner.gameObject);
            runner.Begin(scenePath, fadeOutDuration, fadeInDuration, fadeMaterial);
            started?.Invoke(scenePath);
            return true;
        }

        static bool CanLoad(string scenePath)
        {
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) >= 0) return true;
#if UNITY_EDITOR
            // In the editor, scenes that aren't in the build list can still be played.
            return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scenePath) != null;
#else
            return false;
#endif
        }

        static AsyncOperation LoadAsync(string scenePath)
        {
#if UNITY_EDITOR
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
        }

        sealed class Runner : MonoBehaviour
        {
            const float k_HoldBlack = 0.35f;

            readonly List<VRScreenFader> m_Faders = new List<VRScreenFader>();
            Material m_FadeMaterial;
            float m_StartVolume = 1f;
            float m_Alpha;

            public void Begin(string scenePath, float fadeOut, float fadeIn, Material fadeMaterial)
            {
                var rig = VRPlayerRig.current;
                m_FadeMaterial = fadeMaterial != null ? fadeMaterial : rig != null ? rig.fadeMaterial : null;
                m_StartVolume = AudioListener.volume;

                // Any camera that starts drawing during the transition gets the fade before its first frame,
                // including the new scene's camera, so there's never a flash of unfaded picture.
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                if (rig != null && rig.head != null) AddFader(rig.head);

                StartCoroutine(Run(scenePath, fadeOut, fadeIn));
            }

            IEnumerator Run(string scenePath, float fadeOut, float fadeIn)
            {
                var whoosh = gameObject.AddComponent<AudioSource>();
                whoosh.spatialBlend = 0f;
                whoosh.volume = 0.35f;
                whoosh.PlayOneShot(VRSynth.whoosh);

                // Start loading right away; the scene only switches once the screen is fully black.
                var load = LoadAsync(scenePath);
                load.allowSceneActivation = false;

                // 1. Fade the menu (and its sound) out.
                yield return Animate(0f, 1f, fadeOut);

                // 2. Switch scenes while black.
                while (load.progress < 0.9f) yield return null;
                load.allowSceneActivation = true;
                while (!load.isDone) yield return null;

                // 3. Hold black briefly so the new scene's player and tracking can settle, then fade in.
                for (float held = 0f; held < k_HoldBlack; held += Time.unscaledDeltaTime)
                {
                    SetFade(1f);
                    yield return null;
                }
                yield return Animate(1f, 0f, fadeIn);

                Destroy(gameObject);
            }

            IEnumerator Animate(float from, float to, float duration)
            {
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(duration, 0.0001f))
                {
                    SetFade(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
                    yield return null;
                }
                SetFade(to);
            }

            void SetFade(float alpha)
            {
                m_Alpha = alpha;
                for (int i = m_Faders.Count - 1; i >= 0; i--)
                {
                    if (m_Faders[i] == null) m_Faders.RemoveAt(i);
                    else m_Faders[i].alpha = alpha;
                }
                AudioListener.volume = m_StartVolume * (1f - alpha);
            }

            void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null) return;
                if (camera.GetComponent<VRScreenFader>() != null) return;
                AddFader(camera);
            }

            void AddFader(Camera camera)
            {
                var fader = VRScreenFader.Attach(camera, m_FadeMaterial);
                if (fader == null) return;
                fader.alpha = m_Alpha;
                m_Faders.Add(fader);
            }

            void OnDestroy()
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                foreach (var fader in m_Faders)
                    if (fader != null) Destroy(fader);
                AudioListener.volume = m_StartVolume;
                isTransitioning = false;
            }
        }
    }
}
