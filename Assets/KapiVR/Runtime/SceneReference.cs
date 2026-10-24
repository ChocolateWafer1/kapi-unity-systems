using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KapiVR
{
    /// <summary>
    /// A scene picked in the Inspector by dragging in the scene file. Stores the path so it still works in builds,
    /// where scene assets themselves don't exist.
    /// </summary>
    [Serializable]
    public class SceneReference
    {
        [SerializeField] Object m_SceneAsset;
        [SerializeField] string m_ScenePath = "";

        public string path => m_ScenePath;
        public bool isSet => !string.IsNullOrEmpty(m_ScenePath);

#if UNITY_EDITOR
        /// <summary>Keeps the stored path in sync if the scene file was moved or renamed.</summary>
        public void RefreshPath()
        {
            if (m_SceneAsset == null) return;

            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(m_SceneAsset);
            if (!string.IsNullOrEmpty(assetPath)) m_ScenePath = assetPath;
        }
#endif
    }
}
