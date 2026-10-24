using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace KapiVR.EditorTools
{
    /// <summary>
    /// Sets the project up for VR on PC (SteamVR / Quest Link) and Quest standalone: installs XR Plug-in
    /// Management, OpenXR and the XR Interaction Toolkit (with its Starter Assets), turns OpenXR on for both
    /// platforms with the Quest, Index and Vive controller profiles, and sets the Android settings Quest needs.
    /// Runs once by itself after the packages finish installing, and again whenever a VR Player is added.
    /// </summary>
    /// <remarks>
    /// The XR packages are reached by name through reflection so this project still compiles while they are
    /// being installed (or if someone removes them).
    /// </remarks>
    [InitializeOnLoad]
    static class VRXRProjectSetup
    {
        const string k_DoneMarker = "ProjectSettings/KapiVRSetup.txt";
        const string k_XRIPackage = "com.unity.xr.interaction.toolkit";
        const string k_OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        // The versions Unity 6.3 ships as verified.
        static readonly string[] k_Packages =
        {
            "com.unity.xr.management@4.5.3",
            "com.unity.xr.openxr@1.16.0",
            k_XRIPackage + "@3.3.0",
        };

        static readonly BuildTargetGroup[] k_Platforms = { BuildTargetGroup.Standalone, BuildTargetGroup.Android };

        static readonly string[] k_PCProfiles =
        {
            "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.ValveIndexControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.HTCViveControllerProfile",
        };

        static readonly string[] k_QuestFeatures =
        {
            "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile",
            "UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature",
        };

        static VRXRProjectSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += AutoRun;
        }

        /// <summary>True once XR Plug-in Management and OpenXR are installed and compiled.</summary>
        public static bool packagesReady =>
            FindType(k_OpenXRLoader) != null && FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget") != null;

        static void AutoRun()
        {
            if (File.Exists(k_DoneMarker) || !packagesReady) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            Debug.Log("[Kapi VR] Set the project up for VR:\n" + Run());
        }

        /// <summary>Does every step that isn't done yet and returns a readable summary.</summary>
        public static string Run()
        {
            var log = new StringBuilder();

            if (!packagesReady)
            {
                InstallPackages(log);
                return log.ToString();
            }

            bool ok = Step(log, "Turn on OpenXR", EnableOpenXRLoader);
            ok &= Step(log, "Controller profiles and Quest support", EnableOpenXRFeatures);
            ok &= Step(log, "Android settings for Quest", ConfigureAndroid);
            // Last, because importing the sample recompiles scripts.
            ok &= Step(log, "XR Interaction Toolkit Starter Assets", ImportStarterAssets);

            if (ok) File.WriteAllText(k_DoneMarker, "Kapi VR set this project up for OpenXR (PC + Quest). Delete this file to run the setup again.\n");
            return log.ToString();
        }

        static bool Step(StringBuilder log, string name, Func<string> action)
        {
            try
            {
                log.AppendLine($"• {name}: {action()}");
                return true;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                log.AppendLine($"• {name}: FAILED ({inner.Message}). You can finish this in Project Settings > XR Plug-in Management.");
                Debug.LogException(inner);
                return false;
            }
        }

        static void InstallPackages(StringBuilder log)
        {
            var missing = k_Packages.Where(id => PackageInfo.FindForPackageName(id.Substring(0, id.IndexOf('@'))) == null).ToArray();
            if (missing.Length > 0)
            {
                Client.AddAndRemove(missing);
                log.AppendLine("• Installing " + string.Join(", ", missing) + ".");
            }
            log.AppendLine("• Unity is still importing the XR packages. VR setup finishes by itself once they're in.");
        }

        static string EnableOpenXRLoader()
        {
            var generalSettingsType = RequireType("UnityEngine.XR.Management.XRGeneralSettings");
            var perTargetType = RequireType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
            var metadataStoreType = RequireType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");

            string key = (string)generalSettingsType.GetField("k_SettingsKey", BindingFlags.Public | BindingFlags.Static).GetValue(null);

            // Find or create the XR Plug-in Management settings asset, the same way its Project Settings page does.
            EditorBuildSettings.TryGetConfigObject(key, out Object settings);
            if (settings == null)
            {
                var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (guids.Length > 0) settings = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), perTargetType);
            }
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                settings = ScriptableObject.CreateInstance(perTargetType);
                AssetDatabase.CreateAsset(settings, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            }
            EditorBuildSettings.AddConfigObject(key, settings, true);

            var assignLoader = metadataStoreType.GetMethod("AssignLoader", BindingFlags.Public | BindingFlags.Static);
            var result = new StringBuilder();
            foreach (var platform in k_Platforms)
            {
                if (!(bool)Call(settings, "HasSettingsForBuildTarget", platform)) Call(settings, "CreateDefaultSettingsForBuildTarget", platform);
                if (!(bool)Call(settings, "HasManagerSettingsForBuildTarget", platform)) Call(settings, "CreateDefaultManagerSettingsForBuildTarget", platform);

                var manager = Call(settings, "ManagerSettingsForBuildTarget", platform);
                bool assigned = (bool)assignLoader.Invoke(null, new[] { manager, k_OpenXRLoader, (object)platform });
                result.Append(assigned ? $"on for {Name(platform)}. " : $"couldn't turn on for {Name(platform)}. ");
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return result.ToString().Trim();
        }

        static string EnableOpenXRFeatures()
        {
            // Make sure OpenXR's own settings asset exists before asking it for per-platform settings.
            FindType("UnityEditor.XR.OpenXR.OpenXRPackageSettings")
                ?.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, null);

            var settingsType = RequireType("UnityEngine.XR.OpenXR.OpenXRSettings");
            var helpersType = RequireType("UnityEditor.XR.OpenXR.Features.FeatureHelpers");
            var getSettings = settingsType.GetMethod("GetSettingsForBuildTargetGroup", BindingFlags.Public | BindingFlags.Static);
            var getFeature = settingsType.GetMethod("GetFeature", new[] { typeof(Type) });
            var refresh = helpersType.GetMethod("RefreshFeatures", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BuildTargetGroup) }, null);

            var result = new StringBuilder();
            foreach (var platform in k_Platforms)
            {
                refresh.Invoke(null, new object[] { platform });
                var settings = getSettings.Invoke(null, new object[] { platform }) as Object;
                if (settings == null)
                {
                    result.Append($"{Name(platform)} skipped (install {Name(platform)} Build Support in Unity Hub). ");
                    continue;
                }

                int enabled = 0;
                foreach (var featureName in platform == BuildTargetGroup.Android ? k_QuestFeatures : k_PCProfiles)
                {
                    var featureType = FindType(featureName);
                    var feature = featureType != null ? getFeature.Invoke(settings, new object[] { featureType }) as Object : null;
                    if (feature == null) continue;

                    feature.GetType().GetProperty("enabled").SetValue(feature, true);
                    EditorUtility.SetDirty(feature);
                    enabled++;
                }

                EditorUtility.SetDirty(settings);
                result.Append($"{enabled} on for {Name(platform)}. ");
            }

            AssetDatabase.SaveAssets();
            return result.ToString().Trim();
        }

        static string ConfigureAndroid()
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel32)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            return "IL2CPP, ARM64, minimum Android 12L (API 32).";
        }

        static string ImportStarterAssets()
        {
            var package = PackageInfo.FindForPackageName(k_XRIPackage);
            if (package == null) return "skipped (XR Interaction Toolkit isn't installed yet).";

            foreach (var sample in Sample.FindByPackage(k_XRIPackage, package.version))
            {
                if (sample.displayName != "Starter Assets") continue;
                if (sample.isImported) return "already imported.";

                return sample.Import(Sample.ImportOptions.HideImportWindow)
                    ? "imported into Assets/Samples."
                    : "couldn't import; use Package Manager > XR Interaction Toolkit > Samples.";
            }
            return "skipped (sample not found in this version).";
        }

        static object Call(object target, string method, BuildTargetGroup platform)
        {
            return target.GetType().GetMethod(method, new[] { typeof(BuildTargetGroup) }).Invoke(target, new object[] { platform });
        }

        static string Name(BuildTargetGroup platform) => platform == BuildTargetGroup.Android ? "Android (Quest)" : "PC";

        static Type RequireType(string fullName)
        {
            return FindType(fullName) ?? throw new InvalidOperationException($"{fullName} not found");
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
