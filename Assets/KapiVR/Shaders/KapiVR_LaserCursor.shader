// The dot where the laser lands: a bright center, a soft halo, and a ring that can appear and close in.
Shader "KapiVR/Laser Cursor"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.06, 0.04, 1)
        [HDR] _CoreColor ("Core Color", Color) = (1, 0.95, 0.92, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.3
        _Ring ("Ring Visibility", Range(0, 1)) = 0
        _RingRadius ("Ring Radius", Range(0, 1)) = 0.95
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "LaserCursor"
            // Premultiplied: the center dot is solid, the halo and ring add light on top.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                half _Intensity;
                half _Ring;
                half _RingRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half radius = length(input.uv * 2.0 - 1.0);
                half center = 1.0h - smoothstep(0.16h, 0.28h, radius);
                half halo = saturate(1.0h - radius);
                halo = halo * halo * 0.55h;
                half ring = (1.0h - smoothstep(0.0h, 0.07h, abs(radius - _RingRadius))) * _Ring;

                half glow = halo * 1.6h + ring;
                half3 rgb = (_CoreColor.rgb * center + _Color.rgb * glow) * _Intensity * _Color.a;
                half alpha = saturate(center + glow * 0.3h) * _Color.a;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
