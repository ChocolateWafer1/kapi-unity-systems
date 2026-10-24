using UnityEditor;
using UnityEngine;

namespace KapiVR.EditorTools
{
    /// <summary>Shows a <see cref="SceneReference"/> as a scene file slot, and adds the scene to the build when picked.</summary>
    [CustomPropertyDrawer(typeof(SceneReference))]
    sealed class SceneReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var assetProperty = property.FindPropertyRelative("m_SceneAsset");
            var pathProperty = property.FindPropertyRelative("m_ScenePath");

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var scene = EditorGUI.ObjectField(position, label, assetProperty.objectReferenceValue, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                assetProperty.objectReferenceValue = scene;
                pathProperty.stringValue = scene != null ? AssetDatabase.GetAssetPath(scene) : string.Empty;
                if (scene != null) VRBuildScenes.Ensure(pathProperty.stringValue, false);
            }
            EditorGUI.EndProperty();
        }
    }
}
