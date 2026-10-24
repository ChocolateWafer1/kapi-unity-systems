// Realistic water for URP: rolling Gerstner waves, two layers of drifting ripples, reflections of the scene (screen
// space, falling back to the sky and reflection probes), sun and light glints, refraction with depth-based color,
// glowing wave crests, shore and crest foam, caustics on the floor, see-through sides that look like a solid body of
// water, and a proper view from underneath (Snell's window). Works in VR (single pass instanced) and on Quest.
// The Water Volume component reads the wave settings below, so swimming and splashes follow the same waves.
Shader "Kapi/Water"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor ("Shallow Tint", Color) = (0.6, 0.97, 0.92, 1)
        _DeepColor ("Deep Color", Color) = (0.02, 0.27, 0.36, 1)
        _Visibility ("Visibility (m)", Range(0.1, 60)) = 7
        _ScatterColor ("Wave Glow Color", Color) = (0.12, 0.75, 0.65, 1)
        _ScatterStrength ("Wave Glow", Range(0, 3)) = 0.7
        _LightInfluence ("Darken In Low Light", Range(0, 1)) = 0

        [Header(Waves)]
        _WaveStrength ("Wave Strength", Range(0, 2)) = 1
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 1
        _WaveA ("Wave A (dir X, dir Z, steepness, wavelength m)", Vector) = (1, 0.35, 0.1, 7)
        _WaveB ("Wave B (dir X, dir Z, steepness, wavelength m)", Vector) = (-0.45, 1, 0.08, 4.1)
        _WaveC ("Wave C (dir X, dir Z, steepness, wavelength m)", Vector) = (0.7, -0.8, 0.07, 2.3)
        _WaveD ("Wave D (dir X, dir Z, steepness, wavelength m)", Vector) = (-0.9, -0.3, 0.05, 1.4)

        [Header(Ripples)]
        [NoScaleOffset][Normal] _RippleNormal ("Ripple Normal Map", 2D) = "bump" {}
        _RippleStrength ("Ripple Strength", Range(0, 2)) = 0.45
        _RippleSize ("Ripple Size (m)", Range(0.1, 20)) = 4.5
        _RippleSpeed ("Ripple Speed (m/s)", Range(0, 2)) = 0.35
        _RippleFadeDistance ("Ripples Calm Down At (m)", Range(1, 300)) = 45

        [Header(Reflections)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.96
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 1
        _SunGlint ("Sun Glints", Range(0, 10)) = 2.5
        _SunGlintSharpness ("Glint Sharpness", Range(16, 4096)) = 1100
        [Toggle(_KAPI_LIGHT_GLINTS)] _LightGlints ("Glints From Other Lights", Float) = 1
        _LightGlint ("Other Light Glints", Range(0, 10)) = 0.2
        [ToggleOff(_KAPI_SSR_OFF)] _ScreenReflections ("Reflect The Scene (Screen Space)", Float) = 1
        _ReflectionSteps ("Reflection Steps (quality vs speed)", Range(4, 32)) = 14
        _ReflectionStepSize ("Reflection First Step (m)", Range(0.02, 2)) = 0.15
        _ReflectionDistortion ("Reflection Wobble From Ripples", Range(0, 1)) = 0.5

        [Header(Refraction)]
        _Refraction ("Refraction", Range(0, 0.3)) = 0.05
        _EdgeSoftness ("Shore Softness (m)", Range(0.01, 2)) = 0.12

        [Header(Foam)]
        [Toggle(_KAPI_FOAM)] _FoamEnabled ("Foam", Float) = 1
        [NoScaleOffset] _FoamTexture ("Foam Texture", 2D) = "white" {}
        _FoamColor ("Foam Color", Color) = (0.95, 0.98, 1, 1)
        _FoamSize ("Foam Size (m)", Range(0.1, 10)) = 1.6
        _ShoreFoam ("Shore Foam", Range(0, 1)) = 0.8
        _ShoreFoamWidth ("Shore Foam Width (m)", Range(0.01, 3)) = 0.45
        _CrestFoam ("Wave Crest Foam", Range(0, 1)) = 0.25
        _CrestFoamHeight ("Crest Foam Starts At", Range(0, 1)) = 0.7

        [Header(Caustics)]
        [Toggle(_KAPI_CAUSTICS)] _CausticsEnabled ("Caustics", Float) = 1
        [NoScaleOffset] _CausticsTexture ("Caustics Texture", 2D) = "black" {}
        [HDR] _CausticsColor ("Caustics Color", Color) = (1, 1, 1, 1)
        _CausticsStrength ("Caustics Strength", Range(0, 5)) = 1.6
        _CausticsSize ("Caustics Size (m)", Range(0.2, 20)) = 2.6
        _CausticsSpeed ("Caustics Speed", Range(0, 3)) = 0.6
        _CausticsSplit ("Caustics Color Split", Range(0, 0.05)) = 0.008
        _CausticsDepth ("Caustics Fade Depth (m)", Range(0.1, 60)) = 7

        [Header(Underwater)]
        _UnderwaterColor ("Underwater Fog Color", Color) = (0.04, 0.36, 0.45, 1)
        _UnderwaterVisibility ("Underwater Visibility (m)", Range(0.5, 120)) = 14
        _SurfaceFromBelow ("Surface Brightness From Below", Range(0, 3)) = 1.1
        _WindowSoftness ("Snell's Window Softness", Range(0.01, 1)) = 0.12
        _WindowSize ("See Out From Underwater", Range(0, 1)) = 0.85
        _BelowDistortion ("Distortion From Below", Range(0, 0.3)) = 0.08
        _BelowColorSplit ("Color Split From Below", Range(0, 0.5)) = 0.15
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
    #include "Assets/KapiWater/Shaders/KapiWaterCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _ShallowColor;
        half4 _DeepColor;
        float _Visibility;
        half4 _ScatterColor;
        half _ScatterStrength;
        half _LightInfluence;

        float _WaveStrength;
        float _WaveSpeed;
        float4 _WaveA;
        float4 _WaveB;
        float4 _WaveC;
        float4 _WaveD;

        half _RippleStrength;
        float _RippleSize;
        float _RippleSpeed;
        float _RippleFadeDistance;

        half _Smoothness;
        half _ReflectionStrength;
        half _SunGlint;
        half _SunGlintSharpness;
        half _LightGlint;
        half _ReflectionSteps;
        float _ReflectionStepSize;
        half _ReflectionDistortion;

        half _Refraction;
        float _EdgeSoftness;

        half4 _FoamColor;
        float _FoamSize;
        half _ShoreFoam;
        float _ShoreFoamWidth;
        half _CrestFoam;
        half _CrestFoamHeight;

        half4 _CausticsColor;
        half _CausticsStrength;
        float _CausticsSize;
        float _CausticsSpeed;
        float _CausticsSplit;
        float _CausticsDepth;

        half4 _UnderwaterColor;
        float _UnderwaterVisibility;
        half _SurfaceFromBelow;
        half _WindowSoftness;
        half _WindowSize;
        half _BelowDistortion;
        half _BelowColorSplit;
    CBUFFER_END

    TEXTURE2D(_RippleNormal);       SAMPLER(sampler_RippleNormal);
    TEXTURE2D(_FoamTexture);        SAMPLER(sampler_FoamTexture);
    TEXTURE2D(_CausticsTexture);    SAMPLER(sampler_CausticsTexture);

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        // r = how much the waves may sway this vertex sideways (0 at the edges), g = 1 if it rides the waves,
        // b = 1 for the top surface, 0 for the sides and bottom.
        float4 color : COLOR;
        float4 box : TEXCOORD1; // half width, depth, half length of the water box (object space), w = 1 if its bottom acts as a floor
        float2 look : TEXCOORD2; // x = the water's Look Scale: every size and distance below is multiplied by it
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float3 tangentWS : TEXCOORD2;
        float3 binormalWS : TEXCOORD3;
        float4 wave : TEXCOORD4; // x = 0 trough .. 1 crest, y = rest height of the water, z = 1 on the top surface
        float4 box : TEXCOORD5;
        half fogFactor : TEXCOORD6;
        float look : TEXCOORD7;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // A wave with its wavelength (and so its height) multiplied by the Look Scale.
    float4 ScaleWave(float4 wave, float look)
    {
        return float4(wave.xyz, wave.w * look);
    }

    Varyings Vert(Attributes input)
    {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        float top = step(0.5, input.color.b);
        float look = input.look.x > 0.0 ? input.look.x : 1.0;
        float4 waveA = ScaleWave(_WaveA, look);
        float4 waveB = ScaleWave(_WaveB, look);
        float4 waveC = ScaleWave(_WaveC, look);
        float4 waveD = ScaleWave(_WaveD, look);
        KapiWaveSample waves = KapiSampleWaves(positionWS.xz, _Time.y * _WaveSpeed, waveA, waveB, waveC, waveD, _WaveStrength, saturate(input.color.r));
        positionWS += waves.offset * saturate(input.color.g);

        float amplitude = max(KapiWaveAmplitude(waveA, waveB, waveC, waveD, _WaveStrength), 1e-4);
        output.look = look;
        output.positionWS = positionWS;
        output.normalWS = top > 0.5 ? KapiWaveNormal(waves) : TransformObjectToWorldNormal(input.normalOS);
        output.tangentWS = normalize(waves.tangent);
        output.binormalWS = normalize(waves.binormal);
        float crest = saturate(waves.offset.y / amplitude * 0.5 + 0.5) * top;
        output.wave = float4(crest, TransformObjectToWorld(float3(0.0, 0.0, 0.0)).y, top, 0.0);
        output.box = input.box;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    // How far the view ray travels through the water box after this point before it leaves, and whether it leaves
    // through the bottom.
    float DistanceToLeaveWater(float3 positionWS, half3 view, float4 box, out bool throughBottom)
    {
        float4x4 worldToObject = GetWorldToObjectMatrix();
        float3 o = mul(worldToObject, float4(positionWS, 1.0)).xyz;
        float3 d = mul((float3x3)worldToObject, -view); // Not normalized, so distances stay in meters.
        d += (abs(d) < 1e-6) * 1e-6;
        float3 lo = float3(-box.x, -box.y, -box.z);
        float3 hi = float3(box.x, max(o.y, 0.0) + 1e-3, box.z);
        float3 far = max((lo - o) / d, (hi - o) / d);
        float leave = min(min(far.x, far.y), far.z);
        throughBottom = d.y < 0.0 && far.y <= min(far.x, far.z);
        return max(0.0, leave);
    }

    // One layer of ripples, turned to its own angle so the layers never line up into a visible grid. Returns the
    // ripple's slope in world X/Z.
    half2 RippleLayer(float2 p, float angle, float size, float2 drift, float time)
    {
        float s, c;
        sincos(angle, s, c);
        float2 uv = float2(c * p.x - s * p.y, s * p.x + c * p.y) / size + drift * time / size;
        half3 n = UnpackNormal(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uv));
        half2 slope = n.xy / max(n.z, 0.2h);
        return half2(c * slope.x + s * slope.y, -s * slope.x + c * slope.y);
    }

    // The wave normal plus three layers of drifting ripples (top surface only). Big slow patches make some areas
    // choppier than others, like gusts of wind, which also hides any repeating.
    half3 SurfaceNormal(Varyings input, float distance, float time)
    {
        float size = max(_RippleSize * input.look, 0.01);
        float2 p = input.positionWS.xz;
        float speed = _RippleSpeed * input.look;

        // Slowly bend where the ripple pattern sits, so its repeats never line up into a grid.
        float2 w = p / (size * 3.3);
        p += float2(sin(w.y * 1.7 + time * 0.05) + 0.6 * sin((w.x + w.y) * 0.93),
                    cos(w.x * 1.3 - time * 0.04) + 0.6 * sin((w.x - w.y) * 1.11)) * (size * 0.38);
        half2 slope = RippleLayer(p, 0.0, size, float2(0.8, 0.6) * speed, time)
            + RippleLayer(p, 1.13, size * 0.41, float2(-0.5, 0.86) * speed * 1.3, time) * 0.75h
            + RippleLayer(p, -2.31, size * 2.6, float2(0.3, -0.95) * speed * 0.7, time) * 0.6h;

        float2 g = p / (size * 4.0);
        half gust = 0.6h + 0.25h * sin(dot(g, float2(1.0, 0.62)) + time * 0.31)
            + 0.2h * sin(dot(g, float2(-0.43, 1.27)) - time * 0.23)
            + 0.15h * sin(dot(g, float2(1.71, -0.83)) + time * 0.47);
        gust = clamp(gust, 0.2h, 1.2h);

        // Far-away ripples calm down so they don't sparkle and shimmer in the headset.
        half strength = _RippleStrength * gust * lerp(1.0h, 0.2h, saturate(distance / max(_RippleFadeDistance * input.look, 0.01))) * input.wave.z;
        half3 ripple = normalize(half3(slope * strength * 0.5h, 1.0h));

        return normalize(ripple.x * normalize(input.tangentWS) + ripple.y * normalize(input.binormalWS) + ripple.z * normalize(input.normalWS));
    }

    void ViewOf(Varyings input, out half3 view, out float distance, out float2 screenUV)
    {
        float3 toCamera = GetCameraPositionWS() - input.positionWS;
        distance = max(length(toCamera), 1e-4);
        view = toCamera / distance;
        screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        // The top of the water, seen from above.
        Pass
        {
            Name "Water Surface"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragAbove

            #pragma shader_feature_local_fragment _KAPI_FOAM
            #pragma shader_feature_local_fragment _KAPI_CAUSTICS
            #pragma shader_feature_local_fragment _KAPI_LIGHT_GLINTS
            #pragma shader_feature_local_fragment _KAPI_SSR_OFF

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            half3 Glint(half3 normal, half3 view, half3 lightDirection, half sharpness)
            {
                half3 h = SafeNormalize(lightDirection + view);
                return pow(saturate(dot(normal, h)), sharpness) * sqrt(sharpness) * 0.25h;
            }

            half3 OtherLightGlints(float3 positionWS, float2 screenUV, half3 normal, half3 view)
            {
                half3 glints = 0;
            #if defined(_KAPI_LIGHT_GLINTS) && (defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP)
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normal;
                inputData.normalizedScreenSpaceUV = screenUV;
                half sharpness = max(_SunGlintSharpness, 8.0h);
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, positionWS);
                    glints += Glint(normal, view, light.direction, sharpness) * light.color * light.distanceAttenuation;
                LIGHT_LOOP_END
                glints *= _LightGlint;
            #endif
                return glints;
            }

            // The screen position (same as GetNormalizedScreenSpaceUV) of a point in clip space.
            float2 ClipToScreenUV(float4 positionCS)
            {
                float2 uv = positionCS.xy / positionCS.w * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
            #endif
                TransformNormalizedScreenUV(uv);
                return uv;
            }

            float SceneEyeDepthAt(float2 uv)
            {
                uint2 pixel = (uint2)(saturate(uv) * (_CameraDepthTexture_TexelSize.zw - 1.0));
                return LinearEyeDepth(LoadSceneDepth(pixel), _ZBufferParams);
            }

            // Marches the reflected ray across the screen until it passes behind something, so walls, trees and
            // players right next to the water show up in it. a = how sure the hit is (0 = use the sky/probe instead).
            half4 ScreenReflection(float3 positionWS, half3 direction, float look)
            {
                float stride = max(_ReflectionStepSize * look, 0.01);
                float traveled = 0.0;
                int steps = (int)_ReflectionSteps;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float before = traveled;
                    traveled += stride;
                    stride *= 1.35;

                    float4 clip = TransformWorldToHClip(positionWS + direction * traveled);
                    if (clip.w <= 0.0) break;
                    float2 uv = ClipToScreenUV(clip);
                    if (any(uv < 0.0) || any(uv > 1.0)) break;

                    float gap = clip.w - SceneEyeDepthAt(uv);
                    if (gap <= 0.0) continue;
                    if (gap > max(traveled - before, 0.25) * 2.5) continue; // Passed behind something thin; keep going.

                    // Narrow down exactly where it crossed.
                    float lo = before;
                    float hi = traveled;
                    [unroll]
                    for (int j = 0; j < 4; j++)
                    {
                        float mid = (lo + hi) * 0.5;
                        float4 midClip = TransformWorldToHClip(positionWS + direction * mid);
                        float2 midUV = ClipToScreenUV(midClip);
                        if (midClip.w - SceneEyeDepthAt(midUV) > 0.0) hi = mid;
                        else lo = mid;
                    }
                    float4 hitClip = TransformWorldToHClip(positionWS + direction * hi);
                    uv = ClipToScreenUV(hitClip);

                    float2 edge = min(uv, 1.0 - uv);
                    half fade = saturate(min(edge.x, edge.y) * 8.0) * (1.0h - (half)i / max(steps, 1));
                    half3 color = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb;
                    return half4(color, fade);
                }
                return 0;
            }

            half4 FragAbove(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 view;
                float distance;
                float2 screenUV;
                ViewOf(input, view, distance, screenUV);
                float time = _Time.y;
                half3 normal = SurfaceNormal(input, distance, time);
                half top = input.wave.z; // 1 on the top surface, 0 on the sides and bottom
                float look = input.look;
                float visibility = max(_Visibility * look, 0.01);

                float3 positionWS = input.positionWS;
                float3 cameraWS = GetCameraPositionWS();
                half daylight = KapiWaterDaylight(_LightInfluence);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                // Reflections, stronger at grazing angles (Fresnel): the scene itself where it's on screen, the sky
                // and reflection probes everywhere else.
                half NdotV = saturate(dot(normal, view));
                half fresnel = saturate((0.02h + 0.98h * pow(1.0h - NdotV, 5.0h)) * _ReflectionStrength);
                half roughness = saturate(1.0h - _Smoothness + saturate(distance / 200.0) * 0.12h);
                half3 mirrorNormal = normalize(lerp(normalize(input.normalWS), normal, _ReflectionDistortion));
                half3 reflectDirection = reflect(-view, mirrorNormal);
                if (top > 0.5h) reflectDirection.y = abs(reflectDirection.y); // Waves can't reflect the ground below the horizon.
                half3 reflection = GlossyEnvironmentReflection(reflectDirection, positionWS, roughness, 1.0h, screenUV);
            #if !defined(_KAPI_SSR_OFF)
                [branch]
                if (fresnel > 0.03h)
                {
                    half4 mirrored = ScreenReflection(positionWS, reflectDirection, look);
                    reflection = lerp(reflection, mirrored.rgb, mirrored.a);
                }
            #endif

                half3 glints = Glint(normal, view, mainLight.direction, _SunGlintSharpness) * mainLight.color * shadow * _SunGlint;
                glints += OtherLightGlints(positionWS, screenUV, normal, view);

                // What's under the water, bent by the waves. Anything in front of the water stays unbent.
                float surfaceDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
                float straightDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float2 bend = normal.xz * _Refraction * saturate((straightDepth - surfaceDepth) * 0.7);
                float2 bentUV = screenUV + bend;
                float bentDepth = LinearEyeDepth(SampleSceneDepth(bentUV), _ZBufferParams);
                bool useBent = bentDepth > surfaceDepth;
                float2 sceneUV = useBent ? bentUV : screenUV;
                float sceneDepth = useBent ? bentDepth : straightDepth;
                half3 scene = SampleSceneColor(sceneUV);

                // How much water the view passes through (stopping where it leaves the box), and how deep the floor
                // is below this point.
                float rayScale = distance / max(surfaceDepth, 1e-4);
                float sceneRay = max(0.0, sceneDepth - surfaceDepth) * rayScale;
                bool throughBottom;
                float leave = DistanceToLeaveWater(positionWS, view, input.box, throughBottom);
                float3 floorWS = cameraWS + (positionWS - cameraWS) * (sceneDepth / max(surfaceDepth, 1e-4));

                // Nothing under this water before the view leaves the bottom of the box (a pool without a floor, or
                // a camera without a depth texture): the box bottom becomes a dark floor, so you see deep water with
                // light dancing on it instead of the sky through a hole.
                if (input.box.w > 0.5 && throughBottom && sceneRay > leave + 0.02)
                {
                    floorWS = positionWS - view * leave;
                    scene = _DeepColor.rgb * daylight * 0.55h;
                    sceneRay = leave;
                }
                float throughWater = min(sceneRay, leave);
                float floorDepth = max(0.0, input.wave.y - floorWS.y);

            #if defined(_KAPI_CAUSTICS)
                float3 floorNormal = KapiSurfaceNormalFromPosition(floorWS);
                half3 caustics = KapiCausticsOnSurface(TEXTURE2D_ARGS(_CausticsTexture, sampler_CausticsTexture), floorWS, floorNormal, time, _CausticsSize * look, _CausticsSpeed, _CausticsSplit);
                half causticsFade = exp(-floorDepth / max(_CausticsDepth * look, 0.01)) * saturate(floorDepth * 6.0 / look) * saturate(1.0 - throughWater / (visibility * 3.0));
                // Brightens what's lit, plus a little light of its own so it still shows on dark floors.
                half3 causticLight = caustics * _CausticsColor.rgb * _CausticsStrength * causticsFade;
                scene = scene * (1.0h + causticLight) + causticLight * 0.08h * daylight;
            #endif

                // Light gets absorbed the farther it travels through water: tinted up close, deep color far away.
                half tint = saturate(throughWater / (visibility * 0.25));
                half fog = 1.0h - exp(-throughWater / visibility);
                // Less light reaches deep down, so the sides get darker toward the bottom.
                half sunk = saturate((input.wave.y - positionWS.y) / (visibility * 1.5));
                half3 deep = _DeepColor.rgb * daylight * lerp(1.0h, 0.3h, sunk);
                half3 water = lerp(scene * lerp(1.0h, _ShallowColor.rgb, tint), deep, fog);

                // Sunlight glowing through the thin tops of waves.
                half crest = input.wave.x;
                half backlit = pow(saturate(dot(-view, mainLight.direction)), 3.0h);
                water += _ScatterColor.rgb * _ScatterStrength * crest * crest * (backlit * mainLight.color * shadow + 0.15h * daylight);

                half3 color = lerp(water, reflection, fresnel) + glints;

            #if defined(_KAPI_FOAM)
                float2 foamUV = positionWS.xz / max(_FoamSize * look, 0.01);
                half foam1 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV + float2(0.02, 0.013) * time).r;
                half foam2 = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV * 0.61 + float2(-0.017, 0.022) * time + 0.5).r;
                half pattern = (foam1 + foam2) * 0.5h;

                half shore = 1.0h - saturate(min(floorDepth, throughWater) / max(_ShoreFoamWidth * look, 0.001));
                half shoreFoam = saturate((pattern - (1.0h - shore)) * 4.0h) * shore * _ShoreFoam;
                half crestMask = saturate((crest - _CrestFoamHeight) / max(1.0h - _CrestFoamHeight, 0.001h));
                half crestFoam = saturate((pattern - (1.0h - crestMask)) * 3.0h) * _CrestFoam;
                half foam = saturate(shoreFoam + crestFoam) * top;

                half3 waveNormal = normalize(input.normalWS);
                half3 foamLight = mainLight.color * saturate(dot(waveNormal, mainLight.direction)) * shadow + SampleSH(waveNormal);
                color = lerp(color, _FoamColor.rgb * foamLight, foam * _FoamColor.a);
            #endif

                // Fade out where the water gets paper thin, so it meets the shore softly.
                half alpha = saturate(throughWater / max(_EdgeSoftness * look, 0.001));
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }

        // The underside of the water, seen while swimming.
        Pass
        {
            Name "Water Underside"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend Off
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertBelow
            #pragma fragment FragBelow
            #pragma multi_compile_instancing

            // Only the top surface has an underside worth drawing. From inside the water the sides and bottom are
            // skipped (pushed outside the view so they're clipped), and the underwater view's fog ends at them.
            Varyings VertBelow(Attributes input)
            {
                Varyings output = Vert(input);
                if (input.color.b < 0.5) output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                return output;
            }

            half4 FragBelow(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 view;
                float distance;
                float2 screenUV;
                ViewOf(input, view, distance, screenUV);
                half3 normal = -SurfaceNormal(input, distance, _Time.y); // Facing down, toward you.

                half daylight = KapiWaterDaylight(_LightInfluence);
                Light mainLight = GetMainLight();
                half3 fogColor = _UnderwaterColor.rgb * daylight;

                // Real water only lets you see out through a circle overhead (Snell's window); See Out From
                // Underwater widens it, all the way to the whole surface at 1. Outside it the surface mirrors the
                // water below, with a faint ghost of the world above.
                half NdotV = saturate(dot(normal, view));
                half sinAngle = sqrt(saturate(1.0h - NdotV * NdotV));
                half edge = 1.0h - sinAngle * lerp(1.333h, 1.0h, _WindowSize);
                half window = saturate(edge / max(_WindowSoftness, 0.001h));

                // The world above, bent and wobbling with the waves (more at low angles), colors split like a prism.
                float2 bend = normal.xz * _BelowDistortion * (1.0 + 2.0 * sinAngle);
                half3 above;
                above.r = SampleSceneColor(screenUV + bend * (1.0 + _BelowColorSplit)).r;
                above.g = SampleSceneColor(screenUV + bend).g;
                above.b = SampleSceneColor(screenUV + bend * (1.0 - _BelowColorSplit)).b;
                above *= _SurfaceFromBelow;

                half3 mirror = fogColor * 0.85h + above * 0.15h;
                half3 color = lerp(mirror, above, window);
                // The edge of the window shimmers brighter.
                color += saturate(1.0h - abs(edge) / 0.08h) * 0.3h * (fogColor + 0.15h) * _SurfaceFromBelow;

                // The sun, squashed and wobbling, seen through the window.
                half3 escaped = refract(-view, normal, 1.333h);
                color += pow(saturate(dot(escaped, mainLight.direction)), 300.0h) * window * mainLight.color * _SunGlint;

                half fog = 1.0h - exp(-distance / max(_UnderwaterVisibility * input.look, 0.01));
                return half4(lerp(color, fogColor, fog), 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
