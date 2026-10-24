// Simple, cheap hand look: soft shading with a glowing rim. Unlit, so it looks the same in any lighting.
Shader "KapiVR/Hand"
{
    Properties
    {
        _Color ("Color", Color) = (0.93, 0.95, 1, 1)
        [HDR] _RimColor ("Rim Color", Color) = (1, 0.35, 0.8, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Glow ("Glow", Range(0, 3)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Hand"
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = pow(1.0h - saturate(dot(normal, view)), _RimPower);
                half light = 0.6h + 0.4h * saturate(dot(normal, normalize(half3(0.3h, 1.0h, -0.25h))));

                half3 rgb = _Color.rgb * light * (1.0h + _Glow) + _RimColor.rgb * rim * (1.0h + _Glow);
                return half4(rgb, 1.0h);
            }
            ENDHLSL
        }
    }
}
