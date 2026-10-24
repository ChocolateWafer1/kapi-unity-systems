// A laser beam for LineRenderers that stays readable on any background: a solid, fully saturated beam with a thin
// dark outline. Colors are kept at or below white so bloom and tonemapping can't wash the beam out into a pale haze
// against a bright sky. The line's vertex colors tint it and fade it in at the hand.
Shader "KapiVR/Laser"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _EdgeColor ("Outline Color", Color) = (0.12, 0, 0, 1)
        _EdgeWidth ("Outline Width", Range(0, 0.8)) = 0.35
        _Intensity ("Intensity", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Laser"
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
                half4 _EdgeColor;
                half _EdgeWidth;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 0 along the middle of the beam, 1 at its edges.
                half across = abs(input.uv.y * 2.0h - 1.0h);

                // Solid all the way across, softened only at the very edge to avoid jagged pixels.
                half body = 1.0h - smoothstep(0.85h, 1.0h, across);

                // The colored middle, inside a dark outline.
                half colorEdge = 1.0h - _EdgeWidth;
                half inner = 1.0h - smoothstep(colorEdge - 0.12h, colorEdge, across);

                half3 beam = saturate(_Color.rgb * input.color.rgb * _Intensity);
                half3 rgb = lerp(_EdgeColor.rgb, beam, inner);

                half alpha = body * input.color.a * _Color.a;
                return half4(rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
