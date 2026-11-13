// Draws many copies of one mesh from a GPU buffer (citizens, trees, rocks, animals, plants).
// Each instance: position, yaw, scale, packed sRGB colour. Vertex COLOR = tint, alpha = how much the instance
// colour applies. TEXCOORD1 = (material layer, uv mode). Box projection uses the instance's own (scaled)
// local space, so texture pixels stay the same size on every copy and turn with it.
Shader "Firstlight/InstancedFlat"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "FlatCommon.hlsl"
        struct InstanceData { float3 pos; float yaw; float3 scale; uint color; };
        StructuredBuffer<InstanceData> _Instances;

        float3 Rotate(float yaw, float3 p)
        {
            float s, c; sincos(yaw, s, c);
            return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
        }
        float3 Unrotate(float yaw, float3 p)
        {
            float s, c; sincos(yaw, s, c);
            return float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
        }
        half4 UnpackColor(uint c)
        {
            return half4((c & 255) / 255.0, ((c >> 8) & 255) / 255.0, ((c >> 16) & 255) / 255.0, ((c >> 24) & 255) / 255.0);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; uint id : SV_InstanceID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 local : TEXCOORD1;
                half3 color : COLOR; float2 uv : TEXCOORD2; nointerpolation float3 mat : TEXCOORD3; half fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                o.local = v.positionOS.xyz * d.scale;
                o.positionWS = Rotate(d.yaw, o.local) + d.pos;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                half4 ic = UnpackColor(d.color);
                ic.rgb = SRGBToLinear(ic.rgb);
                o.color = v.color.rgb * lerp(half3(1, 1, 1), ic.rgb, v.color.a);
                o.uv = v.uv;
                o.mat = float3(v.mat, d.yaw);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = FlatNormal(i.positionWS);
                half4 t = i.mat.y > 0.5 ? SampleExplicit(i.uv, i.mat.x) : SampleBoxProjected(i.local, Unrotate(i.mat.z, n), i.mat.x, 0);
                clip(t.a - 0.5);
                half3 c = FlatShade(i.positionWS, n, t.rgb * i.color);
                return half4(MixFog(c, i.fog), 1);
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; uint id : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 mat : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                float3 n = normalize(Rotate(d.yaw, v.normalOS / d.scale));
                o.positionCS = ShadowCasterClip(Rotate(d.yaw, v.positionOS.xyz * d.scale) + d.pos, n);
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; uint id : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 mat : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                o.positionCS = TransformWorldToHClip(Rotate(d.yaw, v.positionOS.xyz * d.scale) + d.pos);
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
    }
}
