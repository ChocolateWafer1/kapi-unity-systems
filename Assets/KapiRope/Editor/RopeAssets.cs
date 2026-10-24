using System.IO;
using UnityEditor;
using UnityEngine;

namespace KapiRope.EditorTools
{
    /// <summary>Saves generated braid textures and URP materials into Assets/KapiRope.</summary>
    [InitializeOnLoad]
    static class RopeAssets
    {
        const string k_TexturesFolder = "Assets/KapiRope/Textures";
        const string k_MaterialsFolder = "Assets/KapiRope/Materials";
        const string k_DefaultMaterialPath = k_MaterialsFolder + "/Rope - Climbing Blue.mat";

        static RopeAssets()
        {
            Rope.EditorDefaultMaterial = GetOrCreateDefaultMaterial;
        }

        public static Material GetOrCreateDefaultMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_DefaultMaterialPath);
            return material != null ? material : CreateBraidMaterial(RopeBraidStyle.Preset(0), k_DefaultMaterialPath);
        }

        /// <summary>Bakes the style into albedo and normal PNGs plus a material. Pass a path, or null for a new unique one.</summary>
        public static Material CreateBraidMaterial(RopeBraidStyle style, string materialPath, string name = "Rope - Braid")
        {
            EnsureFolder(k_TexturesFolder);
            EnsureFolder(k_MaterialsFolder);
            materialPath ??= AssetDatabase.GenerateUniqueAssetPath($"{k_MaterialsFolder}/{name}.mat");
            string baseName = Path.GetFileNameWithoutExtension(materialPath);
            string albedoPath = AssetDatabase.GenerateUniqueAssetPath($"{k_TexturesFolder}/{baseName} Albedo.png");
            string normalPath = AssetDatabase.GenerateUniqueAssetPath($"{k_TexturesFolder}/{baseName} Normal.png");

            int size = (int)style.textureSize;
            var albedoPixels = new Color32[size * size];
            var normalPixels = new Color32[size * size];
            RopeBraidTexture.Generate(style, size, albedoPixels, normalPixels);
            WritePng(albedoPath, albedoPixels, size);
            WritePng(normalPath, normalPixels, size);
            ConfigureTexture(albedoPath, false, size);
            ConfigureTexture(normalPath, true, size);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = baseName };
            RopeBraidTexture.ApplyTextures(material, AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        static void WritePng(string path, Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        static void ConfigureTexture(string path, bool isNormalMap, int size)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = Mathf.Max(size, 256);
            importer.SaveAndReimport();
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
