// Draws many copies of one mesh from a GPU buffer (citizens, trees, rocks, animals, plants, buildings).
// Each instance: position, yaw, scale, packed sRGB colour. Vertex COLOR = tint, alpha = how much the instance
// colour applies. TEXCOORD1 = (material layer, uv mode: 0 box projected, 1 explicit UV, 3 box projected and
// unlit = glowing). Box projection uses the instance's own (scaled) local space, so texture pixels stay the
// same size on every copy and turn with it.
// Instance colour alpha: 255 = normal; 1..254 = construction (nothing drawn above alpha/255 * 4 units: a site
// rising); 0 = placement ghost (the whole mesh washed with the instance colour).
// _Smoke = 1 (smoke puffs): the alpha is how solid the puff is, thinned out with a screen-door dither, and the puff is
// lit softly from all round instead of facet by facet.
Shader "Firstlight/InstancedFlat"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Snowable ("Takes snow", Float) = 1
        _Smoke ("Smoke puffs", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "FlatCommon.hlsl"
        float _Snowable;
        float _Smoke;
        #define BUILD_HEIGHT 4.0
        float BuildCut(uint color, float y)
        {
            float a = ((color >> 24) & 255) / 255.0;
            return a < 0.995 && a > 0.002 ? a * BUILD_HEIGHT - y : 1;
        }
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; uint id : SV_InstanceID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 local : TEXCOORD1;
                half3 color : COLOR; float2 uv : TEXCOORD2; nointerpolation float3 mat : TEXCOORD3;
                nointerpolation half4 inst : TEXCOORD5;    // instance colour (linear rgb, raw alpha)
                nointerpolation float cut : TEXCOORD6;     // canopy cut-away for this instance (trees near the focus)
            };

            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                o.local = v.positionOS.xyz * d.scale;
                o.positionWS = Rotate(d.yaw, o.local) + d.pos + WindSway(o.local, d.pos, v.mat.x);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                half4 ic = UnpackColor(d.color);
                ic.rgb = SRGBToLinear(ic.rgb);
                o.color = v.color.rgb * lerp(half3(1, 1, 1), ic.rgb, v.color.a);
                o.uv = v.uv;
                o.mat = float3(v.mat, d.yaw);
                o.inst = ic;
                o.cut = CanopyFade(d.pos);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                bool smoke = _Smoke > 0.5;
                if (smoke) { if (DitherDrop(i.positionCS.xy, 1 - i.inst.a)) discard; }
                else if (i.inst.a < 0.995 && i.inst.a > 0.002) clip(i.inst.a * BUILD_HEIGHT - i.local.y);
                if (i.cut > 0.001 && IsFoliage(i.mat.x) && DitherDrop(i.positionCS.xy, i.cut)) discard;
                float3 n = FlatNormal(i.positionWS);
                float3 nl = Unrotate(i.mat.z, n);
                bool explicitUV = i.mat.y > 0.5 && i.mat.y < 1.5;
                half4 t = explicitUV ? SampleExplicit(i.uv, i.mat.x) : SampleBoxProjected(i.local, nl, i.mat.x, 0);
                clip(t.a - 0.5);
                half3 albedo = SeasonTint(t.rgb, i.mat.x) * i.color;
                if (smoke)
                {
                    Light sun = GetMainLight();
                    half3 amb = lerp(_FL_AmbientGround.rgb, _FL_AmbientSky.rgb, 0.8) * 1.2;
                    amb = lerp(amb, dot(amb, half3(0.333, 0.333, 0.333)), 0.6);     // smoke is grey, whatever the sky
                    half3 lit = albedo * (amb + sun.color * (0.3 + 0.12 * saturate(n.y)) + FireLight(i.positionWS, n) * 0.5);
                    return half4(FL_Fog(lit, i.positionWS), 1);
                }
                bool ghost = i.inst.a <= 0.002;
                if (i.mat.y > 2.5)
                {
                    half3 g = albedo * _FL_Glow;
                    if (ghost) g = lerp(g, i.inst.rgb, 0.55);
                    return half4(FL_Fog(g, i.positionWS), 1);
                }
                if (_Snowable > 0.5 && !ghost) albedo = ApplySnow(albedo, i.positionWS, n, i.local, nl);
                half3 c = FlatShade(i.positionWS, n, albedo, i.positionCS, PlantWrap(i.mat.x));
                if (ghost) c = lerp(c, i.inst.rgb, 0.5) + i.inst.rgb * 0.12;
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 mat : TEXCOORD1; uint id : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 mat : TEXCOORD1; float cut : TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                float3 n = normalize(Rotate(d.yaw, v.normalOS / d.scale));
                float3 local = v.positionOS.xyz * d.scale;
                o.positionCS = ShadowCasterClip(Rotate(d.yaw, local) + d.pos + WindSway(local, d.pos, v.mat.x), n);
                o.uv = v.uv; o.mat = v.mat;
                o.cut = BuildCut(d.color, v.positionOS.y * d.scale.y);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(i.cut);
                if (i.mat.y > 0.5 && i.mat.y < 1.5) clip(SAMPLE_TEXTURE2D_ARRAY_LOD(_FL_Tex, sampler_FL_Tex, i.uv, i.mat.x, 0).a - 0.5);
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float2 mat : TEXCOORD1; float cut : TEXCOORD2; nointerpolation float canopy : TEXCOORD3; };
            Varyings vert(Attributes v)
            {
                InstanceData d = _Instances[v.id];
                Varyings o;
                float3 local = v.positionOS.xyz * d.scale;
                o.positionCS = TransformWorldToHClip(Rotate(d.yaw, local) + d.pos + WindSway(local, d.pos, v.mat.x));
                o.uv = v.uv; o.mat = v.mat;
                o.cut = _Smoke > 0.5 ? 1 : BuildCut(d.color, v.positionOS.y * d.scale.y);
                o.canopy = _Smoke > 0.5 ? 1 - ((d.color >> 24) & 255) / 255.0 : CanopyFade(d.pos);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(i.cut);
                if (i.canopy > 0.001 && (IsFoliage(i.mat.x) || _Smoke > 0.5) && DitherDrop(i.positionCS.xy, i.canopy)) discard;
                if (i.mat.y > 0.5 && i.mat.y < 1.5) clip(SAMPLE_TEXTURE2D_ARRAY_LOD(_FL_Tex, sampler_FL_Tex, i.uv, i.mat.x, 0).a - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
}
