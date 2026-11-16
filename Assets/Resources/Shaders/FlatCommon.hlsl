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
float _FL_LayerDirt, _FL_LayerGrass, _FL_LayerLitter, _FL_LayerSnow, _FL_LayerShallow, _FL_LayerBed, _FL_LayerPath;
float _FL_LayerLeaves, _FL_LayerNeedles, _FL_LayerTuft, _FL_LayerBush, _FL_LayerFlowers, _FL_LayerReeds, _FL_LayerFern;

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

// The look (Atmosphere.cs). Haze: the far ground, the ground beyond the map's border and low morning mist fade into
// the sky's horizon colour, warmer toward the sun. Clouds: slow shadows drifting over the land. Wind: plants sway.
float4 _FL_FogColor;       // rgb = horizon haze (the sky shader draws the same colour at the horizon)
float4 _FL_FogSun;         // rgb = glow added when looking toward the sun, a = how tight the glow is
float4 _FL_FogParams;      // x = start distance, y = 1 / range, z = strongest, w = mist strength
float4 _FL_FogHeight;      // x = top of the mist (world y), y = 1 / thickness
float4 _FL_MapBounds;      // xy = map size, z = 1 / fade distance beyond the border
float4 _FL_SunDir;         // xyz = toward the sun (or moon)
float4 _FL_Cloud;          // xy = drift offset, z = 1 / size, w = how dark the shadows are
float4 _FL_Wind;           // xy = direction, z = strength, w = time
float4 _FL_SkyZenith;      // rgb = sky overhead
float4 _FL_WaterSky;       // rgb = what the water mirrors at a low angle, a = strength of sun glints
float4 _FL_Fx;             // the player's strengths (Look.Strength): x = soft shadowing in creases, y = haze beyond the border



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
    if (layer == _FL_LayerGrass || layer == _FL_LayerLitter || layer == _FL_LayerTuft || layer == _FL_LayerBush || layer == _FL_LayerFlowers || layer == _FL_LayerReeds || layer == _FL_LayerFern) return albedo * _FL_SeasonGrass.rgb;
    if (layer == _FL_LayerLeaves) return albedo * _FL_SeasonLeaf.rgb;
    if (layer == _FL_LayerNeedles) return albedo * _FL_SeasonNeedle.rgb;
    return albedo;
}

float2 Hash22(float2 p);
float VNoise(float2 q);

// The colour of lying snow at a point: its texture, drifts (hollows a touch darker and bluer than the crests, in broad
// soft shapes, constant per texture pixel) and, by day, a scatter of pixels that glitter on and off.
half3 SnowColour(float3 posWS, float3 pBox, float3 nBox)
{
    half3 s = SampleBoxProjected(pBox, nBox, _FL_LayerSnow, 0).rgb;
    float2 px = floor(posWS.xz * _FL_TexelsPerUnit);
    float2 texel = (px + 0.5) / _FL_TexelsPerUnit;
    // The snow lies in low swells a few metres across: the side of a swell toward the sun is brighter and warmer, its
    // lee darker and bluer (in a few flat steps, like the texture's own ripples).
    float2 toSun = normalize(_FL_SunDir.xz + float2(0.0001, 0));
    float2 ahead = texel + toSun * 0.3;
    float h0 = VNoise(texel / 1.7 + 17.3) * 0.6 + VNoise(texel / 5.5 + 4.2) * 0.4;
    float h1 = VNoise(ahead / 1.7 + 17.3) * 0.6 + VNoise(ahead / 5.5 + 4.2) * 0.4;
    float lit = floor(saturate(0.5 + (h0 - h1) * 5.0) * 4 + 0.5) / 4;
    s *= lerp(half3(0.83, 0.89, 1.0), half3(1.05, 1.04, 1.01), lit);
    float2 h = Hash22(px + floor(posWS.y * _FL_TexelsPerUnit) * 13.0);
    if (h.x > 0.9935 && sin(_Time.y * (1.2 + h.y * 2.5) + h.x * 900.0) > 0.1) s *= 1 + 1.5 * _FL_WaterSky.a;
    return s;
}

