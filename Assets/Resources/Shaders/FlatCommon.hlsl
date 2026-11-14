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
float _FL_LayerDirt, _FL_LayerGrass, _FL_LayerLitter, _FL_LayerSnow, _FL_LayerShallow;
float _FL_LayerLeaves, _FL_LayerNeedles, _FL_LayerTuft, _FL_LayerBush, _FL_LayerFlowers;

// Seasons and time of day (Atmosphere.cs): tints for grass and foliage, snow cover 0..1, glow of fires.
float4 _FL_SeasonGrass;    // rgb multiplier for grass, litter, tufts, bushes, flowers
float4 _FL_SeasonLeaf;     // rgb multiplier for broadleaf leaves
float4 _FL_SeasonNeedle;   // rgb multiplier for conifer needles
float _FL_Snow;            // 0 = none, 1 = full cover on flat ground
float _FL_Glow;            // brightness of unlit (glowing) materials

// Fires at night (SettlementView): up to 16 burning fires near the camera, xyz = flame position, w = reach. A warm
// pool of light in pixel-sized steps on everything round them; _FL_FireStrength is 0 by day.
float4 _FL_Fires[16];
float _FL_FireCount;
float _FL_FireStrength;

// Canopy cut-away (SettlementView): leaves and needles of trees near this point are dithered away when zoomed in, so
// the people working under them can be seen. xz = centre, z unused, w = radius; _FL_CutStrength 0..1.
float4 _FL_Cut;
float _FL_CutStrength;



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

half3 SeasonTint(half3 albedo, float layer)
{
    if (layer == _FL_LayerGrass || layer == _FL_LayerLitter || layer == _FL_LayerTuft || layer == _FL_LayerBush || layer == _FL_LayerFlowers) return albedo * _FL_SeasonGrass.rgb;
    if (layer == _FL_LayerLeaves) return albedo * _FL_SeasonLeaf.rgb;
    if (layer == _FL_LayerNeedles) return albedo * _FL_SeasonNeedle.rgb;
    return albedo;
}

// Snow lies on upward-facing surfaces in pixel clumps (one decision per 2x2 texture pixels), thicker on flat
// ground, so it reads as pixel art and comes and goes with _FL_Snow.
half3 ApplySnow(half3 albedo, float3 posWS, float3 n, float3 pBox, float3 nBox)
{
    if (_FL_Snow <= 0.001 || n.y < 0.3) return albedo;
    float2 block = floor(posWS.xz * _FL_TexelsPerUnit / 2) + floor(posWS.y * _FL_TexelsPerUnit / 2) * 17.0;
    float h = frac(sin(dot(block, float2(12.9898, 78.233))) * 43758.5453);
    float cover = _FL_Snow * smoothstep(0.3, 0.85, n.y) * 1.15;
    if (h > cover) return albedo;
    return SampleBoxProjected(pBox, nBox, _FL_LayerSnow, 0).rgb;
}

// Flowing water: the texture is carried along the simulated flow (flow = velocity in units/s) using two
// phases half a cycle apart, cross-faded so the texture never stretches far. Shallow water shows the
// shallow texture (bed visible), deep water the deep one.
half3 SampleWater(float3 posWS, float2 flow, float depth01, float layer)
{
    const float period = 1.6;
    float k = _FL_TexelsPerUnit / _FL_TileTexels;
    float2 base = posWS.xz * k;
    float t = _Time.y / period;
    float p0 = frac(t), p1 = frac(t + 0.5);
    float wB = abs(p0 * 2 - 1);
    float2 dx = ddx(base), dy = ddy(base);
    float2 o0 = flow * (p0 * period) * k, o1 = flow * (p1 * period) * k;
    half3 deepA = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FL_Tex, sampler_FL_Tex, base - o0, layer, dx, dy).rgb;
    half3 deepB = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FL_Tex, sampler_FL_Tex, base - o1 + 0.37, layer, dx, dy).rgb;
    half3 shA = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FL_Tex, sampler_FL_Tex, base - o0, _FL_LayerShallow, dx, dy).rgb;
    half3 shB = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FL_Tex, sampler_FL_Tex, base - o1 + 0.37, _FL_LayerShallow, dx, dy).rgb;
    half3 deep = lerp(deepA, deepB, wB), shallow = lerp(shA, shB, wB);
    return lerp(shallow, deep, smoothstep(0.15, 0.75, depth01));
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

half3 FireLight(float3 posWS, float3 n)
{
    if (_FL_FireStrength <= 0.001 || _FL_FireCount < 0.5) return 0;
    // Lit per texture pixel, in a few steps, so the pool reads as pixel art rather than a smooth gradient.
    float3 p = (floor(posWS * _FL_TexelsPerUnit) + 0.5) / _FL_TexelsPerUnit;
    float sum = 0;
    [loop] for (int i = 0; i < 16; i++)
    {
        if (i >= (int)_FL_FireCount) break;
        float3 d = _FL_Fires[i].xyz - p;
        float dist = length(d);
        float k = saturate(1 - dist / _FL_Fires[i].w);
        float facing = saturate(dot(n, d / max(dist, 0.001)) * 0.6 + 0.45);
        sum += k * k * facing;
    }
    sum = floor(min(sum, 1.5) * 8) / 8;
    return half3(1.0, 0.56, 0.26) * (sum * _FL_FireStrength);
}

// How much of a tree's foliage near the cut-away point is removed (per instance, in the vertex shader).
float CanopyFade(float3 instPos)
{
    if (_FL_CutStrength <= 0.001) return 0;
    float dist = distance(instPos.xz, _FL_Cut.xy);
    float r = _FL_Cut.w * _FL_CutStrength;          // the cut grows as the camera comes in
    return 1 - smoothstep(r - 1.6, r, dist);
}

static const float FL_Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

// Screen-door dither: true where a pixel of something faded by `fade` should be dropped (4x4 Bayer, 2x2 blocks).
bool DitherDrop(float2 screenPx, float fade)
{
    if (fade <= 0.001) return false;
    uint2 q = (uint2)(screenPx * 0.5) & 3;
    return (FL_Bayer[q.y * 4 + q.x] + 0.5) / 16.0 < fade;
}

bool IsFoliage(float layer) { return layer == _FL_LayerLeaves || layer == _FL_LayerNeedles; }

half3 FlatShade(float3 posWS, float3 n, half3 albedo)
{
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
    Light light = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));
    half ndl = saturate(dot(n, light.direction));
    half3 direct = light.color * (ndl * light.shadowAttenuation);
    half3 amb = lerp(_FL_AmbientGround.rgb, _FL_AmbientSky.rgb, n.y * 0.5 + 0.5);
    return albedo * (direct + amb + FireLight(posWS, n));
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
