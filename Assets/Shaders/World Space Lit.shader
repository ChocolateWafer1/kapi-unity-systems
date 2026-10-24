// URP Lit, except the textures are laid out in world space instead of by the mesh's UVs. A floor built from many
// objects or faces, at any scale, shows one continuous pattern with nothing breaking where they meet, and the
// texture keeps its real size. Surfaces facing up or down get it from above; walls get it from the side.
// Base Map tiling is how many times the texture repeats per meter.
Shader "Kapi/World Space Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (Tiling = repeats per meter)", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Normal][NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [Toggle(_KAPI_ALONG_X)] _AlongX("Turn 90 Degrees On Floors", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Render Face", Float) = 2

        // Read by URP's Lit code this shader builds on.
        [HideInInspector] _OcclusionStrength("Occlusion Strength", Float) = 1.0
        [HideInInspector] _Cutoff("Alpha Cutoff", Float) = 0.5
        [HideInInspector] _Surface("Surface", Float) = 0.0
        [HideInInspector] _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        [HideInInspector] _EmissionColor("Emission", Color) = (0, 0, 0)
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment WorldSpaceLitFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _KAPI_ALONG_X

            // Same pipeline and engine keywords as URP Lit, so lighting, shadows, fog and lightmaps all match it.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            // Projects the texture onto the world axis the surface faces most: from above for floors and ceilings,
            // from the side for walls. Also gives the normal map a matching tangent frame.
            void WorldSpaceUV(float3 positionWS, half3 normalWS, out float2 uv, out half4 tangentWS)
            {
                float3 facing = abs(normalWS);
                float3 across, along;
                if (facing.y >= facing.x && facing.y >= facing.z)
                {
                #if defined(_KAPI_ALONG_X)
                    across = float3(0, 0, -1); along = float3(1, 0, 0);
                #else
                    across = float3(1, 0, 0); along = float3(0, 0, 1);
                #endif
                }
                else if (facing.x >= facing.z)
                {
                    across = float3(0, 0, 1); along = float3(0, 1, 0);
                }
                else
                {
                    across = float3(1, 0, 0); along = float3(0, 1, 0);
                }

                uv = float2(dot(positionWS, across), dot(positionWS, along)) * _BaseMap_ST.xy + _BaseMap_ST.zw;

                half3 tangent = normalize(across - normalWS * dot(normalWS, across));
                half sign = dot(cross(normalWS, tangent), along) >= 0.0 ? 1.0 : -1.0;
                tangentWS = half4(tangent, sign);
            }

            void WorldSpaceLitFragment(
                Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float2 uv;
                half4 tangentWS;
                WorldSpaceUV(input.positionWS, normalize(input.normalWS), uv, tangentWS);
                input.uv = uv;
            #if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
                input.tangentWS = tangentWS;
            #endif

                LitPassFragment(input, outColor
            #ifdef _WRITE_RENDERING_LAYERS
                    , outRenderingLayers
            #endif
                );
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // Surface normals for effects like ambient occlusion. Uses the mesh's own normals.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }

        // Colors for baked lighting.
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