// Snow lies on upward-facing surfaces, thickest on flat ground. Where it is thin (slopes, the first and last days,
// trodden ground) it breaks up into soft-edged patches a few pixels across rather than single specks, so a roof or a
// tree carries drifts of snow, not salt. `amount` scales the cover (trodden ground holds less); `tint` colours it.
half3 SnowOn(half3 albedo, float3 posWS, float3 n, float3 pBox, float3 nBox, float amount, half3 tint)
{
    if (_FL_Snow <= 0.001 || n.y < 0.3) return albedo;
    float2 block = floor((posWS.xz + posWS.y * float2(0.37, 0.61)) * _FL_TexelsPerUnit / 2);   // height slides the pattern along, so slopes get patches, not stripes
    float speck = frac(sin(dot(block, float2(12.9898, 78.233))) * 43758.5453);
    float patch = VNoise(block / 6.0 + 3.7) * 0.6 + VNoise(block / 17.0 + 8.9) * 0.25 + speck * 0.15;
    float cover = _FL_Snow * smoothstep(0.3, 0.85, n.y) * 1.15 * amount;
    // patch is mostly between 0.25 and 0.75: full cover hides everything, half cover leaves about half in patches.
    if (patch * 0.9 + 0.05 > cover) return albedo;
    return SnowColour(posWS, pBox, nBox) * tint;
}

half3 ApplySnow(half3 albedo, float3 posWS, float3 n, float3 pBox, float3 nBox)
{
    return SnowOn(albedo, posWS, n, pBox, nBox, 1, half3(1, 1, 1));
}

