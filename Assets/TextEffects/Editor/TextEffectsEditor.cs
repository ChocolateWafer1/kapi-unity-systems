using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TextFX.EditorTools
{
    [CustomEditor(typeof(TextEffects))]
    class TextEffectsEditor : Editor
    {
        static readonly GUIContent k_AddEffect = new GUIContent("Add Effect", "Add an effect to the end of the list. Effects run top to bottom and stack.");
        static readonly GUIContent k_Restart = new GUIContent("Restart", "Start every effect's animation over from the beginning.");
        static readonly Color k_HeaderDark = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color k_HeaderLight = new Color(0f, 0f, 0f, 0.08f);
        static readonly Dictionary<(Type, string), MemberInfo> s_Conditions = new Dictionary<(Type, string), MemberInfo>();

        SerializedProperty m_PreviewInEditor;
        SerializedProperty m_UseUnscaledTime;
        SerializedProperty m_Speed;
        SerializedProperty m_Effects;

        void OnEnable()
        {
            m_PreviewInEditor = serializedObject.FindProperty("m_PreviewInEditor");
            m_UseUnscaledTime = serializedObject.FindProperty("m_UseUnscaledTime");
            m_Speed = serializedObject.FindProperty("m_Speed");
            m_Effects = serializedObject.FindProperty("m_Effects");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_PreviewInEditor);
            EditorGUILayout.PropertyField(m_UseUnscaledTime);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(m_Speed);
                if (GUILayout.Button(k_Restart, EditorStyles.miniButton, GUILayout.Width(60f)))
                    foreach (var t in targets)
                        ((TextEffects)t).Restart();
            }

            EditorGUILayout.Space();

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox("Select a single object to edit its effects.", MessageType.Info);
            }
            else
            {
                var effects = ((TextEffects)target).effects;
                for (int i = 0; i < m_Effects.arraySize; i++)
                    DrawEffect(i, i < effects.Count ? effects[i] : null);

                if (m_Effects.arraySize == 0)
                    EditorGUILayout.HelpBox("No effects yet. Add one below. Effects stack, running top to bottom.", MessageType.None);

                EditorGUILayout.Space(4f);
                DrawAddButton();
            }

            serializedObject.ApplyModifiedProperties();
        }

        void DrawEffect(int index, TextEffect effect)
        {
            var element = m_Effects.GetArrayElementAtIndex(index);
            var enabled = effect != null ? element.FindPropertyRelative("enabled") : null;

            Rect header = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, EditorGUIUtility.isProSkin ? k_HeaderDark : k_HeaderLight);

            var foldoutRect = new Rect(header.x + 4f, header.y + 3f, 14f, 16f);
            var toggleRect = new Rect(foldoutRect.xMax, header.y + 3f, 16f, 16f);
            var menuRect = new Rect(header.xMax - 22f, header.y + 3f, 18f, 16f);
            var labelRect = new Rect(toggleRect.xMax + 4f, header.y + 2f, menuRect.x - toggleRect.xMax - 8f, 18f);

            element.isExpanded = GUI.Toggle(foldoutRect, element.isExpanded, GUIContent.none, EditorStyles.foldout);
            if (enabled != null) enabled.boolValue = GUI.Toggle(toggleRect, enabled.boolValue, GUIContent.none);

            string title = effect != null ? GetDisplayName(effect.GetType()) : "Missing Effect";
            using (new EditorGUI.DisabledScope(enabled != null && !enabled.boolValue))
                EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);

            if (GUI.Button(menuRect, EditorGUIUtility.TrIconContent("_Menu", "Effect options"), GUIStyle.none))
                ShowEffectMenu(index, effect, menuRect);

            var e = Event.current;
            if (e.type == EventType.MouseDown && header.Contains(e.mousePosition))
            {
                if (e.button == 1)
                {
                    ShowEffectMenu(index, effect, new Rect(e.mousePosition, Vector2.zero));
                    e.Use();
                }
                else if (e.button == 0 && labelRect.Contains(e.mousePosition))
                {
                    element.isExpanded = !element.isExpanded;
                    e.Use();
                }
            }

            if (effect == null)
            {
                EditorGUILayout.HelpBox("This effect's script is missing or failed to compile. Remove it with the menu on the right.", MessageType.Warning);
            }
            else if (element.isExpanded)
            {
                EditorGUILayout.Space(2f);
                EditorGUI.indentLevel++;

                var property = element.Copy();
                var end = element.GetEndProperty();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren) && !SerializedProperty.EqualContents(property, end))
                {
                    enterChildren = false;
                    if (IsShown(effect, property.name))
                        EditorGUILayout.PropertyField(property, true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4f);
        }

        void DrawAddButton()
        {
            Rect row = GUILayoutUtility.GetRect(k_AddEffect, GUI.skin.button, GUILayout.Height(24f));
            float width = Mathf.Min(230f, row.width);
            var button = new Rect(row.center.x - width * 0.5f, row.y, width, row.height);

            if (!EditorGUI.DropdownButton(button, k_AddEffect, FocusType.Keyboard)) return;

            var menu = new GenericMenu();
            foreach (var type in GetEffectTypes())
                menu.AddItem(new GUIContent(GetMenuPath(type)), false, () => AddEffect(type));
            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("No effects found"));
            menu.DropDown(button);
        }

        void ShowEffectMenu(int index, TextEffect effect, Rect rect)
        {
            int count = m_Effects.arraySize;
            var menu = new GenericMenu();

            AddItem(menu, "Move Up", index > 0, effects => effects.MoveArrayElement(index, index - 1));
            AddItem(menu, "Move Down", index < count - 1, effects => effects.MoveArrayElement(index, index + 1));
            menu.AddSeparator("");
            AddItem(menu, "Reset", effect != null, effects =>
                effects.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(effect.GetType()));
            AddItem(menu, "Duplicate", effect != null, effects =>
            {
                var copy = Activator.CreateInstance(effect.GetType());
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(effect), copy);
                effects.InsertArrayElementAtIndex(index);
                effects.GetArrayElementAtIndex(index + 1).managedReferenceValue = copy;
            });
            menu.AddSeparator("");
            AddItem(menu, "Remove", true, effects => effects.DeleteArrayElementAtIndex(index));

            menu.DropDown(rect);
        }

        void AddItem(GenericMenu menu, string label, bool enabled, Action<SerializedProperty> change)
        {
            if (!enabled)
            {
                menu.AddDisabledItem(new GUIContent(label));
                return;
            }

            // Menu callbacks run after this inspector pass, so re-sync before changing the list.
            menu.AddItem(new GUIContent(label), false, () =>
            {
                serializedObject.Update();
                change(m_Effects);
                serializedObject.ApplyModifiedProperties();
            });
        }

        void AddEffect(Type type)
        {
            serializedObject.Update();
            int index = m_Effects.arraySize;
            m_Effects.arraySize++;
            var element = m_Effects.GetArrayElementAtIndex(index);
            element.managedReferenceValue = Activator.CreateInstance(type);
            element.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        static IEnumerable<Type> GetEffectTypes()
        {
            return TypeCache.GetTypesDerivedFrom<TextEffect>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(GetMenuPath);
        }

        static string GetMenuPath(Type type)
        {
            var menu = type.GetCustomAttribute<TextEffectMenuAttribute>();
            return menu != null ? menu.path : ObjectNames.NicifyVariableName(type.Name);
        }

        static string GetDisplayName(Type type)
        {
            string path = GetMenuPath(type);
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        static bool IsShown(TextEffect effect, string fieldName)
        {
            var key = (effect.GetType(), fieldName);
            if (!s_Conditions.TryGetValue(key, out var condition))
            {
                condition = FindCondition(key.Item1, fieldName);
                s_Conditions[key] = condition;
            }

            switch (condition)
            {
                case FieldInfo field: return field.GetValue(effect) is bool fieldValue && fieldValue;
                case PropertyInfo property: return property.GetValue(effect) is bool propertyValue && propertyValue;
                case MethodInfo method: return method.Invoke(effect, null) is bool methodValue && methodValue;
                default: return true;
            }
        }

        static MemberInfo FindCondition(Type type, string fieldName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            ShowIfAttribute showIf = null;
            for (var t = type; t != null && showIf == null; t = t.BaseType)
            {
                var field = t.GetField(fieldName, flags);
                if (field != null) showIf = field.GetCustomAttribute<ShowIfAttribute>();
            }
            if (showIf == null) return null;

            for (var t = type; t != null; t = t.BaseType)
            {
                MemberInfo member = t.GetField(showIf.condition, flags);
                if (member == null) member = t.GetProperty(showIf.condition, flags);
                if (member == null) member = t.GetMethod(showIf.condition, flags, null, Type.EmptyTypes, null);
                if (member != null) return member;
            }

            Debug.LogWarning($"[ShowIf] {type.Name}.{fieldName}: no bool member named '{showIf.condition}'.");
            return null;
        }
    }
}
