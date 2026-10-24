// Full-view distortion: liquid waves, a twist around the middle of the view, and a color split toward the edges.
// _Strength goes from 1 (fully distorted) to 0 (clean). Lives in Resources so it's always included in builds.
Shader "Hidden/KapiVR/ScreenDistortion"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 1
        _DistortionTime ("Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "ScreenDistortion"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Provides the full-screen Vert, Varyings and _BlitTexture, with VR (stereo) support.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Strength;
            float _DistortionTime;

            half3 SampleSplit(float2 uv, float2 split)
            {
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + split).r;
                half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - split).b;
                return half3(r, g, b);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float s = _Strength;
                if (s <= 0.0001)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float t = _DistortionTime;
                float2 p = uv - 0.5;
                float radius = length(p);

                // Twist around the middle of the view, rocking back and forth.
                float twist = s * 2.2 * (1.0 - smoothstep(0.0, 0.7, radius)) * sin(t * 1.7 + 0.6);
                float twistSin, twistCos;
                sincos(twist, twistSin, twistCos);
                p = float2(p.x * twistCos - p.y * twistSin, p.x * twistSin + p.y * twistCos);

                // Layered waves bend the picture like it's seen through moving water.
                float2 wave;
                wave.x = sin(p.y * 11.0 + t * 3.0) * 0.6 + sin(p.y * 27.0 - t * 5.0) * 0.25 + sin((p.x + p.y) * 17.0 + t * 2.0) * 0.15;
                wave.y = sin(p.x * 10.0 - t * 2.6) * 0.6 + sin(p.x * 23.0 + t * 4.4) * 0.25 + sin((p.x - p.y) * 15.0 - t * 3.2) * 0.15;
                p += wave * 0.07 * s;

                // Zoom in a little while distorted so the bent edges don't smear.
                p *= 1.0 - 0.18 * s;

                // Split the colors outward from the center, more toward the edges.
                float2 split = (p / max(radius, 0.0001)) * 0.025 * s * (0.4 + radius);
                return half4(SampleSplit(p + 0.5, split), 1.0);
            }
            ENDHLSL
        }
    }
}