// Snow on the ground: trodden earth and paths hold less of it, and what lies there is packed and greyer.
half3 ApplyGroundSnow(half3 albedo, float3 posWS, float3 n, float layer)
{
    bool trodden = layer == _FL_LayerDirt || layer == _FL_LayerPath;
    return SnowOn(albedo, posWS, n, posWS, n, trodden ? 0.74 : 1, trodden ? half3(0.86, 0.85, 0.83) : half3(1, 1, 1));
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

float VNoise(float2 q)
{
    float2 i = floor(q), f = frac(q);
    f = f * f * (3 - 2 * f);
    float a = Hash22(i).x, b = Hash22(i + float2(1, 0)).x, c = Hash22(i + float2(0, 1)).x, d = Hash22(i + float2(1, 1)).x;
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Gradient noise (about 0..1, centred on 0.5): rounder than VNoise, with no hint of the grid it is made on.
float GNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * f * (f * (f * 6 - 15) + 10);
    float a = dot(Hash22(i) * 2 - 1, f), b = dot(Hash22(i + float2(1, 0)) * 2 - 1, f - float2(1, 0));
    float c = dot(Hash22(i + float2(0, 1)) * 2 - 1, f - float2(0, 1)), d = dot(Hash22(i + float2(1, 1)) * 2 - 1, f - float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 0.75 + 0.5;
}

// Billowing shapes for clouds, fog and mist: three sizes of gradient noise laid over each other, the whole thing
// pushed about by a slower noise so the outlines curl instead of following straight lines.
float Billow(float2 p)
{
    float2 w = float2(GNoise(p * 0.6 + 3.1), GNoise(p * 0.6 + 9.4)) - 0.5;
    p += w * 1.5;
    return GNoise(p) * 0.55 + GNoise(p * 2.07 + 11.3) * 0.28 + GNoise(p * 4.3 + 5.7) * 0.17;
}

// A point's position along and across the wind: noise stretched along the wind gives wisps.
float2 WindSpace(float2 pxz)
{
    return float2(dot(pxz, _FL_Wind.xy), dot(pxz, float2(-_FL_Wind.y, _FL_Wind.x)));
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
    // Beyond the map's border: open grassland and forest floor in broad patches (the river's bed stays, under its water).
    float2 o = max(max(-q, q - _FL_MatMapSize.xy), 0);
    if (o.x + o.y > 0 && layer != _FL_LayerBed)
        layer = VNoise(q / 14.0 + 9.7) * 0.6 + VNoise(q / 5.0 + 2.1) * 0.4 > 0.6 ? _FL_LayerLitter : _FL_LayerGrass;
    bool soft = layer == _FL_LayerGrass || layer == _FL_LayerLitter;
    if (soft && n.y < 0.82) layer = _FL_LayerDirt;
    return layer;
}

// Large-scale variation of the ground, constant per texture pixel so pixels stay flat: brightness at three sizes
// (a few units, a field, a hillside), and on grass and forest floor drier, yellower stretches between lusher ones.
// Breaks up visible tiling and makes a wide view read as land rather than a pattern.
half3 GroundVariation(half3 albedo, float2 pxz, float layer)
{
    float2 texel = (floor(pxz * _FL_TexelsPerUnit) + 0.5) / _FL_TexelsPerUnit;
    float a = VNoise(texel / 9.0), b = VNoise(texel / 37.0 + 13.7), c = VNoise(texel / 2.6 + 5.1);
    albedo *= 1 + (a - 0.5) * 0.15 + (b - 0.5) * 0.16 + (c - 0.5) * 0.07;
    if (layer == _FL_LayerGrass || layer == _FL_LayerLitter)
    {
        float dry = smoothstep(0.42, 0.72, VNoise(texel / 21.0 + 41.3) * 0.65 + b * 0.35);
        albedo = lerp(albedo, albedo * half3(1.10, 1.02, 0.82), dry * 0.45);
        float lush = smoothstep(0.55, 0.8, VNoise(texel / 15.0 + 77.7));
        albedo = lerp(albedo, albedo * half3(0.86, 0.97, 0.88), lush * 0.5);
    }
    return albedo;
}

// Shadows of slow clouds, as seen from the sun: 1 = open sky, lower under a cloud. Soft-edged, rounded shapes.
half CloudShadow(float3 posWS)
{
    if (_FL_Cloud.w <= 0.001) return 1;
    float2 p = (posWS.xz - _FL_SunDir.xz / max(_FL_SunDir.y, 0.25) * posWS.y) * _FL_Cloud.z + _FL_Cloud.xy;
    return 1 - smoothstep(0.5, 0.64, Billow(p * 1.3)) * _FL_Cloud.w;
}

// Haze: distance, the fog bank beyond the map's border, and low mist; its colour is the sky's at the horizon.
half3 FL_Fog(half3 c, float3 posWS)
{
    float3 toP = posWS - GetCameraPositionWS();
    float d = max(length(toP), 0.001);
    float f = saturate((d - _FL_FogParams.x) * _FL_FogParams.y);
    f = f * (2 - f) * _FL_FogParams.z;
    float2 wp = WindSpace(posWS.xz);
    float drift = _FL_Wind.w * 0.35;
    // Beyond the border a fog bank closes over the land. Its edge is not the border's straight line: it billows in and
    // out by some 35 units, in tongues drawn out along the wind that drift slowly, thinner where the land stands higher.
    float2 o = max(max(-posWS.xz, posWS.xz - _FL_MapBounds.xy), 0);
    float od = length(o);
    if (od > 0)
    {
        float bank = Billow(float2(wp.x * 0.55 - drift, wp.y) / 24.0);
        float wisp = GNoise(float2(wp.x * 0.3 - drift * 1.7, wp.y) / 5.0);
        float back = (smoothstep(0.3, 0.7, bank) * 24.0 + wisp * 5.0 + max(posWS.y, 0) * 1.0) * saturate((76.0 - od) / 36.0);   // none of it by the end of the drawn land
        float dense = saturate(max(od - back, 0) * _FL_MapBounds.z);
        dense = dense * dense * (3 - 2 * dense);
        // In front of the bank a thin veil thickens with distance, in streaks, so the border itself never shows as a line.
        float veil = saturate(od / 45.0) * (0.22 + 0.5 * wisp) * _FL_Fx.y;
        float outside = max(dense, veil);
        f = 1 - (1 - f) * (1 - outside);
    }
    // Mist lies low in drifting banks with wispy tops rather than as one flat sheet.
    if (_FL_FogParams.w > 0.001)
    {
        float banks = Billow(float2(wp.x * 0.45 - drift, wp.y) / 13.0 + 31.7);
        float top = _FL_FogHeight.x + (banks - 0.5) * 2.2;
        float lying = saturate((top - posWS.y) * _FL_FogHeight.y);
        float mist = _FL_FogParams.w * lying * (0.3 + 1.1 * smoothstep(0.34, 0.66, banks)) * saturate(d * 0.012);
        f = 1 - (1 - f) * (1 - saturate(mist));
    }
    float3 dir = toP / d;
    half glow = pow(saturate(dot(dir, _FL_SunDir.xyz)), _FL_FogSun.a);
    return lerp(c, _FL_FogColor.rgb + _FL_FogSun.rgb * glow, f);
}

// Plants sway in the wind: more the higher up the plant, in gusts that travel across the land. World-space offset.
float3 WindSway(float3 local, float3 instPos, float layer)
{
    if (_FL_Wind.z <= 0.001) return 0;
    float k = layer == _FL_LayerLeaves ? 0.03 : layer == _FL_LayerNeedles ? 0.018
            : layer == _FL_LayerTuft || layer == _FL_LayerFlowers || layer == _FL_LayerReeds ? 0.26
            : layer == _FL_LayerBush || layer == _FL_LayerFern ? 0.07 : 0;
    if (k == 0) return 0;
    float ph = dot(instPos.xz, float2(0.37, 0.23));
    float t = _FL_Wind.w;
    float gust = sin(t * 1.3 + ph) * 0.6 + sin(t * 2.9 + ph * 1.7 + local.x * 2.0) * 0.4;
    float front = 0.55 + 0.45 * sin(dot(instPos.xz, _FL_Wind.xy) * 0.09 - t * 0.7);
    float a = k * _FL_Wind.z * max(local.y, 0) * (gust * front + 0.35);
    return float3(_FL_Wind.x * a, 0, _FL_Wind.y * a);
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

// Plants let some light through: lit a little from behind too.
half PlantWrap(float layer)
{
    return IsFoliage(layer) || layer == _FL_LayerTuft || layer == _FL_LayerBush || layer == _FL_LayerFlowers || layer == _FL_LayerReeds || layer == _FL_LayerFern ? 0.45 : 0;
}

// Sun (with shadows and cloud shadows) + sky and ground ambient + firelight. Creases and the ground round things are
// darkened by the screen-space ambient occlusion when the effects are on (mostly the ambient, a little of the sun).
half3 FlatShade(float3 posWS, float3 n, half3 albedo, float4 positionCS, half wrap)
{
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
    Light light = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));
    half ndl = saturate((dot(n, light.direction) + wrap) / (1 + wrap));
    half3 direct = light.color * (ndl * light.shadowAttenuation * CloudShadow(posWS));
    half3 amb = lerp(_FL_AmbientGround.rgb, _FL_AmbientSky.rgb, n.y * 0.5 + 0.5);
    half ao = 1;
#if defined(_SCREEN_SPACE_OCCLUSION)
    ao = saturate(1 - (1 - SampleAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS))) * _FL_Fx.x);
