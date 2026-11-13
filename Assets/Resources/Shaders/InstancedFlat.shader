// Draws many copies of one mesh from a GPU buffer (citizens, trees, rocks, animals).
// Each instance: position, yaw, scale, packed RGBA colour. Vertex colour alpha decides how much the
// instance colour tints that vertex (alpha 0 keeps the mesh colour, e.g. tree trunks).
Shader "Firstlight/InstancedFlat"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "FlatCommon.hlsl"
        struct InstanceData { float3 pos; float yaw; float3 scale; uint color; };
        StructuredBuffer<InstanceData> _Instances;

        float3 InstanceToWorld(InstanceData d, float3 p)
        {
            p *= d.scale;
            float s, c; sincos(d.yaw, s, c);
            return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z) + d.pos;
        }
        float3 InstanceDirToWorld(InstanceData d, float3 n)
        {
            n /= d.scale;
            float s, c; sincos(d.yaw, s, c);
            return normalize(float3(c * n.x + s * n.z, n.y, -s * n.x + c * n.z));
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
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; uint id : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 color : COLOR; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                o.positionWS = InstanceToWorld(d, v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                half4 ic = UnpackColor(d.color);
                ic.rgb = SRGBToLinear(ic.rgb);
                o.color = lerp(v.color.rgb, v.color.rgb * ic.rgb, v.color.a);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = FlatShade(i.positionWS, i.color);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint id : SV_InstanceID; };
            float4 vert(Attributes v) : SV_POSITION
            {
                InstanceData d = _Instances[v.id];
                return ShadowCasterClip(InstanceToWorld(d, v.positionOS.xyz), InstanceDirToWorld(d, v.normalOS));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; uint id : SV_InstanceID; };
            float4 vert(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(InstanceToWorld(_Instances[v.id], v.positionOS.xyz));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
