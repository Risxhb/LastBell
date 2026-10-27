// Area 3 water far below: glowing turquoise with slow caustic shimmer, drifting foam, and animated foam rings where
// each waterfall lands (impact list from LookSettings / the builder), sinking into the shared cloud fog.
Shader "LastBell/Area3/Pool"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.07, 0.30, 0.31, 1)
        _Glow ("Glow", Color) = (0.32, 0.88, 0.82, 1)
        _Foam ("Foam", Color) = (0.85, 0.97, 0.94, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LBCloud.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Deep, _Glow, _Foam;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct Vt { float4 positionCS : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; };
            Vt vert(A v) { Vt o; o.w = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.w); o.fog = ComputeFogFactor(o.positionCS.z); return o; }
            half4 frag(Vt i) : SV_Target
            {
                float2 p = i.w.xz * 0.08; float t = _LB_Time;
                float c = lbcNoise(p + t * 0.05) * 0.5 + lbcNoise(p * 2.3 - t * 0.07) * 0.5;
                float caustic = smoothstep(0.55, 0.95, lbcNoise(p * 3.7 + float2(t * 0.11, -t * 0.08)) * lbcNoise(p * 4.3 - t * 0.09) * 2);
                float drift = smoothstep(0.7, 0.9, lbcNoise(p * 0.6 + _LB_Wind.xy * t * 0.02) * 0.6 + lbcNoise(p * 3.1) * 0.4);
                float rings = 0, splash = 0;
                for (int k = 0; k < 24; k++)
                {
                    if (k >= (int)_LB_ImpactCount) break;
                    float d = distance(i.w.xz, _LB_Impacts[k].xy), r = _LB_Impacts[k].w * 0.5 + 6;
                    float fade = 1 - saturate(d / r);
                    rings = max(rings, smoothstep(0.55, 1, sin(d * 2.2 - t * 3.5 + k) * 0.5 + 0.5) * fade);
                    splash = max(splash, smoothstep(0.4, 1, fade) * (0.6 + 0.4 * lbcNoise(i.w.xz * 1.5 + t * 2)));
                }
                float3 col = lerp(_Deep.rgb, _Glow.rgb, smoothstep(0.3, 0.8, c) * 0.7 + exp(-length(i.w.xz) * 0.015) * 0.5);
                col += _Glow.rgb * caustic * 0.25 + _Foam.rgb * (drift * 0.3 + rings * 0.55 + splash * 0.7);
                col = lerp(lbcApply(col, i.w), col, 0.7); // the water stays turquoise where the cloud sea opens
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
