#ifndef FIRSTLIGHT_FLAT_COMMON
#define FIRSTLIGHT_FLAT_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Hemisphere ambient, set globally by the game (keeps every Firstlight material consistent).
float4 _FL_AmbientSky;
float4 _FL_AmbientGround;

// Faceted normal from screen-space derivatives, flipped to face the camera so it is correct
// whatever the platform's y-flip is.
float3 FlatNormal(float3 posWS)
{
    float3 n = normalize(cross(ddy(posWS), ddx(posWS)));
    float3 toCam = GetCameraPositionWS() - posWS;
    return dot(n, toCam) < 0 ? -n : n;
}

half3 FlatShade(float3 posWS, half3 albedo)
{
    float3 n = FlatNormal(posWS);
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
