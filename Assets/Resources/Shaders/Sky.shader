// The sky: haze at the horizon (the same colour the ground fades into, see FL_Fog) rising to the zenith colour, a glow
// round the sun, the sun's or moon's disc, slow clouds, and stars at night. All colours come from Atmosphere.cs.
Shader "Firstlight/Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "FlatCommon.hlsl"

            float4 _FL_SunDisc;    // rgb = colour of the disc, a = cosine of its angular radius
            float4 _FL_SkyCloud;   // rgb = colour of the clouds, a = how much of them shows
            float _FL_Stars;       // 0 by day, 1 at deep night

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float up = saturate(dir.y);
                half3 c = lerp(_FL_FogColor.rgb, _FL_SkyZenith.rgb, 1 - pow(1 - up, 2.5));
                // Stars: one per small cell of the sky, a few of them, brighter higher up.
                if (_FL_Stars > 0.01)
                {
                    float3 q = floor(dir * 240);
                    float h = Hash22(q.xz + q.y * 37.13).x;
                    c += step(0.9972, h) * _FL_Stars * saturate(dir.y * 4) * (0.5 + 0.5 * Hash22(q.zx + 3.1).y);
                }
                // Clouds on a flat layer overhead, drifting with the cloud shadows.
                float2 uv = dir.xz / (dir.y + 0.14) * 0.9 + _FL_Cloud.xy * 3.0;
                float n = VNoise(uv) * 0.6 + VNoise(uv * 2.6 + 4.7) * 0.4;
                float cover = smoothstep(0.5, 0.74, n) * saturate(dir.y * 5) * _FL_SkyCloud.a;
                c = lerp(c, _FL_SkyCloud.rgb, cover);
                float sd = saturate(dot(dir, _FL_SunDir.xyz));
                c += _FL_FogSun.rgb * pow(sd, _FL_FogSun.a) * (1 - cover * 0.7);
                c += _FL_SunDisc.rgb * smoothstep(_FL_SunDisc.a, _FL_SunDisc.a + 0.0004, sd) * (1 - cover);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