#endif
    return albedo * (direct * lerp(1, ao, 0.3) + amb * ao + FireLight(posWS, n));
}

// The river's surface: the flowing texture, lit; the sky mirrored at a low angle; twinkling glints where the sun
// reflects toward the camera; a broken line of foam along the shore. All in texture-pixel steps.
half3 WaterShade(float3 posWS, float3 n, float2 flow, float depth01, float layer, float4 positionCS)
{
    half3 base = SampleWater(posWS, flow, depth01, layer);
    float tpu = _FL_TexelsPerUnit;
    float t = _Time.y;
    // Foam: where the water is thinnest, in 2x2 pixel clumps that come and go.
    float2 fb = floor(posWS.xz * tpu / 2);
    float2 fhh = Hash22(fb);
    float fh = frac(fhh.x + floor(t * 1.5 + fhh.y * 4) * 0.37);
    float foam = step(depth01 + fh * 0.16, 0.13);
    base = lerp(base, half3(0.80, 0.85, 0.84), foam * 0.8);
    // Winter: ice grows out from the banks over the shallows (never right across: people still fetch water), with a
    // ragged edge and a dusting of snow on it.
    float ice = 0;
    if (_FL_Snow > 0.01)
    {
        float2 ib = floor(posWS.xz * tpu / 2);
        float ragged = VNoise(ib / 9.0 + 5.3) * 0.7 + Hash22(ib).x * 0.3;
        ice = step(depth01 + ragged * 0.3, 0.16 + 0.42 * _FL_Snow);
        half3 iceCol = lerp(half3(0.62, 0.74, 0.80), SnowColour(posWS, posWS, float3(0, 1, 0)), step(ragged, 0.25 + 0.35 * _FL_Snow));
        base = lerp(base, iceCol, ice);
        foam *= 1 - ice;
    }
    half3 c = FlatShade(posWS, n, base, positionCS, 0);
    float3 v = normalize(GetCameraPositionWS() - posWS);
    half fres = pow(1 - saturate(v.y), 3);
    c = lerp(c, _FL_WaterSky.rgb, fres * 0.6 * (1 - foam) * (1 - ice * 0.7));
    // Glints: cells carried along by the flow, each lit for a moment, brightest along the sun's reflection.
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
    Light light = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));
    float2 gc = floor((posWS.xz - flow * t * 0.6) * tpu / 3);
    float2 gh = Hash22(gc);
    float tw = frac(gh.x + t * (0.25 + gh.y * 0.35));
    float3 h = normalize(light.direction + v);
    float lobe = pow(saturate(h.y), 24);
    float glint = step(0.93, tw) * step(0.55, gh.y) * lobe;
    c += light.color * light.shadowAttenuation * CloudShadow(posWS) * (glint * 2.2 + lobe * 0.10) * _FL_WaterSky.a * (1 - ice);
    return c;
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
