using GorillaLocomotion;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KapiWater.EditorTools
{
    [CustomEditor(typeof(WaterVolume))]
    [CanEditMultipleObjects]
    sealed class WaterVolumeEditor : Editor
    {
        const string k_MaterialsFolder = "Assets/KapiWater/Materials";

        readonly BoxBoundsHandle m_Box = new BoxBoundsHandle();
        Material[] m_Presets;

        void OnEnable()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { k_MaterialsFolder });
            m_Presets = new Material[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                m_Presets[i] = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
        }

        public override void OnInspectorGUI()
        {
            var volume = (WaterVolume)target;
            SwimmerStatus();
            LookButtons(volume);
            Warnings(volume);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        void SwimmerStatus()
        {
            if (Application.isPlaying) return;
            var swimmer = FindFirstObjectByType<GorillaSwimmer>();
            if (swimmer != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"Swimming settings live on the Gorilla Swimmer on '{swimmer.name}'.", MessageType.None);
                    if (GUILayout.Button("Select", GUILayout.Width(60f), GUILayout.Height(38f))) Selection.activeObject = swimmer;
                }
                return;
            }

            var player = FindFirstObjectByType<Player>();
            if (player == null)
            {
                EditorGUILayout.HelpBox("No GorillaLocomotion Player in this scene, so nobody can swim here yet.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox($"A Gorilla Swimmer gets added to '{player.name}' when you press Play. Add it now to change how swimming feels.", MessageType.Info);
                if (GUILayout.Button("Add It", GUILayout.Width(60f), GUILayout.Height(38f)))
                    Selection.activeObject = Undo.AddComponent<GorillaSwimmer>(player.gameObject);
            }
        }

        void LookButtons(WaterVolume volume)
        {
            if (m_Presets == null || m_Presets.Length == 0) return;
            EditorGUILayout.LabelField("Water Look", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var preset in m_Presets)
                {
                    if (preset == null) continue;
                    string label = preset.name.Replace("Water - ", "");
                    bool current = volume.SurfaceMaterial == preset;
                    using (new EditorGUI.DisabledScope(current))
                    {
                        if (!GUILayout.Button(label, EditorStyles.miniButton)) continue;
                    }
                    foreach (var t in targets)
                    {
                        var renderer = ((WaterVolume)t).GetComponent<MeshRenderer>();
                        Undo.RecordObject(renderer, "Change Water Look");
                        renderer.sharedMaterial = preset;
                    }
                }
            }
            EditorGUILayout.HelpBox("Every look is a material using the Kapi/Water shader; edit its waves, colors, foam and caustics at the bottom of this inspector. Changes affect every water using that material.", MessageType.None);
        }

        static void Warnings(WaterVolume volume)
        {
            Vector3 euler = volume.transform.eulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(euler.x, 0f)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(euler.z, 0f)) > 0.01f)
                EditorGUILayout.HelpBox("Water is always level: only turning around Y is used. Tilting the object tilts the surface you see but not the water you swim in.", MessageType.Warning);

            var material = volume.SurfaceMaterial;
            if (material == null || material.shader == null || material.shader.name != "Kapi/Water")
                EditorGUILayout.HelpBox("Give the Mesh Renderer a material that uses the Kapi/Water shader (pick a look above), or the water won't have waves.", MessageType.Warning);
        }

        void OnSceneGUI()
        {
            var volume = (WaterVolume)target;
            var t = volume.transform;
            Vector3 scale = t.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            var level = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
            Vector3 size = volume.Size;

            using (new Handles.DrawingScope(new Color(0.25f, 0.8f, 1f, 1f), Matrix4x4.TRS(t.position, level, scale)))
            {
                m_Box.center = new Vector3(0f, -size.y * 0.5f, 0f);
                m_Box.size = size;

                EditorGUI.BeginChangeCheck();
                m_Box.DrawHandle();
                if (!EditorGUI.EndChangeCheck()) return;

                // The object stays on the surface, so moving the top moves the object and the depth with it.
                Undo.RecordObjects(new Object[] { volume, t }, "Resize Water");
                Vector3 newSize = Vector3.Max(m_Box.size, new Vector3(0.1f, 0.05f, 0.1f));
                Vector3 top = new Vector3(m_Box.center.x, m_Box.center.y + newSize.y * 0.5f, m_Box.center.z);
                t.position += level * Vector3.Scale(top, scale);
                volume.Size = newSize;
            }
        }
    }
}
