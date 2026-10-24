// Shared code for Kapi/Window Glass and Kapi/Mirror. Define KAPI_MIRROR before including to add the mirror
// reflection layer. Everything (patterns, raindrops, dirt, fingerprints) is generated here, so no textures are needed.
#ifndef KAPI_GLASS_CORE_INCLUDED
#define KAPI_GLASS_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

CBUFFER_START(UnityPerMaterial)
    float _PatternScale;
    float _WorldScale;
    float _PatternRotation;
    float _PatternSeed;
    float _Bumpiness;
    float _Refraction;
    half4 _Tint;
    half _Darkness;
    half4 _EdgeColor;
    half _EdgeStrength;
    half _EdgePower;
    half _Smoothness;
    half _Reflectivity;
    half _FresnelPower;
    half _HighlightStrength;
    half _FrostAmount;
    half _DirtAmount;
    half4 _DirtColor;
    float _DirtScale;
    half _Streaks;
    half _RainAmount;
    float _DropSize;
    float _RainSpeed;
    half _RainDistortion;
    half _SmudgeAmount;
    half _FingerprintAmount;
    float _SmudgeScale;
    half _EdgeFogAmount;
    half _EdgeFogWidth;
    half4 _EdgeFogColor;
    half _MirrorStrength;
    half4 _MirrorTint;
    half _MirrorBrightness;
    float _Cull;
CBUFFER_END

#if defined(KAPI_MIRROR)
    // Set per mirror by the Mirror component each frame: one reflection image per eye, the matrix that maps the
    // mirror's surface into each image, and each eye's position (to tell which eye is drawing).
    TEXTURE2D(_KapiMirrorTexL);
    SAMPLER(sampler_KapiMirrorTexL);
    TEXTURE2D(_KapiMirrorTexR);
    SAMPLER(sampler_KapiMirrorTexR);
    float4x4 _KapiMirrorVPL;
    float4x4 _KapiMirrorVPR;
    float4 _KapiMirrorEyeL;
    float4 _KapiMirrorEyeR;
    float _KapiMirrorActive;
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float fogFactor : TEXCOORD4;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings GlassVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = normal.normalWS;
    output.tangentWS = float4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv = input.uv;
    output.fogFactor = ComputeFogFactor(position.positionCS.z);
    return output;
}

// ---------------------------------------------------------------------- Noise

// Hashes without sine, which stay stable on mobile GPUs.
float Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float3 Hash32(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

// Smooth value noise. Returns (value, d/dx, d/dy) so slopes come for free.
float3 ValueNoiseD(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float2 du = 6.0 * f * (1.0 - f);

    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));

    float k1 = b - a;
    float k2 = c - a;
    float k4 = a - b - c + d;
    return float3(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y, du * float2(k1 + k4 * u.y, k2 + k4 * u.x));
}

float Fbm(float2 p)
{
    return ValueNoiseD(p).x * 0.55 + ValueNoiseD(p * 2.13 + 5.2).x * 0.3 + ValueNoiseD(p * 4.37 + 11.7).x * 0.15;
}

// ---------------------------------------------------------------------- Glass patterns
// Each returns the slope of the glass surface (how much it tilts in x and y), which bends the view.

// Long, flowing, wandering bumps, like rolled "rain" glass.
float2 RippledSlope(float2 p)
{
    float2 stretch = float2(1.0, 0.55);
    float2 q = p * stretch;
    q += (ValueNoiseD(q * 0.7 + 13.1).x - 0.5) * 0.9;
    float2 big = ValueNoiseD(q * 1.3).yz * 1.3;
    float2 small = ValueNoiseD(q * 2.9 + 7.7).yz * 2.9 * 0.3;
    return (big + small) * stretch * 0.45;
}

