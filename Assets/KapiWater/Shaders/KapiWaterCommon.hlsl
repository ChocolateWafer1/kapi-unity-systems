#ifndef KAPI_WATER_COMMON_INCLUDED
#define KAPI_WATER_COMMON_INCLUDED

// Shared by the water surface and the underwater view. KapiWater.WaterWaves does the exact same math in C#, so
// swimming, splashes and the waterline all line up with the waves you see.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

#define KAPI_WATER_GRAVITY 9.81
#define KAPI_WATER_TWO_PI 6.28318530718

struct KapiWaveSample
{
    float3 offset;   // How far the waves moved this point of the surface.
    float3 tangent;  // Surface slope along world X.
    float3 binormal; // Surface slope along world Z.
};

// One Gerstner wave. wave.xy = direction, wave.z = steepness (0-1), wave.w = wavelength in meters.
// pin (0-1) scales the sideways sway, so the edges of the water can stay put against walls.
void KapiAddGerstnerWave(float4 wave, float2 p, float time, float strength, float pin, inout KapiWaveSample s)
{
    float steepness = wave.z * strength;
    float k = KAPI_WATER_TWO_PI / max(wave.w, 0.01);
    float speed = sqrt(KAPI_WATER_GRAVITY / k);
    float2 d = wave.xy;
    float len = length(d);
    d = len > 1e-4 ? d / len : float2(1.0, 0.0);

    float f = k * (dot(d, p) - speed * time);
    float a = steepness / k;
    float sn, cs;
    sincos(f, sn, cs);

    s.offset += float3(d.x * a * cs * pin, a * sn, d.y * a * cs * pin);
    s.tangent += float3(-d.x * d.x * steepness * sn * pin, d.x * steepness * cs, -d.x * d.y * steepness * sn * pin);
    s.binormal += float3(-d.x * d.y * steepness * sn * pin, d.y * steepness * cs, -d.y * d.y * steepness * sn * pin);
}

KapiWaveSample KapiSampleWaves(float2 p, float time, float4 waveA, float4 waveB, float4 waveC, float4 waveD, float strength, float pin)
{
    KapiWaveSample s;
    s.offset = 0.0;
    s.tangent = float3(1.0, 0.0, 0.0);
    s.binormal = float3(0.0, 0.0, 1.0);
    KapiAddGerstnerWave(waveA, p, time, strength, pin, s);
    KapiAddGerstnerWave(waveB, p, time, strength, pin, s);
    KapiAddGerstnerWave(waveC, p, time, strength, pin, s);
    KapiAddGerstnerWave(waveD, p, time, strength, pin, s);
    return s;
}

float3 KapiWaveNormal(KapiWaveSample s)
{
    return normalize(cross(s.binormal, s.tangent));
}

// How tall the waves can get (the sum of every wave's height), for foam and wave glow.
float KapiWaveAmplitude(float4 waveA, float4 waveB, float4 waveC, float4 waveD, float strength)
{
    float4 steepness = float4(waveA.z, waveB.z, waveC.z, waveD.z) * strength;
    float4 wavelength = max(float4(waveA.w, waveB.w, waveC.w, waveD.w), 0.01);
    return dot(steepness, wavelength) / KAPI_WATER_TWO_PI;
}

// Height of the waves above the water's rest level at world XZ. Waves also push the surface sideways, so this walks
// back to the spot that ended up here before reading its height.
float KapiWaveHeight(float2 p, float time, float4 waveA, float4 waveB, float4 waveC, float4 waveD, float strength, float pin)
{
    float2 q = p;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        KapiWaveSample guess = KapiSampleWaves(q, time, waveA, waveB, waveC, waveD, strength, pin);
        q = p - guess.offset.xz;
    }
    return KapiSampleWaves(q, time, waveA, waveB, waveC, waveD, strength, pin).offset.y;
}

// Two layers of the caustics texture drifting different ways. Keeping the darker of the two makes the light lines
// split and merge like real caustics. split > 0 shifts red and blue apart like light through a prism.
float KapiCausticsLayer(TEXTURE2D_PARAM(causticsTex, causticsSampler), float2 uv1, float2 uv2)
{
    return min(SAMPLE_TEXTURE2D(causticsTex, causticsSampler, uv1).r, SAMPLE_TEXTURE2D(causticsTex, causticsSampler, uv2).r);
}

half3 KapiCaustics(TEXTURE2D_PARAM(causticsTex, causticsSampler), float2 p, float time, float size, float speed, float split)
{
    float2 uv = p / max(size, 0.01);
    float t = time * speed;
    float2 uv1 = uv + float2(0.043, 0.027) * t;
    float2 uv2 = float2(uv.y, -uv.x) * 0.87 + float2(-0.031, 0.049) * t + 0.37;

    half g = KapiCausticsLayer(TEXTURE2D_ARGS(causticsTex, causticsSampler), uv1, uv2);
    half r = g;
    half b = g;
    if (split > 0.0)
    {
        float2 o = float2(split, split * 0.6);
        r = KapiCausticsLayer(TEXTURE2D_ARGS(causticsTex, causticsSampler), uv1 + o, uv2 + o);
        b = KapiCausticsLayer(TEXTURE2D_ARGS(causticsTex, causticsSampler), uv1 - o, uv2 - o);
    }
    return half3(r, g, b);
}

// Caustics on any surface: projected straight down on floors, and sideways (stretched downward, like light rippling
// down a pool wall) on walls, blended by which way the surface faces. normalWS can point either way.
half3 KapiCausticsOnSurface(TEXTURE2D_PARAM(causticsTex, causticsSampler), float3 positionWS, float3 normalWS, float time, float size, float speed, float split)
{
    float3 n = abs(normalWS);
    half floorWeight = saturate(n.y * n.y * 1.15 - 0.05);
    float along = n.x > n.z ? positionWS.z : positionWS.x;
    half3 floorLight = KapiCaustics(TEXTURE2D_ARGS(causticsTex, causticsSampler), positionWS.xz, time, size, speed, split);
    half3 wallLight = KapiCaustics(TEXTURE2D_ARGS(causticsTex, causticsSampler), float2(along, positionWS.y * 0.8), time, size, speed, split);
    return lerp(wallLight * 0.5h, floorLight, floorWeight);
}

// The direction a surface faces, worked out from how its position changes between neighboring pixels.
float3 KapiSurfaceNormalFromPosition(float3 positionWS)
{
    float3 n = cross(ddy(positionWS), ddx(positionWS));
    float len = length(n);
    return len > 1e-8 ? n / len : float3(0.0, 1.0, 0.0);
}

// 1 = colors exactly as picked. With influence > 0 the water darkens when the scene's sun is dim or missing.
half KapiWaterDaylight(half influence)
{
    half sun = saturate(Luminance(_MainLightColor.rgb) * saturate(_MainLightPosition.y) * 1.5h);
    return lerp(1.0h, lerp(0.15h, 1.0h, sun), influence);
}

#endif
