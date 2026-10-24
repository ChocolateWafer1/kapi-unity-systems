// Crisp pixel-art particles (bubbles, specks, splash droplets). Point-sampled, hard-edged pixels that fade out with a
// retro dither instead of going see-through. They draw with the solid geometry, so underwater fog, refraction and
// the depth texture all treat them like real objects. Lives in Resources so it's always included in builds.
Shader "Hidden/KapiWater/PixelParticle"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Lit ("Scene Lighting", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        half _Lit;
    CBUFFER_END

    TEXTURE2D(_MainTex);

    struct Attributes
    {
        float4 positionOS : POSITION;
        half4 color : COLOR;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        half4 color : TEXCOORD1;
        half fogFactor : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        output.color = input.color;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    // 4x4 ordered dither: fading pixels drop out in a pattern, like old consoles did it.
    half Bayer4(float2 pixel)
    {
        uint2 p = (uint2)pixel & 3u;
        uint index = p.y * 4u + p.x;
        const half matrix4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        return (matrix4[index] + 0.5h) / 16.0h;
    }

    half4 SampleSprite(Varyings input)
    {
        half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, input.uv) * input.color;
        clip(sprite.a - Bayer4(input.positionCS.xy));
        return sprite;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Pixel Particle"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sprite = SampleSprite(input);
                Light mainLight = GetMainLight();
                half3 light = SampleSH(half3(0, 1, 0)) + mainLight.color * saturate(mainLight.direction.y);
                half3 rgb = sprite.rgb * lerp(1.0h, light, _Lit);
                return half4(MixFog(rgb, input.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            half FragDepth(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SampleSprite(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