// Round dimples, like bathroom privacy glass. Slopes point away from each dimple's center.
float2 HammeredSlope(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    float nearest = 8.0;
    float2 toCenter = float2(0.0, 0.0);

    UNITY_UNROLL
    for (int y = -1; y <= 1; y++)
    {
        UNITY_UNROLL
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y);
            float2 center = offset + 0.1 + Hash22(cell + offset) * 0.8;
            float2 d = center - f;
            float distanceSquared = dot(d, d);
            if (distanceSquared < nearest)
            {
                nearest = distanceSquared;
                toCenter = d;
            }
        }
    }
    return -toCenter * 1.6;
}

// Rounded ribs side by side. Each rib curves across, so the view stretches into stripes.
float2 ReededSlope(float2 p)
{
    float x = frac(p.x) - 0.5;
    float slope = x / sqrt(max(0.27 - x * x, 0.03));
    // Soften the seam between ribs a little so it doesn't flicker.
    slope *= smoothstep(0.5, 0.42, abs(x)) * 0.6 + 0.4;
    return float2(slope * 0.55, 0.0);
}

// Almost flat, with the slight wobble of an old pane.
float2 ClearSlope(float2 p)
{
    return ValueNoiseD(p * 0.35 + 3.3).yz * 0.12;
}

float2 PatternSlope(float2 p)
{
#if defined(_PATTERN_FLAT)
    return float2(0.0, 0.0);
#elif defined(_PATTERN_HAMMERED)
    return HammeredSlope(p);
#elif defined(_PATTERN_REEDED)
    return ReededSlope(p);
#elif defined(_PATTERN_CLEAR)
    return ClearSlope(p);
#else
    return RippledSlope(p);
#endif
}

// ---------------------------------------------------------------------- Raindrops
// Return (slope x, slope y, wetness). Coordinates are in "drop cells", with y pointing up the glass.

// Drops resting on the glass. Each acts like a tiny lens.
float3 RestingDrops(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p) - 0.5;
    float3 random = Hash32(cell);

    float present = step(1.0 - _RainAmount * 0.85, random.z);
    float2 d = f - (random.xy - 0.5) * 0.6;
    float radius = lerp(0.07, 0.2, Hash21(cell + 4.7));
    float edge = length(d) / radius;

    float inside = (1.0 - smoothstep(0.85, 1.0, edge)) * present;
    return float3(-d / radius * 1.6 * inside, inside);
}

// Drops sliding down in columns, speeding up and slowing down, leaving a wet trail of tiny droplets.
float3 SlidingDrops(float2 p, float time)
{
    const float cellHeight = 4.0;
    float column = floor(p.x);
    float3 random = Hash32(float2(column, 17.0));

    float present = step(1.0 - _RainAmount, random.z);
    float speed = lerp(0.5, 1.4, random.x);
    float slide = time * speed + 0.6 * sin(time * speed * 2.0 + random.y * 6.28) + random.y * cellHeight;

    float along = p.y + slide;
    float localY = (frac(along / cellHeight) - 0.5) * cellHeight;
    // The wiggly path is fixed to the glass, so the trail follows the same path the drop took.
    float wobble = sin(p.y * 2.3 + random.x * 6.28) * 0.08;
    float2 d = float2(frac(p.x) - 0.5 - (random.y - 0.5) * 0.5 - wobble, localY);

    // The main drop, a bit longer than it is wide.
    float2 shape = d / float2(0.18, 0.24);
    float edge = length(shape);
    float drop = (1.0 - smoothstep(0.8, 1.0, edge)) * present;
    float2 slope = -shape * 1.4 * drop;

    // A trail above the drop: a wet streak with tiny droplets left behind on the glass, fading with height.
    float above = saturate(d.y / (cellHeight * 0.45));
    float trail = step(0.0, d.y) * (1.0 - above) * (1.0 - smoothstep(0.05, 0.12, abs(d.x))) * present;
    float2 bead = float2(d.x, (frac(p.y * 3.0) - 0.5) / 3.0) / 0.05;
    float droplet = (1.0 - smoothstep(0.7, 1.0, length(bead))) * trail * step(0.3, Hash21(float2(column, floor(p.y * 3.0))));
    slope += -bead * 0.8 * droplet;

    return float3(slope, saturate(drop + droplet + trail * 0.35));
}

