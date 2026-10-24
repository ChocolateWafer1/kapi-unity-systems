using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KapiWindowGlass
{
    /// <summary>
    /// Material inspector for Kapi/Window Glass: settings grouped into sections, extras that only show their
    /// options when switched on, and a one-click fix if URP's Opaque Texture (needed to see through) is off.
    /// </summary>
    sealed class WindowGlassShaderGUI : ShaderGUI
    {
        MaterialEditor m_Editor;
        MaterialProperty[] m_Properties;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            m_Editor = materialEditor;
            m_Properties = properties;

            DrawOpaqueTextureCheck();

            // Only the Kapi/Mirror shader has these.
            if (FindProperty("_MirrorStrength", m_Properties, false) != null)
            {
                if (BeginSection("Mirror"))
                {
                    EditorGUILayout.HelpBox("Reflects everything around it in Play mode, from each eye in VR. Outside Play mode it shows the nearest reflection probe. Add a Mirror component to the object for quality settings.", MessageType.None);
                    Property("_MirrorStrength", "Mirror Reflection", "How strong the mirror reflection is. 1 is a full mirror, lower lets you see through it like glass, 0 is plain glass.");
                    Property("_MirrorTint", "Mirror Tint", "Color of the reflection, for tinted or antique mirrors.");
                    Property("_MirrorBrightness", "Mirror Brightness", "Darken or brighten the reflection.");
                }
                EndSection();
            }

            if (BeginSection("Glass"))
            {
                Property("_Pattern", "Pattern", "Rippled: flowing bumps like rain glass. Hammered: round dimples. Reeded: ribs that stretch the view into stripes. Clear: almost flat with a slight wobble. Flat: perfectly smooth.");
                Property("_PatternScale", "Pattern Size", "How big the bumps, dimples or ribs are. In meters when Size In Meters is on.");
                Property("_WorldScale", "Size In Meters", "On: the pattern keeps the same real size however the window is scaled. Off: it follows the mesh's UVs.");
                Property("_PatternRotation", "Pattern Rotation", "Turns the pattern, for example to make reeded ribs run sideways.");
                Property("_PatternSeed", "Pattern Variation", "Change this so neighboring windows don't look identical.");
                Property("_Bumpiness", "Bumpiness", "How strongly the pattern tilts the surface, for both reflections and distortion.");
                Property("_Refraction", "Distortion", "How much the view through the glass bends.");
            }
            EndSection();

            if (BeginSection("Color"))
            {
                Property("_Tint", "Tint", "Color of the glass.");
                Property("_Darkness", "Darkness", "How much light the glass absorbs.");
                Property("_EdgeStrength", "Thick Edges", "Looking through the glass at a steep angle makes it greener and darker, like real thick glass.");
                if (Value("_EdgeStrength") > 0f)
                {
                    EditorGUI.indentLevel++;
                    Property("_EdgeColor", "Edge Color", "The color the glass takes on at steep angles.");
                    Property("_EdgePower", "Edge Falloff", "Higher keeps the edge color closer to the very steepest angles.");
                    EditorGUI.indentLevel--;
                }
            }
            EndSection();

            if (BeginSection("Gloss & Reflections"))
            {
                Property("_Smoothness", "Gloss", "Sharpness of reflections and highlights.");
                Property("_Reflectivity", "Reflections", "Strength of the sky and surroundings reflected in the glass.");
                Property("_FresnelPower", "Reflection Falloff", "Higher means reflections mostly show at steep angles, like real glass.");
                Property("_HighlightStrength", "Sun Highlights", "Bright glints of the sun on the bumps.");
            }
            EndSection();

            if (BeginSection("Extras"))
            {
                Extra("_Frost", "Frosted Blur", "Blurs what's behind the glass.", () =>
                    Property("_FrostAmount", "Blur Amount", "From a light haze to fully frosted."));

                Extra("_Dirt", "Dirt & Grime", "Patchy grime and dried streaks that dull the gloss.", () =>
                {
                    Property("_DirtAmount", "Amount", "How much of the glass is dirty.");
                    Property("_DirtColor", "Color", "Color of the dirt. Alpha is how opaque it is.");
                    Property("_DirtScale", "Size", "Size of the dirty patches, in meters.");
                    Property("_Streaks", "Streaks", "Mix in long dried drips running down the glass.");
                });

                Extra("_Smudges", "Smudges & Fingerprints", "Faint wiped smears and oval fingerprints that catch the light.", () =>
                {
                    Property("_SmudgeAmount", "Smudges", "How much of the surface is smeared.");
                    Property("_FingerprintAmount", "Fingerprints", "How many fingerprints there are.");
                    Property("_SmudgeScale", "Size", "Size of the smears, in meters. Fingerprints scale with it.");
                });

                Extra("_EdgeFog", "Foggy Edges", "Steam gathers around the edges, blurring and whitening them like a bathroom mirror.", () =>
                {
                    Property("_EdgeFogAmount", "Amount", "How thick the fog is.");
                    Property("_EdgeFogWidth", "Width", "How far in from the edges the fog reaches, as a fraction of the surface.");
                    Property("_EdgeFogColor", "Color", "Color of the fog. Alpha is how opaque it is.");
                });

                Extra("_Rain", "Raindrops", "Drops resting on the glass and sliding down, each one bending the view.", () =>
                {
                    Property("_RainAmount", "Amount", "How many drops there are.");
                    Property("_DropSize", "Drop Size", "Size of the drops.");
                    Property("_RainSpeed", "Slide Speed", "How fast drops slide down. 0 freezes them.");
                    Property("_RainDistortion", "Drop Distortion", "How strongly each drop bends the view, like a tiny lens.");
                });
            }
            EndSection();

            if (BeginSection("Advanced", false))
            {
                Property("_Cull", "Render Face", "Both lets the window be seen from either side.");
                m_Editor.RenderQueueField();
                m_Editor.EnableInstancingField();
            }
            EndSection();
        }

        bool BeginSection(string title, bool openByDefault = true)
        {
            string key = "KapiWindowGlass.Section." + title;
            bool open = SessionState.GetBool(key, openByDefault);
            EditorGUILayout.Space(2f);
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            SessionState.SetBool(key, open);
            if (open) EditorGUI.indentLevel++;
            return open;
        }

        void EndSection()
        {
            if (EditorGUI.indentLevel > 0) EditorGUI.indentLevel--;
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void Extra(string toggle, string label, string tooltip, System.Action drawOptions)
        {
            Property(toggle, label, tooltip);
            if (Value(toggle) < 0.5f) return;

            EditorGUI.indentLevel++;
            drawOptions();
            EditorGUI.indentLevel--;
        }

        void Property(string name, string label, string tooltip)
        {
            var property = FindProperty(name, m_Properties, false);
            if (property != null) m_Editor.ShaderProperty(property, new GUIContent(label, tooltip));
        }

        float Value(string name)
        {
            var property = FindProperty(name, m_Properties, false);
            return property != null ? property.floatValue : 0f;
        }

        // Seeing through the glass needs the picture of the scene behind it, which URP only makes with Opaque Texture on.
        static void DrawOpaqueTextureCheck()
        {
            var missing = new List<UniversalRenderPipelineAsset>();
            for (int i = 0; i < QualitySettings.count; i++)
                Collect(QualitySettings.GetRenderPipelineAssetAt(i), missing);
            Collect(GraphicsSettings.defaultRenderPipeline, missing);
            if (missing.Count == 0) return;

            EditorGUILayout.HelpBox(
                "You can't see through the glass yet: Opaque Texture is off in " + string.Join(", ", missing.Select(a => a.name)) + ".",
                MessageType.Warning);

            if (GUILayout.Button("Turn On Opaque Texture"))
            {
                foreach (var asset in missing)
                {
                    Undo.RecordObject(asset, "Turn On Opaque Texture");
                    asset.supportsCameraOpaqueTexture = true;
                    EditorUtility.SetDirty(asset);
                }
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.Space(4f);
        }

        static void Collect(RenderPipelineAsset asset, List<UniversalRenderPipelineAsset> missing)
        {
            if (asset is UniversalRenderPipelineAsset urp && !urp.supportsCameraOpaqueTexture && !missing.Contains(urp))
                missing.Add(urp);
        }
    }
}
