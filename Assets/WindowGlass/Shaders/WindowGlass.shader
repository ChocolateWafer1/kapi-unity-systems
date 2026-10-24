// Glossy window glass for URP. Bends whatever is behind it through a textured glass pattern, reflects the sky with
// a fresnel falloff, catches sun glints, and can add frost, dirt, raindrops, fingerprints and steamed-up edges.
// The shading itself lives in GlassCore.hlsl, shared with Kapi/Mirror.
//
// Needs "Opaque Texture" turned on in the URP asset to see through it. Objects that are themselves see-through
// (UI, other glass) don't show up behind it, because they're drawn after the picture behind the glass is taken.
Shader "Kapi/Window Glass"
{
    Properties
    {
        [KeywordEnum(Rippled, Hammered, Reeded, Clear, Flat)] _Pattern ("Pattern", Float) = 0
        _PatternScale ("Pattern Size", Range(0.01, 2)) = 0.2
        [ToggleUI] _WorldScale ("Size In Meters", Float) = 1
        _PatternRotation ("Pattern Rotation", Range(0, 360)) = 0
        _PatternSeed ("Pattern Variation", Range(0, 100)) = 0
        _Bumpiness ("Bumpiness", Range(0, 2)) = 1
        _Refraction ("Distortion", Range(0, 0.3)) = 0.06

        _Tint ("Tint", Color) = (0.93, 0.97, 1, 1)
        _Darkness ("Darkness", Range(0, 1)) = 0.05
        _EdgeColor ("Edge Color", Color) = (0.3, 0.55, 0.45, 1)
        _EdgeStrength ("Thick Edges", Range(0, 1)) = 0.5
        _EdgePower ("Edge Falloff", Range(0.5, 8)) = 3

        _Smoothness ("Gloss", Range(0, 1)) = 0.95
        _Reflectivity ("Reflections", Range(0, 2)) = 1
        _FresnelPower ("Reflection Falloff", Range(0.5, 10)) = 4
        _HighlightStrength ("Sun Highlights", Range(0, 4)) = 1

        [Toggle(_FROST_ON)] _Frost ("Frosted Blur", Float) = 0
        _FrostAmount ("Blur Amount", Range(0, 1)) = 0.3

        [Toggle(_DIRT_ON)] _Dirt ("Dirt & Grime", Float) = 0
        _DirtAmount ("Dirt Amount", Range(0, 1)) = 0.4
        _DirtColor ("Dirt Color", Color) = (0.45, 0.4, 0.33, 1)
        _DirtScale ("Dirt Size", Range(0.05, 4)) = 0.6
        _Streaks ("Streaks", Range(0, 1)) = 0.5

        [Toggle(_SMUDGES_ON)] _Smudges ("Smudges & Fingerprints", Float) = 0
        _SmudgeAmount ("Smudge Amount", Range(0, 1)) = 0.4
        _FingerprintAmount ("Fingerprints", Range(0, 1)) = 0.5
        _SmudgeScale ("Smudge Size", Range(0.05, 2)) = 0.3

        [Toggle(_EDGEFOG_ON)] _EdgeFog ("Foggy Edges", Float) = 0
        _EdgeFogAmount ("Fog Amount", Range(0, 1)) = 0.7
        _EdgeFogWidth ("Fog Width", Range(0.01, 0.5)) = 0.15
        _EdgeFogColor ("Fog Color", Color) = (0.9, 0.93, 0.95, 1)

        [Toggle(_RAIN_ON)] _Rain ("Raindrops", Float) = 0
        _RainAmount ("Rain Amount", Range(0, 1)) = 0.6
        _DropSize ("Drop Size", Range(0.2, 3)) = 1
        _RainSpeed ("Slide Speed", Range(0, 3)) = 1
        _RainDistortion ("Drop Distortion", Range(0, 1)) = 0.6

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Render Face", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WindowGlass"
            Tags { "LightMode" = "UniversalForward" }

            // The glass outputs the finished picture of what's behind it, so no blending is needed.
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _PATTERN_RIPPLED _PATTERN_HAMMERED _PATTERN_REEDED _PATTERN_CLEAR _PATTERN_FLAT
            #pragma shader_feature_local _FROST_ON
            #pragma shader_feature_local _DIRT_ON
            #pragma shader_feature_local _SMUDGES_ON
            #pragma shader_feature_local _EDGEFOG_ON
            #pragma shader_feature_local _RAIN_ON

            #include "GlassCore.hlsl"

            Varyings Vert(Attributes input)
            {
                return GlassVertex(input);
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return GlassFragment(input, IS_FRONT_VFACE(face, 1.0, -1.0));
            }
            ENDHLSL
        }
    }

    CustomEditor "KapiWindowGlass.WindowGlassShaderGUI"
}