// ---------------------------------------------------------------------- Smudges and fingerprints

// Returns how much grime covers this spot: soft wiped smears plus oval fingerprints with ridge lines.
half Smudges(float2 surface)
{
    float2 p = surface / max(_SmudgeScale, 0.01);
    half smear = saturate((Fbm(p * 1.5 + 9.1) - 0.55) * 3.0) * _SmudgeAmount;

    float2 cellCoords = p * 3.0;
    float2 cell = floor(cellCoords);
    float3 random = Hash32(cell + 2.3);
    float present = step(1.0 - _FingerprintAmount * 0.6, random.z);

    float2 d = frac(cellCoords) - 0.5 - (random.xy - 0.5) * 0.4;
    float turn = random.x * 6.2832;
    float turnSin, turnCos;
    sincos(turn, turnSin, turnCos);
    d = float2(d.x * turnCos - d.y * turnSin, d.x * turnSin + d.y * turnCos);

    float oval = length(d / float2(0.09, 0.12));
    float ridges = 0.5 + 0.5 * sin(oval * 60.0 + random.y * 6.2832);
    half print = (1.0 - smoothstep(0.75, 1.0, oval)) * ridges * present * _FingerprintAmount;

    return saturate(smear + print * 0.8);
}

// ---------------------------------------------------------------------- Frost

static const float2 kBlurTaps[8] =
{
    float2(1.0, 0.0), float2(0.707, 0.707), float2(0.0, 1.0), float2(-0.707, 0.707),
    float2(-1.0, 0.0), float2(-0.707, -0.707), float2(0.0, -1.0), float2(0.707, -0.707)
};

half3 FrostedSample(float2 uv, float radius, float2 pixel)
{
    // A disc of samples, turned a little differently per pixel so it reads as soft frost, not rings.
    float angle = Hash21(pixel) * 6.2832;
    float s, c;
    sincos(angle, s, c);
    float2 aspect = float2(_ScreenParams.y / _ScreenParams.x, 1.0);

    half3 sum = SampleSceneColor(uv);
    UNITY_UNROLL
    for (int i = 0; i < 8; i++)
    {
        float2 tap = kBlurTaps[i] * (i % 2 == 0 ? 1.0 : 0.55);
        tap = float2(tap.x * c - tap.y * s, tap.x * s + tap.y * c);
        sum += SampleSceneColor(uv + tap * radius * aspect);
    }
    return sum / 9.0;
}

// ---------------------------------------------------------------------- Mirror

#if defined(KAPI_MIRROR)
half3 SampleMirrorEye(float2 uv, bool rightEye)
{
    uv = saturate(uv);
    if (rightEye)
        return SAMPLE_TEXTURE2D_LOD(_KapiMirrorTexR, sampler_KapiMirrorTexR, uv, 0).rgb;
    return SAMPLE_TEXTURE2D_LOD(_KapiMirrorTexL, sampler_KapiMirrorTexL, uv, 0).rgb;
}

// The planar reflection for this spot on the mirror, for whichever eye is drawing. Blur spreads a few samples.
half3 SampleMirror(float3 positionWS, float2 offset, float blur)
{
    // Each eye has its own reflection image; pick the one rendered from the eye nearest this camera.
    float3 camera = GetCameraPositionWS();
    bool rightEye = distance(camera, _KapiMirrorEyeR.xyz) < distance(camera, _KapiMirrorEyeL.xyz);

    float4 clip = mul(rightEye ? _KapiMirrorVPR : _KapiMirrorVPL, float4(positionWS, 1.0));
    float2 uv = clip.xy / clip.w * 0.5 + 0.5 + offset;

    half3 color = SampleMirrorEye(uv, rightEye);
    if (blur > 0.0001)
    {
        color += SampleMirrorEye(uv + float2(blur, blur), rightEye);
        color += SampleMirrorEye(uv + float2(-blur, blur), rightEye);
        color += SampleMirrorEye(uv + float2(blur, -blur), rightEye);
        color += SampleMirrorEye(uv + float2(-blur, -blur), rightEye);
        color /= 5.0;
    }
    return color;
}
#endif

