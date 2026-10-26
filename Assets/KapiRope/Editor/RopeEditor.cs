using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KapiRope.EditorTools
{
    [CustomEditor(typeof(Rope))]
    [CanEditMultipleObjects]
    sealed class RopeEditor : Editor
    {
        const int k_PreviewSize = 128;
        static readonly string[] k_Hidden = { "m_Script", "m_BraidStyle" };
        static bool s_ShowBraidMaker;

        int m_Preset;
        Texture2D m_Preview;
        string m_PreviewKey;

        void OnDisable()
        {
            if (m_Preview != null) DestroyImmediate(m_Preview);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var mode = serializedObject.FindProperty("m_SegmentMode");
            var smoothBends = serializedObject.FindProperty("m_SmoothBends");
            int modeBefore = mode.enumValueIndex;

            // Only show the segment setting that the chosen mode uses.
            var hidden = new List<string>(k_Hidden);
            if (!mode.hasMultipleDifferentValues)
                hidden.Add(modeBefore == (int)RopeSegmentMode.FixedCount ? "m_SegmentLength" : "m_SegmentCount");
            if (!smoothBends.hasMultipleDifferentValues && !smoothBends.boolValue)
                hidden.Add("m_Smoothness");
            DrawPropertiesExcluding(serializedObject, hidden.ToArray());

            // Fixed Count usually means a Gorilla Tag style rope, so switch to straight pieces (and back).
            if (!mode.hasMultipleDifferentValues && mode.enumValueIndex != modeBefore)
                smoothBends.boolValue = mode.enumValueIndex == (int)RopeSegmentMode.Automatic;
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Reset Shape", "Drop the rope straight down again and let it settle.")))
                {
                    foreach (var t in targets)
                        ((Rope)t).ResetShape();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button(new GUIContent("Wake Up", "Start simulating again if the rope went to sleep.")))
                {
                    foreach (var t in targets)
                        ((Rope)t).WakeUp();
                }
            }

            foreach (var t in targets)
            {
                if (!((Rope)t).NeedsRopeMaterial()) continue;
                EditorGUILayout.HelpBox("This rope has no rope material, so it shows up untextured.", MessageType.Warning);
                if (GUILayout.Button("Use Braided Rope Material"))
                {
                    var material = RopeAssets.GetOrCreateDefaultMaterial();
                    foreach (var other in targets)
                    {
                        var renderer = ((Rope)other).GetComponent<MeshRenderer>();
                        Undo.RecordObject(renderer, "Use Rope Material");
                        renderer.sharedMaterial = material;
                    }
                }
                break;
            }

            if (!Application.isPlaying && FindFirstObjectByType<GorillaRopeClimber>(FindObjectsInactive.Include) == null)
            {
                EditorGUILayout.HelpBox("Nothing can grab ropes yet: grabbing needs a Gorilla Rope Climber on your GorillaLocomotion Player, and this scene doesn't have a Player.", MessageType.Warning);
                if (GUILayout.Button("Add Rope Climber to Gorilla Player"))
                {
                    RopeMenu.EnsureClimber(out string message);
                    if (message != null) Debug.Log("Kapi Rope: " + message);
                }
            }

            if (targets.Length == 1 && Application.isPlaying)
            {
                var rope = (Rope)target;
                string state = rope.IsReturning ? "going back to its starting shape" : rope.IsAsleep ? "asleep" : "simulating";
                EditorGUILayout.HelpBox($"{state}, held by {rope.Grips.Count} hand(s)", MessageType.None);
            }

            if (targets.Length == 1 && Mathf.Abs(((Rope)target).transform.lossyScale.x - ((Rope)target).transform.lossyScale.y) > 0.001f)
                EditorGUILayout.HelpBox("Keep this object's scale uniform (or 1), otherwise the rope looks squashed. Use Length and Radius to size it.", MessageType.Warning);

            DrawBraidMaker();
        }

        void DrawBraidMaker()
        {
            EditorGUILayout.Space();
            s_ShowBraidMaker = EditorGUILayout.Foldout(s_ShowBraidMaker, "Braid Texture Maker", true, EditorStyles.foldoutHeader);
            if (!s_ShowBraidMaker) return;

            EditorGUILayout.HelpBox("Pick colors and a weave, then press Make Material. The textures and material are saved in Assets/KapiRope and put on this rope; other ropes can use the same material.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                m_Preset = EditorGUILayout.Popup("Preset", m_Preset, RopeBraidStyle.PresetNames);
                if (GUILayout.Button("Use", GUILayout.Width(50f)))
                {
                    string json = JsonUtility.ToJson(RopeBraidStyle.Preset(m_Preset));
                    foreach (var t in targets)
                    {
                        Undo.RecordObject(t, "Use Rope Preset");
                        JsonUtility.FromJsonOverwrite(json, ((Rope)t).BraidStyle);
                        EditorUtility.SetDirty(t);
                    }
                    serializedObject.Update();
                }
            }

            serializedObject.Update();
            var style = serializedObject.FindProperty("m_BraidStyle");
            var child = style.Copy();
            var end = style.GetEndProperty();
            child.NextVisible(true);
            while (!SerializedProperty.EqualContents(child, end))
            {
                EditorGUILayout.PropertyField(child, true);
                if (!child.NextVisible(false)) break;
            }
            serializedObject.ApplyModifiedProperties();

            var rope = (Rope)target;
            DrawPreview(rope.BraidStyle);

            if (GUILayout.Button("Make Material", GUILayout.Height(26f)))
            {
                string name = JsonUtility.ToJson(rope.BraidStyle) == JsonUtility.ToJson(RopeBraidStyle.Preset(m_Preset))
                    ? $"Rope - {RopeBraidStyle.PresetNames[m_Preset]}"
                    : "Rope - Custom Braid";
                var material = RopeAssets.CreateBraidMaterial(rope.BraidStyle, null, name);
                foreach (var t in targets)
                {
                    var renderer = ((Rope)t).GetComponent<MeshRenderer>();
                    Undo.RecordObject(renderer, "Make Rope Material");
                    renderer.sharedMaterial = material;
                }
                EditorGUIUtility.PingObject(material);
            }
        }

        void DrawPreview(RopeBraidStyle style)
        {
            string key = JsonUtility.ToJson(style);
            if (m_Preview == null || key != m_PreviewKey)
            {
                m_PreviewKey = key;
                var albedo = new Color32[k_PreviewSize * k_PreviewSize];
                var normal = new Color32[k_PreviewSize * k_PreviewSize];
                var small = style.Clone();
                RopeBraidTexture.Generate(small, k_PreviewSize, albedo, normal);
                if (m_Preview == null)
                    m_Preview = new Texture2D(k_PreviewSize, k_PreviewSize, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat };

                // Swap the axes so the rope runs left to right, like a rope lying on the floor.
                var turned = new Color32[albedo.Length];
                for (int y = 0; y < k_PreviewSize; y++)
                for (int x = 0; x < k_PreviewSize; x++)
                    turned[y * k_PreviewSize + x] = albedo[x * k_PreviewSize + y];
                m_Preview.SetPixels32(turned);
                m_Preview.Apply(false);
            }

            Rect rect = GUILayoutUtility.GetRect(10f, 64f, GUILayout.ExpandWidth(true));
            rect = EditorGUI.IndentedRect(rect);
            GUI.DrawTextureWithTexCoords(rect, m_Preview, new Rect(0f, 0f, rect.width / rect.height, 1f));
        }
    }
}
