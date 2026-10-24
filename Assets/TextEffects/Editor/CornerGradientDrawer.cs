using TMPro;
using UnityEditor;
using UnityEngine;

namespace TextFX.EditorTools
{
    /// <summary>Draws a <see cref="CornerGradient"/> with the same layout as TextMeshPro's Color Gradient.</summary>
    [CustomPropertyDrawer(typeof(CornerGradient))]
    class CornerGradientDrawer : PropertyDrawer
    {
        static readonly GUIContent k_Preset = new GUIContent("Color Preset", "Optional TextMeshPro Color Gradient asset. When set, its mode and colors are used, and editing them here edits the asset.");
        static readonly GUIContent k_Mode = new GUIContent("Color Mode", "The type of gradient, same as TextMeshPro's.");
        static readonly GUIContent k_Colors = new GUIContent("Colors");
        const float k_Gap = 2f;

        SerializedObject m_PresetObject;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var preset = property.FindPropertyRelative("preset").objectReferenceValue as TMP_ColorGradient;
            var mode = preset != null ? preset.colorMode : (ColorMode)property.FindPropertyRelative("colorMode").enumValueIndex;
            int colorRows = mode == ColorMode.VerticalGradient || mode == ColorMode.FourCornersGradient ? 2 : 1;
            return (2 + colorRows) * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) - EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var presetProperty = property.FindPropertyRelative("preset");
            EditorGUI.PropertyField(row, presetProperty, k_Preset);
            NextRow(ref row);

            var preset = presetProperty.objectReferenceValue as TMP_ColorGradient;
            if (preset != null)
            {
                if (m_PresetObject == null || m_PresetObject.targetObject != preset)
                    m_PresetObject = new SerializedObject(preset);
                m_PresetObject.Update();

                DrawColors(row, m_PresetObject.FindProperty("colorMode"), m_PresetObject.FindProperty("topLeft"),
                    m_PresetObject.FindProperty("topRight"), m_PresetObject.FindProperty("bottomLeft"), m_PresetObject.FindProperty("bottomRight"));

                m_PresetObject.ApplyModifiedProperties();
            }
            else
            {
                DrawColors(row, property.FindPropertyRelative("colorMode"), property.FindPropertyRelative("topLeft"),
                    property.FindPropertyRelative("topRight"), property.FindPropertyRelative("bottomLeft"), property.FindPropertyRelative("bottomRight"));
            }

            EditorGUI.EndProperty();
        }

        // Mirrors TMP_ColorGradientEditor, including how it keeps unused corners in sync.
        static void DrawColors(Rect row, SerializedProperty mode, SerializedProperty topLeft, SerializedProperty topRight,
            SerializedProperty bottomLeft, SerializedProperty bottomRight)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(row, mode, k_Mode);
            if (EditorGUI.EndChangeCheck())
            {
                switch ((ColorMode)mode.enumValueIndex)
                {
                    case ColorMode.Single:
                        topRight.colorValue = topLeft.colorValue;
                        bottomLeft.colorValue = topLeft.colorValue;
                        bottomRight.colorValue = topLeft.colorValue;
                        break;
                    case ColorMode.HorizontalGradient:
                        bottomLeft.colorValue = topLeft.colorValue;
                        bottomRight.colorValue = topRight.colorValue;
                        break;
                    case ColorMode.VerticalGradient:
                        topRight.colorValue = topLeft.colorValue;
                        bottomRight.colorValue = bottomLeft.colorValue;
                        break;
                }
            }
            NextRow(ref row);

            Rect fields = EditorGUI.PrefixLabel(row, k_Colors);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var full = fields;
            var left = new Rect(fields.x, fields.y, (fields.width - k_Gap) * 0.5f, fields.height);
            var right = new Rect(left.xMax + k_Gap, fields.y, left.width, fields.height);

            switch ((ColorMode)mode.enumValueIndex)
            {
                case ColorMode.Single:
                    if (ColorField(full, topLeft))
                    {
                        topRight.colorValue = topLeft.colorValue;
                        bottomLeft.colorValue = topLeft.colorValue;
                        bottomRight.colorValue = topLeft.colorValue;
                    }
                    break;

                case ColorMode.HorizontalGradient:
                    if (ColorField(left, topLeft)) bottomLeft.colorValue = topLeft.colorValue;
                    if (ColorField(right, topRight)) bottomRight.colorValue = topRight.colorValue;
                    break;

                case ColorMode.VerticalGradient:
                    if (ColorField(full, topLeft)) topRight.colorValue = topLeft.colorValue;
                    NextRow(ref full);
                    if (ColorField(full, bottomLeft)) bottomRight.colorValue = bottomLeft.colorValue;
                    break;

                case ColorMode.FourCornersGradient:
                    ColorField(left, topLeft);
                    ColorField(right, topRight);
                    NextRow(ref left);
                    NextRow(ref right);
                    ColorField(left, bottomLeft);
                    ColorField(right, bottomRight);
                    break;
            }

            EditorGUI.indentLevel = indent;
        }

        static bool ColorField(Rect rect, SerializedProperty color)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, color, GUIContent.none);
            return EditorGUI.EndChangeCheck();
        }

        static void NextRow(ref Rect row) => row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
    }
}
