// Pixel-art material shader for regular meshes (terrain, water, buildings).
// Vertex data: COLOR = tint, TEXCOORD0 = explicit UV, TEXCOORD1 = (material layer, uv mode: 0 box projection, 1 explicit).
// _UseMatMap = 1 takes the layer from the terrain material map instead.
// Everything drawn with this shader is ground (terrain, water, outskirts): its visible pixels set stencil bit 1, so
// highlights hidden under the ground can be drawn through it (InstancedFlat's x-ray material).
Shader "Firstlight/FlatLit"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _UseMatMap ("Terrain material map", Float) = 0
        _UVScroll ("Scroll (texels/sec x, y)", Vector) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "FlatCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _UseMatMap;
            float4 _UVScroll;
            float _Cull;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Stencil { Ref 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 positionOS : TEXCOORD1;
                half4 color : COLOR; float2 uv : TEXCOORD2; nointerpolation float2 mat : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color * _BaseColor;
                o.uv = v.uv;
                o.mat = v.mat;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = FlatNormal(i.positionWS);
                float layer = i.mat.x;
                if (_UseMatMap > 0.5) layer = TerrainLayer(i.positionWS, n);
                if (i.mat.y > 1.5)
                {
                    return half4(FL_Fog(WaterShade(i.positionWS, n, i.uv, i.color.a, layer, i.positionCS), i.positionWS), 1);
                }
                float2 scroll = floor(_Time.y * _UVScroll.xy) / _FL_TileTexels;
                half4 t = i.mat.y > 0.5 ? SampleExplicit(i.uv + scroll, layer) : SampleBoxProjected(i.positionOS, n, layer, scroll);
                clip(t.a - 0.5);
                half3 albedo = t.rgb * i.color.rgb;
                if (_UseMatMap > 0.5)
                {
                    albedo = SeasonTint(GroundVariation(albedo, i.positionWS.xz, layer), layer);
                    albedo = ApplySnow(albedo, i.positionWS, n, i.positionOS, n);
                }
                half3 c = FlatShade(i.positionWS, n, albedo, i.positionCS, 0);
                return half4(FL_Fog(c, i.positionWS), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 mat : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = ShadowCasterClip(TransformObjectToWorld(v.positionOS.xyz), TransformObjectToWorldNormal(v.normalOS));
                o.uv = v.uv; o.mat = v.mat;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                if (i.mat.y > 0.5) clip(SAMPLE_TEXTURE2D_ARRAY_LOD(_FL_Tex, sampler_FL_Tex, i.uv, i.mat.x, 0).a - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 mat : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.mat = v.mat; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                if (i.mat.y > 0.5) clip(SAMPLE_TEXTURE2D_ARRAY_LOD(_FL_Tex, sampler_FL_Tex, i.uv, i.mat.x, 0).a - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
}
