#ifndef FIRSTLIGHT_FLAT_COMMON
#define FIRSTLIGHT_FLAT_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Hemisphere ambient, set globally by the game (keeps every Firstlight material consistent).
float4 _FL_AmbientSky;
float4 _FL_AmbientGround;

// Pixel-art materials (MaterialLibrary): one array layer per material, applied at a fixed texel density.
TEXTURE2D_ARRAY(_FL_Tex);
SAMPLER(sampler_FL_Tex);
float _FL_TexelsPerUnit;   // texture pixels per world unit (unit = 2 m)
float _FL_TileTexels;      // size of one layer in pixels
float _FL_LayerDirt, _FL_LayerGrass, _FL_LayerLitter, _FL_LayerSnow;

// Terrain material map: one texel per grid cell holding a layer index.
TEXTURE2D(_FL_MatMap);
float4 _FL_MatMapSize;     // width, height

// Faceted normal from screen-space derivatives, flipped to face the camera so it is correct
// whatever the platform's y-flip is.
float3 FlatNormal(float3 posWS)
{
    float3 n = normalize(cross(ddy(posWS), ddx(posWS)));
    float3 toCam = GetCameraPositionWS() - posWS;
    return dot(n, toCam) < 0 ? -n : n;
}

// Box projection: the plane the face mostly points along decides which two axes become UV.
float2 BoxProject(float3 p, float3 n)
{
    float3 a = abs(n);
    if (a.y >= a.x && a.y >= a.z) return p.xz;
    if (a.x >= a.z) return float2(p.z, p.y);
    return float2(p.x, p.y);
}

// Samples a material with box-projected UVs at the global texel density, with gradients taken from the
// same projection so mip selection stays stable across triangle edges.
half4 SampleBoxProjected(float3 p, float3 n, float layer, float2 offset)
{
    float k = _FL_TexelsPerUnit / _FL_TileTexels;
    float2 uv = BoxProject(p, n) * k + offset;
    float2 dx = BoxProject(ddx(p), n) * k;
    float2 dy = BoxProject(ddy(p), n) * k;
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(_FL_Tex, sampler_FL_Tex, uv, layer, dx, dy);
}

half4 SampleExplicit(float2 uv, float layer)
{
    return SAMPLE_TEXTURE2D_ARRAY(_FL_Tex, sampler_FL_Tex, uv, layer);
}

float2 Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Terrain layer at a world position: the cell's material, with borders broken up into pixel clumps
// (the lookup is jittered per 2x2 texel block) and steep slopes shown as bare dirt.
float TerrainLayer(float3 p, float3 n)
{
    float tpu = _FL_TexelsPerUnit;
    float2 texel = floor(p.xz * tpu);
    float2 block = floor(texel / 2);
    float2 jitter = (Hash22(block) - 0.5) * 0.85;
    float2 q = (texel + 0.5) / tpu + jitter;
    int2 cell = clamp(int2(floor(q)), int2(0, 0), int2(_FL_MatMapSize.xy) - 1);
    float layer = floor(LOAD_TEXTURE2D(_FL_MatMap, cell).r * 255 + 0.5);
    bool soft = layer == _FL_LayerGrass || layer == _FL_LayerLitter;
    if (soft && n.y < 0.82) layer = _FL_LayerDirt;
    return layer;
}

// Large-scale brightness variation (about 10 units across), constant per texture pixel so pixels stay flat.
// Breaks up visible tiling on big ground areas.
float MacroVariation(float2 pxz)
{
    float2 texel = (floor(pxz * _FL_TexelsPerUnit) + 0.5) / _FL_TexelsPerUnit;
    float2 q = texel / 9.0;
    float2 i = floor(q), f = frac(q);
    f = f * f * (3 - 2 * f);
    float a = Hash22(i).x, b = Hash22(i + float2(1, 0)).x, c = Hash22(i + float2(0, 1)).x, d = Hash22(i + float2(1, 1)).x;
    float v = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    return 1 + (v - 0.5) * 0.16;
}

half3 FlatShade(float3 posWS, float3 n, half3 albedo)
{
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
    Light light = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));
    half ndl = saturate(dot(n, light.direction));
    half3 direct = light.color * (ndl * light.shadowAttenuation);
    half3 amb = lerp(_FL_AmbientGround.rgb, _FL_AmbientSky.rgb, n.y * 0.5 + 0.5);
    return albedo * (direct + amb);
}

float3 _LightDirection;
float3 _LightPosition;

float4 ShadowCasterClip(float3 posWS, float3 normalWS)
{
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDir = normalize(_LightPosition - posWS);
#else
    float3 lightDir = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, lightDir));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return positionCS;
}

#endif
