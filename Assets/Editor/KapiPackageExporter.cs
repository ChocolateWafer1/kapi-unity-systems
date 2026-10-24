using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds one .unitypackage per Kapi system into the Builds folder next to Assets. Packages that build on another
/// system include it too, so each one imports on its own; importing several just updates the shared files.
/// Batch mode: Unity -batchmode -projectPath . -executeMethod KapiPackageExporter.ExportAll -quit
/// </summary>
public static class KapiPackageExporter
{
    struct Package
    {
        public string file;
        public string[] folders;
        public Package(string file, params string[] folders) { this.file = file; this.folders = folders; }
    }

    static readonly Package[] k_Packages =
    {
        new Package("Kapi-Text-Effects", "Assets/TextEffects"),
        new Package("Kapi-Glass-and-Mirror", "Assets/WindowGlass"),
        new Package("Kapi-VR-Menu-Player", "Assets/KapiVR", "Assets/TextEffects"),
        new Package("Kapi-Rope", "Assets/KapiRope", "Assets/KapiVR", "Assets/TextEffects"),
        new Package("Kapi-Water-and-Swimming", "Assets/KapiWater", "Assets/KapiRope", "Assets/KapiVR", "Assets/TextEffects"),
        new Package("Kapi-World-Space-Floor", "Assets/Shaders/World Space Lit.shader", "Assets/Materials/Rustic Grey Oak Floor"),
        new Package("Kapi-Everything", "Assets/TextEffects", "Assets/WindowGlass", "Assets/KapiVR", "Assets/KapiRope",
            "Assets/KapiWater", "Assets/Shaders/World Space Lit.shader", "Assets/Materials/Rustic Grey Oak Floor"),
    };

    [MenuItem("Tools/Kapi/Export Unity Packages")]
    public static void ExportAll()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
        Directory.CreateDirectory(output);

        foreach (var package in k_Packages)
        {
            string path = Path.Combine(output, package.file + ".unitypackage");
            AssetDatabase.ExportPackage(package.folders, path, ExportPackageOptions.Recurse);
            Debug.Log($"[Kapi Export] {package.file}: {new FileInfo(path).Length / 1024} KB");
        }

        Debug.Log("[Kapi Export] Done: " + output);
        if (!Application.isBatchMode) EditorUtility.RevealInFinder(output);
    }
}
