// What you see with your head underwater: everything fades into the water color with distance (reds first, like
// real water), the view wobbles, caustic light dances over the floor and walls, it gets darker the deeper you go,
// and the edges darken. When your head is half in, only the part of each eye below the waves gets the effect, with a
// thin dark waterline between.
// It's drawn on a quad just in front of the camera, like an ordinary object, so it shows in both eyes in every VR
// mode (multi-pass, single-pass, Quest). It draws before the water surface and particles, which add their own fog.
// Lives in Resources so it's always included in builds.
Shader "Hidden/KapiWater/Underwater"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Underwater"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha // Premultiplied: above-water pixels keep what's already there.

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ _KAPI_CAUSTICS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/KapiWater/Shaders/KapiWaterCommon.hlsl"

            float4x4 _WaterWorldToBox;
            float3 _WaterExtents;       // half width, depth, half length (box space)
            float _WaterRestHeight;
            float4 _LensSurface;        // height of the waves at the head, slope along X, slope along Z, unused
            float2 _LensCenter;         // head XZ

            half4 _UnderwaterColor;
            float _UnderwaterVisibility;
            half _LightInfluence;
            half _ColorAbsorption;
            float _DarkenWithDepth;
            half _Distortion;
            half _DistortionSpeed;
            half _DistortionDetail;
            half _Vignette;
            half _WaterlineThickness;
            half _WaterlineDarkness;

            TEXTURE2D(_CausticsTexture);
            SAMPLER(sampler_CausticsTexture);
            half4 _CausticsColor;
            half _CausticsStrength;
            float _CausticsSize;
            float _CausticsSpeed;
            float _CausticsSplit;
            float _CausticsDepth;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float3 WorldFromDepth(float2 uv, float rawDepth)
            {
            #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif
                return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            }

            // How far along a ray from inside the box before it leaves the water.
            float DistanceToLeaveWater(float3 originWS, float3 directionWS)
            {
                float3 o = mul(_WaterWorldToBox, float4(originWS, 1.0)).xyz;
                float3 d = mul((float3x3)_WaterWorldToBox, directionWS); // Not normalized, so distances stay in meters.
                d = d + (abs(d) < 1e-6) * 1e-6;
                float3 lo = float3(-_WaterExtents.x, -_WaterExtents.y, -_WaterExtents.z);
                float3 hi = float3(_WaterExtents.x, 0.0, _WaterExtents.z);
                float3 t0 = (lo - o) / d;
                float3 t1 = (hi - o) / d;
                float3 far = max(t0, t1);
                return max(0.0, min(min(far.x, far.y), far.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 cameraWS = GetCameraPositionWS();
                float time = _Time.y;

                // Is this spot of the overlay (right in front of your eye) under the waves? Over something this
                // small the surface is a flat plane, worked out on the CPU at your head.
                float3 lensWS = input.positionWS;
                float surfaceY = _LensSurface.x + dot(_LensSurface.yz, lensWS.xz - _LensCenter);
                float below = surfaceY - lensWS.y;
                float perPixel = max(fwidth(below), 1e-7);
                half under = saturate(below / perPixel + 0.5);

                // Wobble the view like looking through moving water.
                float t = time * _DistortionSpeed;
                float2 p = uv * (6.0 * _DistortionDetail);
                float2 wobble;
                wobble.x = sin(p.y * 1.7 + t * 1.3) + 0.5 * sin(p.y * 3.9 - t * 2.1) + 0.3 * sin((p.x + p.y) * 2.6 + t * 0.9);
                wobble.y = cos(p.x * 1.5 - t * 1.1) + 0.5 * sin(p.x * 3.3 + t * 2.4) + 0.3 * cos((p.x - p.y) * 2.3 - t * 1.4);
                float2 wobbledUV = clamp(uv + wobble * (_Distortion * 0.0025 * under), 0.0005, 0.9995);

                half3 scene = SampleSceneColor(wobbledUV);
                float3 hitWS = WorldFromDepth(wobbledUV, SampleSceneDepth(wobbledUV));
                float3 ray = hitWS - cameraWS;
                float sceneDistance = length(ray);
                float3 direction = ray / max(sceneDistance, 1e-5);
                float leave = DistanceToLeaveWater(cameraWS, direction);
                float waterDistance = min(sceneDistance, leave);

            #if defined(_KAPI_CAUSTICS)
                // Caustics land on the floor and walls underwater.
                float3 slopeNormal = KapiSurfaceNormalFromPosition(hitWS);
                half submerged = sceneDistance <= leave + 0.02 && hitWS.y < _WaterRestHeight ? 1.0h : 0.0h;
                float hitDepth = max(0.0, _WaterRestHeight - hitWS.y);
                half3 caustics = KapiCausticsOnSurface(TEXTURE2D_ARGS(_CausticsTexture, sampler_CausticsTexture), hitWS, slopeNormal, time, _CausticsSize, _CausticsSpeed, _CausticsSplit);
                half3 causticLight = caustics * _CausticsColor.rgb * (_CausticsStrength * submerged * exp(-hitDepth / max(_CausticsDepth, 0.01)));
                scene = scene * (1.0h + causticLight) + causticLight * 0.08h; // A little light of its own for dark floors.
            #endif

                // Water soaks up light: reds go first, then greens, until only the water color is left.
                half daylight = KapiWaterDaylight(_LightInfluence);
                float visibility = max(_UnderwaterVisibility, 0.01);
                half3 absorption = (1.05h - _UnderwaterColor.rgb) * (_ColorAbsorption / visibility);
                half3 fogColor = _UnderwaterColor.rgb * daylight;
                half fog = 1.0h - exp(-waterDistance / visibility);
                half3 color = lerp(scene * exp(-waterDistance * absorption), fogColor, fog);

                // Deeper is darker.
                float headDepth = max(0.0, _WaterRestHeight - cameraWS.y);
                if (_DarkenWithDepth > 0.0)
                    color *= max(exp2(-headDepth / _DarkenWithDepth), 0.12);

                float2 fromCenter = uv - 0.5;
                color *= 1.0h - _Vignette * smoothstep(0.15, 0.75, length(fromCenter) * 1.25);

                // The waterline across your eyes when your head is half in darkens both sides of it.
                half waterline = (1.0h - saturate(abs(below) / (perPixel * max(_WaterlineThickness, 0.01)))) * _WaterlineDarkness * step(0.01, _WaterlineThickness);
                half alpha = under + (1.0h - under) * waterline;
                return half4(color * under * (1.0h - waterline), alpha);
            }
            ENDHLSL
        }
    }
}
