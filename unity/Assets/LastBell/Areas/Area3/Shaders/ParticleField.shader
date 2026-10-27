// Area 3 particles without the Particle System module (it is disabled in this project): each mesh holds many quads whose
// four corners share a centre; the vertex shader animates and billboards them on the shared clock and wind (LBCloud.hlsl).
// uv = corner, uv2 = (seed, size), uv3 = (sheet height, sheet arc) for glints, normal = direction, colour = tint.
// _Mode 0 arena motes: drift on the wind, swirl, twinkle; additive; dim with _LB_DecoGlow
//       1 petals: fall on a loop and travel downwind
//       2 lip spray: puffs rolling out and down from each waterfall lip, bending on the wind
//       3 waterfall glints: ride down each sheet at exactly _LB_FlowSpeed along the sheet's arc, warm-white to turquoise
//       4 rising mist motes: lift slowly from each impact plume, drift downwind, fade as they climb (the falls' breath)
Shader "LastBell/Area3/ParticleField"
{
    Properties
    {
        _Mode ("Mode (0 motes, 1 petals, 2 spray, 3 glints, 4 rising mist)", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 1
        _Softness ("Softness", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "DisableBatching" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
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
                float _Mode, _SrcBlend, _DstBlend, _Softness;
            CBUFFER_END
            float _LB_DecoGlow;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float2 uv3 : TEXCOORD2; };
            struct Vt { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; float add : TEXCOORD2; };
            Vt vert(A v)
            {
                Vt o; float t = _LB_Time, s = v.uv2.x, size = v.uv2.y, a = v.color.a;
                float3 p = TransformObjectToWorld(v.positionOS.xyz), dir = v.normalOS, col = v.color.rgb;
                float3 wind = float3(_LB_Wind.x, 0, _LB_Wind.y) * _LB_Wind.z;
                float add = 0;
                if (_Mode < 0.5)
                {
                    float g = lbcGust(p);
                    p += float3(sin(t * 0.4 + s * 6.28) * 0.8, sin(t * 0.7 + s * 12.0) * 0.5, cos(t * 0.35 + s * 6.28) * 0.8);
                    p += wind * (0.6 + g) * 0.8 * sin(t * 0.05 + s * 3);                                   // carried back and forth by gusts
                    a *= (0.55 + 0.45 * sin(t * 1.3 + s * 40.0)) * (0.35 + 0.65 * _LB_DecoGlow); add = 1;
                }
                else if (_Mode < 1.5)
                {
                    float life = frac(t / 14.0 + s);
                    p += float3(sin(t * 0.5 + s * 9.0) * 1.2, -life * 16.0, cos(t * 0.4 + s * 7.0) * 1.2) + wind * life * 7;
                    a *= smoothstep(0, 0.1, life) * smoothstep(1, 0.85, life);
                }
                else if (_Mode < 2.5)
                {
                    float life = frac(t / 1.9 + s);
                    p += dir * (life * 1.8) + float3(0, -life * life * 2.2 + life * 0.5, 0) + wind * life * 0.6;
                    size *= 0.6 + life * 1.4;
                    a *= smoothstep(0, 0.15, life) * (1 - life);
                }
                else if (_Mode < 3.5)
                {
                    float H = v.uv3.x, arc = v.uv3.y, sp = _LB_FlowSpeed;
                    float life = frac(t * sp / H * 0.85 + s);                                                // fraction of the sheet travelled
                    p += float3(0, -life * H, 0) + dir * pow(life, 0.6) * arc + wind * 0.12 * life * life * H * 0.03;
                    col = lerp(float3(2.2, 2.0, 1.6), float3(0.8, 1.9, 1.7), life);
                    a *= smoothstep(0, 0.05, life) * smoothstep(0.75, 0.45, life) * (0.6 + 0.4 * sin(t * 9 + s * 50)); add = 1;
                }
                else
                {
                    float life = frac(t / 11.0 + s);
                    p += float3(sin(t * 0.3 + s * 8) * 1.5, life * 16, cos(t * 0.27 + s * 6) * 1.5) + wind * life * 5;
                    size *= 0.7 + life * 0.8;
                    a *= smoothstep(0, 0.15, life) * smoothstep(1, 0.6, life) * (0.35 + 0.65 * _LB_DecoGlow); add = 0;
                }
                float3 cv = TransformWorldToView(p); float2 stretch = _Mode > 2.5 && _Mode < 3.5 ? float2(0.45, 2.2) : float2(1, 1); // glints streak along the fall
                cv.xy += (v.uv - 0.5) * size * stretch;
                o.positionCS = TransformWViewToHClip(cv); o.uv = v.uv; o.color = float4(col, a); o.fog = ComputeFogFactor(o.positionCS.z); o.add = add;
                return o;
            }
            half4 frag(Vt i) : SV_Target
            {
                float a = pow(saturate(1 - length(i.uv - 0.5) * 2), _Softness) * i.color.a;
                if (i.add > 0.5) return half4(i.color.rgb * a, 1);   // additive (material blend One One)
                return half4(MixFog(i.color.rgb, i.fog), a);
            }
            ENDHLSL
        }
    }
}
