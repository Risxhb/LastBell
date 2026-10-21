// Area 3 particles without the Particle System module (it is disabled in this project): each mesh holds many quads whose
// four corners share a centre; the vertex shader animates and billboards them. uv = corner, uv2 = (seed, size),
// normal = drift direction, colour = tint. _Mode 0 motes (drifting, twinkling, additive), 1 petals (falling loop),
// 2 waterfall spray (puffs rolling out and down from the lip, growing and fading). Motes dim with _LB_DecoGlow.
Shader "LastBell/Area3/ParticleField"
{
    Properties
    {
        _Mode ("Mode (0 motes, 1 petals, 2 spray)", Float) = 0
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
            CBUFFER_START(UnityPerMaterial)
                float _Mode, _SrcBlend, _DstBlend, _Softness;
            CBUFFER_END
            float _LB_DecoGlow;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Vt { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            Vt vert(A v)
            {
                Vt o; float t = _Time.y, s = v.uv2.x, size = v.uv2.y, a = v.color.a;
                float3 p = TransformObjectToWorld(v.positionOS.xyz), dir = v.normalOS;
                if (_Mode < 0.5)
                {
                    p += float3(sin(t * 0.4 + s * 6.28) * 0.8, sin(t * 0.7 + s * 12.0) * 0.5, cos(t * 0.35 + s * 6.28) * 0.8);
                    a *= (0.55 + 0.45 * sin(t * 1.3 + s * 40.0)) * (0.35 + 0.65 * _LB_DecoGlow);
                }
                else if (_Mode < 1.5)
                {
                    float life = frac(t / 14.0 + s);
                    p += float3(sin(t * 0.5 + s * 9.0) * 1.5 + life * 3.0, -life * 16.0, cos(t * 0.4 + s * 7.0) * 1.5);
                    a *= smoothstep(0, 0.1, life) * smoothstep(1, 0.85, life);
                }
                else
                {
                    float life = frac(t / 1.9 + s);
                    p += dir * (life * 1.8) + float3(0, -life * life * 2.2 + life * 0.5, 0);
                    size *= 0.6 + life * 1.4;
                    a *= smoothstep(0, 0.15, life) * (1 - life);
                }
                float3 cv = TransformWorldToView(p); cv.xy += (v.uv - 0.5) * size;
                o.positionCS = TransformWViewToHClip(cv); o.uv = v.uv; o.color = float4(v.color.rgb, a); o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Vt i) : SV_Target
            {
                float a = pow(saturate(1 - length(i.uv - 0.5) * 2), _Softness) * i.color.a;
                if (_Mode < 0.5) return half4(i.color.rgb * a, 1);   // additive
                return half4(MixFog(i.color.rgb, i.fog), a);
            }
            ENDHLSL
        }
    }
}