// ---------------------------------------------------------------------- Shading

half4 GlassFragment(Varyings input, float faceSign)
{
    float3 geometricNormal = normalize(input.normalWS);
    float3 tangent = normalize(input.tangentWS.xyz);
    float3 bitangent = cross(geometricNormal, tangent) * input.tangentWS.w;
    float3 normal = geometricNormal * faceSign;

    // Pattern coordinates: real meters across the glass, or the mesh's UVs.
    float2 surface = _WorldScale > 0.5
        ? float2(dot(input.positionWS, tangent), dot(input.positionWS, bitangent))
        : input.uv;

    float rotSin, rotCos;
    sincos(radians(_PatternRotation), rotSin, rotCos);
    float2 patternCoords = float2(surface.x * rotCos - surface.y * rotSin, surface.x * rotSin + surface.y * rotCos);
    patternCoords = patternCoords / max(_PatternScale, 0.001) + _PatternSeed * 17.31;

    float2 slope = PatternSlope(patternCoords) * _Bumpiness;
    half wet = 0.0;
    half dirt = 0.0;
    half grime = 0.0;
    half edgeFog = 0.0;

#if defined(_RAIN_ON)
    float2 rainCoords = surface / (0.035 * _DropSize) + _PatternSeed * 3.7;
    float3 resting = RestingDrops(rainCoords * 1.7);
    float3 sliding = SlidingDrops(rainCoords * float2(0.6, 1.0), _Time.y * _RainSpeed * 1.5);
    slope += (resting.xy + sliding.xy) * _RainDistortion * 2.0;
    wet = saturate(resting.z + sliding.z);
#endif

#if defined(_DIRT_ON)
    float2 dirtCoords = surface / max(_DirtScale, 0.01);
    float dirtNoise = Fbm(dirtCoords * 2.0);
    // Streaks run down the glass, like dried drips.
    float streak = Fbm(float2(dirtCoords.x * 7.0, dirtCoords.y * 0.6) + 31.4);
    dirtNoise = lerp(dirtNoise, dirtNoise * 0.5 + streak * 0.7, _Streaks);
    dirt = saturate((dirtNoise - (1.0 - _DirtAmount)) * 2.5 + _DirtAmount * 0.15) * (1.0 - wet);
#endif

#if defined(_SMUDGES_ON)
    grime = Smudges(surface) * (1.0 - wet);
#endif

#if defined(_EDGEFOG_ON)
    // Steam gathers along the edges of the mesh, with a ragged inner border.
    float2 uv = input.uv;
    float toEdge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
    float ragged = (Fbm(surface * 6.0) - 0.5) * _EdgeFogWidth;
    edgeFog = (1.0 - smoothstep(0.0, max(_EdgeFogWidth, 0.001), toEdge + ragged)) * _EdgeFogAmount * (1.0 - wet);
#endif

    float3 bumped = normalize(normal - (slope.x * tangent + slope.y * bitangent) * 0.5);
    float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half viewFacing = saturate(dot(bumped, view));
    half grazing = 1.0 - saturate(abs(dot(geometricNormal, view)));

    // Bend the picture behind the glass by how much the surface tilts, as seen from the eye.
    float2 bend = TransformWorldToViewDir(bumped).xy - TransformWorldToViewDir(normal).xy;
    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    float2 refractedUV = saturate(screenUV + bend * _Refraction);

    // Haze from frost, dirt, smudges and steam blurs what's seen through (and reflected in) the glass.
    float haze = dirt * 0.3 + grime * 0.25 + edgeFog * 0.5;

#if defined(_FROST_ON)
    // Water on frosted glass clears it.
    float blur = (_FrostAmount * (1.0 - wet * 0.85) + haze) * 0.03;
#else
    float blur = haze * 0.03;
#endif

    half3 behind;
#if defined(KAPI_MIRROR)
    // A full mirror hides what's behind it, so skip looking through.
    UNITY_BRANCH
    if (_MirrorStrength >= 0.999)
        behind = half3(0.0, 0.0, 0.0);
    else
#endif
    {
        UNITY_BRANCH
        if (blur > 0.0001)
            behind = FrostedSample(refractedUV, blur, input.positionCS.xy);
        else
            behind = SampleSceneColor(refractedUV);
    }

    // Light passing through: tinted, a little darker, and greener at grazing angles like thick glass.
    half3 transmitted = behind * _Tint.rgb * (1.0 - _Darkness);
    half edge = pow(grazing, _EdgePower) * _EdgeStrength;
    transmitted *= lerp(half3(1.0, 1.0, 1.0), _EdgeColor.rgb, edge);

    // Reflections of the sky and surroundings: weak straight on, strong at grazing angles. Grime dulls them.
    half smoothness = saturate(_Smoothness * (1.0 - dirt * 0.6 - grime * 0.3 - edgeFog * 0.5) + wet * 0.1);
    half3 reflected = GlossyEnvironmentReflection(reflect(-view, bumped), 1.0 - smoothness, 1.0);
    half fresnel = saturate((0.04 + 0.96 * pow(1.0 - viewFacing, _FresnelPower)) * _Reflectivity);
    half3 color = lerp(transmitted, reflected, fresnel);

#if defined(KAPI_MIRROR)
    // The mirror reflection, sharp enough to touch: rendered from each eye, bent by the same bumps as the glass.
    half3 mirror;
    UNITY_BRANCH
    if (_KapiMirrorActive > 0.5)
        mirror = SampleMirror(input.positionWS, bend * _Refraction * 0.5, blur * 0.15);
    else
        mirror = GlossyEnvironmentReflection(reflect(-view, bumped), 1.0 - smoothness, 1.0);

    mirror *= _MirrorTint.rgb * _MirrorBrightness;
    color = lerp(color, mirror, saturate(_MirrorStrength * (1.0 - edgeFog * 0.6)));
#endif

    // Sun glints off the surface.
    Light sun = GetMainLight();
    half3 halfway = normalize(sun.direction + view);
    half shininess = exp2(10.0 * smoothness + 1.0);
    half glint = pow(saturate(dot(bumped, halfway)), shininess) * (shininess + 8.0) / (8.0 * PI);
    color += sun.color * glint * _HighlightStrength * 0.25 * saturate(dot(bumped, sun.direction));

    half3 ambient = SampleSH(normal) + sun.color * saturate(dot(normal, sun.direction)) * 0.6;

#if defined(_SMUDGES_ON)
    // Grime catches light: a faint milky film, brighter where the sun hits it.
    half sheen = pow(saturate(dot(bumped, halfway)), 6.0);
    color = lerp(color, color * 0.85 + ambient * 0.05, grime);
    color += (ambient * 0.06 + sun.color * sheen * 0.15) * grime;
#endif

#if defined(_DIRT_ON)
    color = lerp(color, _DirtColor.rgb * ambient, dirt * 0.65 * _DirtColor.a);
#endif

#if defined(_EDGEFOG_ON)
    color = lerp(color, _EdgeFogColor.rgb * ambient, edgeFog * 0.7 * _EdgeFogColor.a);
#endif

    color = MixFog(color, input.fogFactor);
    return half4(color, 1.0);
}

#endif // KAPI_GLASS_CORE_INCLUDED
