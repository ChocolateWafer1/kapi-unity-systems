using System;
using System.IO;
using GorillaLocomotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KapiWater.EditorTools
{
    /// <summary>
    /// Bakes the water textures, the preset materials and the drag-in Water prefabs into Assets/KapiWater. Runs by
    /// itself the first time the project opens with this folder (only creating what's missing, so your edits to the
    /// materials and prefabs are never overwritten), and from Tools > Kapi Water.
    /// </summary>
    [InitializeOnLoad]
    static class WaterAssetBuilder
    {
        const string k_Root = "Assets/KapiWater";
        const string k_Textures = k_Root + "/Textures";
        const string k_Materials = k_Root + "/Materials";
        const string k_Presets = k_Root + "/Presets";
        const string k_RipplePath = k_Textures + "/Water Ripples Normal.png";
        const string k_CausticsPath = k_Textures + "/Water Caustics.png";
        const string k_FoamPath = k_Textures + "/Water Foam.png";
        public const string MainPrefabPath = k_Root + "/Water.prefab";
        const string k_ShaderName = "Kapi/Water";

        /// <summary>One ready-made look: material values plus the matching underwater view.</summary>
        sealed class Preset
        {
            public string name;
            public Action<Material> material;
            public Action<SerializedObject> volume;
        }

        static readonly Preset[] s_Presets =
        {
            new Preset { name = "Tropical Lagoon", material = TropicalLagoon, volume = v => Volume(v, 1f, 0.3f, 40f, new Color(0.85f, 0.97f, 0.92f, 0.9f), 10f, 1f) },
            new Preset { name = "Swimming Pool", material = SwimmingPool, volume = v => Volume(v, 0.7f, 0.2f, 8f, new Color(0.9f, 0.97f, 1f, 0.8f), 20f, 0.8f) },
            new Preset { name = "Deep Ocean", material = DeepOcean, volume = v => Volume(v, 1.2f, 0.45f, 90f, new Color(0.7f, 0.85f, 0.9f, 0.9f), 6f, 1.2f) },
            new Preset { name = "Murky Lake", material = MurkyLake, volume = v => Volume(v, 0.8f, 0.5f, 220f, new Color(0.62f, 0.58f, 0.42f, 0.95f), 3f, 1.4f) },
        };

        static WaterAssetBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (TexturesOutdated())
                {
                    BakeTextures(true);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[Kapi Water] Rebaked the water textures with the latest look.");
                }
                if (!NeedsAssets()) return;
                CreateMissingAssets();
                Debug.Log($"[Kapi Water] Created the water textures, materials and prefabs. Drag {MainPrefabPath} into a scene to add water.");
            };
        }

        // Bumped whenever the texture baker changes, so existing projects get the new textures (same files, so
        // materials keep pointing at them).
        const string k_TextureVersion = "kapi-water-textures-2";

        static bool TexturesOutdated()
        {
            foreach (var path in new[] { k_RipplePath, k_CausticsPath, k_FoamPath })
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.userData != k_TextureVersion) return true;
            }
            return false;
        }

        static bool NeedsAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MainPrefabPath) == null) return true;
            foreach (var preset in s_Presets)
                if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(preset)) == null) return true;
            return false;
        }

        /// <summary>For building from the command line: Unity -batchmode -executeMethod KapiWater.EditorTools.WaterAssetBuilder.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            CreateMissingAssets();
        }

        [MenuItem("Tools/Kapi Water/Create Missing Water Assets", priority = 1)]
        public static void CreateMissingAssets()
        {
            var shader = Shader.Find(k_ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Kapi Water] The '{k_ShaderName}' shader isn't imported yet; try Tools > Kapi Water > Create Missing Water Assets again in a moment.");
                return;
            }

            EnsureFolder(k_Textures);
            EnsureFolder(k_Materials);
            EnsureFolder(k_Presets);
            BakeTextures(false);

            foreach (var preset in s_Presets)
                if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(preset)) == null) CreateMaterial(shader, preset);

            // Build the prefabs in a hidden scene so the open scene isn't touched.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var main = AssetDatabase.LoadAssetAtPath<GameObject>(MainPrefabPath);
                if (main == null) main = CreateMainPrefab(s_Presets[0], scene);
                for (int i = 1; i < s_Presets.Length; i++)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(s_Presets[i])) == null) CreateVariant(main, s_Presets[i], scene);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kapi Water/Rebake Water Textures", priority = 2)]
        static void RebakeTextures()
        {
            EnsureFolder(k_Textures);
            BakeTextures(true);
            AssetDatabase.SaveAssets();
            Debug.Log("[Kapi Water] Rebaked the ripple, caustics and foam textures.");
        }

        [MenuItem("Tools/Kapi Water/Add Gorilla Swimmer To Player", priority = 20)]
        static void AddSwimmerToPlayer()
        {
            var player = Object.FindFirstObjectByType<Player>();
            if (player == null)
            {
                EditorUtility.DisplayDialog("Kapi Water", "There's no GorillaLocomotion Player in the open scene.", "OK");
                return;
            }
            var swimmer = player.GetComponent<GorillaSwimmer>();
            if (swimmer == null) swimmer = Undo.AddComponent<GorillaSwimmer>(player.gameObject);
            Selection.activeObject = swimmer;
            EditorGUIUtility.PingObject(swimmer);
        }

        [MenuItem("GameObject/Kapi Water/Water", false, 10)]
        static void CreateWaterInScene(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainPrefabPath);
            if (prefab == null)
            {
                CreateMissingAssets();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainPrefabPath);
                if (prefab == null) return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            if (command.context is GameObject parent) GameObjectUtility.SetParentAndAlign(instance, parent);
            else if (SceneView.lastActiveSceneView != null) instance.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(instance, "Create Water");
            Selection.activeGameObject = instance;
        }

        static void BakeTextures(bool force)
        {
            if (force || !File.Exists(k_RipplePath)) WriteTexture(k_RipplePath, WaterTextureBaker.RippleNormals(512, 7), 512, true);
            if (force || !File.Exists(k_CausticsPath)) WriteTexture(k_CausticsPath, WaterTextureBaker.Caustics(512, 21), 512, false);
            if (force || !File.Exists(k_FoamPath)) WriteTexture(k_FoamPath, WaterTextureBaker.Foam(256, 3), 256, false);
        }

        static void WriteTexture(string path, Color32[] pixels, int size, bool normalMap)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.userData = k_TextureVersion;
            importer.SaveAndReimport();
        }

        static void CreateMaterial(Shader shader, Preset preset)
        {
            var material = new Material(shader) { name = "Water - " + preset.name };
            material.SetTexture("_RippleNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(k_RipplePath));
            material.SetTexture("_CausticsTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(k_CausticsPath));
            material.SetTexture("_FoamTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(k_FoamPath));
            Toggle(material, "_FoamEnabled", "_KAPI_FOAM", true);
            Toggle(material, "_CausticsEnabled", "_KAPI_CAUSTICS", true);
            Toggle(material, "_LightGlints", "_KAPI_LIGHT_GLINTS", true);
            preset.material(material);
            AssetDatabase.CreateAsset(material, MaterialPath(preset));
        }

        static GameObject CreateMainPrefab(Preset preset, Scene scene)
        {
            var go = new GameObject("Water");
            EditorSceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                int layer = LayerMask.NameToLayer("Water");
                if (layer >= 0) go.layer = layer;

                var volume = go.AddComponent<WaterVolume>();
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(preset));
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;

                var filter = go.GetComponent<MeshFilter>();
                filter.hideFlags = HideFlags.HideInInspector; // The surface mesh is generated; nothing to edit there.

                var settings = new SerializedObject(volume);
                preset.volume(settings);
                settings.ApplyModifiedPropertiesWithoutUndo();

                filter.sharedMesh = null; // Rebuilt from the size whenever the water loads.
                return PrefabUtility.SaveAsPrefabAsset(go, MainPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static void CreateVariant(GameObject main, Preset preset, Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(main, scene);
            try
            {
                instance.name = "Water - " + preset.name;
                instance.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(preset));
                var settings = new SerializedObject(instance.GetComponent<WaterVolume>());
                preset.volume(settings);
                settings.ApplyModifiedPropertiesWithoutUndo();
                instance.GetComponent<MeshFilter>().sharedMesh = null;
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(preset));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static string MaterialPath(Preset preset) => $"{k_Materials}/Water - {preset.name}.mat";
        static string PrefabPath(Preset preset) => $"{k_Presets}/Water - {preset.name}.prefab";

        static void Volume(SerializedObject volume, float distortion, float vignette, float specks, Color speckColor, float darkenWithDepth, float colorAbsorption)
        {
            volume.FindProperty("m_Distortion").floatValue = distortion;
            volume.FindProperty("m_Vignette").floatValue = vignette;
            volume.FindProperty("m_Specks").floatValue = specks;
            volume.FindProperty("m_SpeckColor").colorValue = speckColor;
            volume.FindProperty("m_DarkenWithDepth").floatValue = darkenWithDepth;
            volume.FindProperty("m_ColorAbsorption").floatValue = colorAbsorption;
        }

        static void TropicalLagoon(Material m)
        {
            m.SetColor("_ShallowColor", new Color(0.55f, 0.98f, 0.9f));
            m.SetColor("_DeepColor", new Color(0.0f, 0.3f, 0.4f));
            m.SetFloat("_Visibility", 20f);
            m.SetColor("_ScatterColor", new Color(0.15f, 0.85f, 0.7f));
            m.SetFloat("_ScatterStrength", 0.8f);
            m.SetFloat("_WaveStrength", 0.85f);
            m.SetFloat("_WaveSpeed", 1f);
            m.SetVector("_WaveA", new Vector4(1f, 0.35f, 0.08f, 7f));
            m.SetVector("_WaveB", new Vector4(-0.45f, 1f, 0.07f, 4.1f));
            m.SetVector("_WaveC", new Vector4(0.7f, -0.8f, 0.06f, 2.3f));
            m.SetVector("_WaveD", new Vector4(-0.9f, -0.3f, 0.05f, 1.4f));
            m.SetFloat("_RippleStrength", 0.45f);
            m.SetFloat("_RippleSize", 4.5f);
            m.SetFloat("_RippleSpeed", 0.35f);
            m.SetFloat("_ShoreFoam", 0.8f);
            m.SetFloat("_ShoreFoamWidth", 0.4f);
            m.SetFloat("_CrestFoam", 0.15f);
            m.SetFloat("_CausticsStrength", 1.8f);
            m.SetFloat("_CausticsSize", 2.6f);
            m.SetFloat("_CausticsDepth", 12f);
            m.SetColor("_UnderwaterColor", new Color(0.03f, 0.42f, 0.5f));
            m.SetFloat("_UnderwaterVisibility", 25f);
        }

        static void SwimmingPool(Material m)
        {
            m.SetColor("_ShallowColor", new Color(0.78f, 0.96f, 1f));
            m.SetColor("_DeepColor", new Color(0.05f, 0.42f, 0.75f));
            m.SetFloat("_Visibility", 28f);
            m.SetColor("_ScatterColor", new Color(0.3f, 0.7f, 1f));
            m.SetFloat("_ScatterStrength", 0.35f);
            m.SetFloat("_WaveStrength", 0.35f);
            m.SetFloat("_WaveSpeed", 0.8f);
            m.SetVector("_WaveA", new Vector4(1f, 0.2f, 0.05f, 3f));
            m.SetVector("_WaveB", new Vector4(-0.4f, 1f, 0.05f, 2f));
            m.SetVector("_WaveC", new Vector4(0.6f, -0.8f, 0.04f, 1.3f));
            m.SetVector("_WaveD", new Vector4(-0.9f, -0.3f, 0.03f, 0.8f));
            m.SetFloat("_RippleStrength", 0.45f);
            m.SetFloat("_RippleSize", 3.2f);
            m.SetFloat("_RippleSpeed", 0.25f);
            Toggle(m, "_FoamEnabled", "_KAPI_FOAM", false);
            m.SetFloat("_CausticsStrength", 3f);
            m.SetFloat("_CausticsSize", 1.6f);
            m.SetFloat("_CausticsDepth", 15f);
            m.SetFloat("_CausticsSplit", 0.01f);
            m.SetColor("_UnderwaterColor", new Color(0.12f, 0.55f, 0.8f));
            m.SetFloat("_UnderwaterVisibility", 45f);
        }

        static void DeepOcean(Material m)
        {
            m.SetColor("_ShallowColor", new Color(0.4f, 0.75f, 0.85f));
            m.SetColor("_DeepColor", new Color(0.0f, 0.07f, 0.18f));
            m.SetFloat("_Visibility", 8f);
            m.SetColor("_ScatterColor", new Color(0.05f, 0.55f, 0.6f));
            m.SetFloat("_ScatterStrength", 1f);
            m.SetFloat("_WaveStrength", 1.2f);
            m.SetFloat("_WaveSpeed", 1f);
            m.SetVector("_WaveA", new Vector4(1f, 0.3f, 0.14f, 14f));
            m.SetVector("_WaveB", new Vector4(-0.3f, 1f, 0.11f, 8f));
            m.SetVector("_WaveC", new Vector4(0.8f, -0.6f, 0.09f, 4.5f));
            m.SetVector("_WaveD", new Vector4(-0.95f, -0.2f, 0.07f, 2.2f));
            m.SetFloat("_RippleStrength", 0.6f);
            m.SetFloat("_RippleSize", 5f);
            m.SetFloat("_RippleSpeed", 0.5f);
            m.SetFloat("_ShoreFoam", 0.9f);
            m.SetFloat("_ShoreFoamWidth", 0.6f);
            m.SetFloat("_CrestFoam", 0.55f);
            m.SetFloat("_CrestFoamHeight", 0.6f);
            m.SetFloat("_CausticsStrength", 1f);
            m.SetFloat("_CausticsSize", 3.5f);
            m.SetFloat("_CausticsDepth", 8f);
            m.SetColor("_UnderwaterColor", new Color(0.01f, 0.12f, 0.25f));
            m.SetFloat("_UnderwaterVisibility", 10f);
        }

        static void MurkyLake(Material m)
        {
            m.SetColor("_ShallowColor", new Color(0.55f, 0.6f, 0.35f));
            m.SetColor("_DeepColor", new Color(0.06f, 0.12f, 0.05f));
            m.SetFloat("_Visibility", 2.5f);
            m.SetColor("_ScatterColor", new Color(0.25f, 0.4f, 0.15f));
            m.SetFloat("_ScatterStrength", 0.25f);
            m.SetFloat("_WaveStrength", 0.4f);
            m.SetFloat("_WaveSpeed", 0.7f);
            m.SetVector("_WaveA", new Vector4(1f, 0.4f, 0.05f, 5f));
            m.SetVector("_WaveB", new Vector4(-0.5f, 1f, 0.04f, 3f));
            m.SetVector("_WaveC", new Vector4(0.7f, -0.7f, 0.035f, 1.8f));
            m.SetVector("_WaveD", new Vector4(-0.8f, -0.4f, 0.03f, 1.1f));
            m.SetFloat("_RippleStrength", 0.35f);
            m.SetFloat("_RippleSize", 4f);
            m.SetFloat("_RippleSpeed", 0.15f);
            m.SetFloat("_Smoothness", 0.9f);
            m.SetFloat("_ShoreFoam", 0.4f);
            m.SetFloat("_ShoreFoamWidth", 0.3f);
            m.SetFloat("_CrestFoam", 0f);
            m.SetColor("_FoamColor", new Color(0.8f, 0.82f, 0.7f));
            m.SetFloat("_CausticsStrength", 0.5f);
            m.SetFloat("_CausticsSize", 3f);
            m.SetFloat("_CausticsDepth", 3f);
            m.SetColor("_UnderwaterColor", new Color(0.12f, 0.17f, 0.08f));
            m.SetFloat("_UnderwaterVisibility", 3.5f);
        }

        static void Toggle(Material material, string property, string keyword, bool on)
        {
            material.SetFloat(property, on ? 1f : 0f);
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
