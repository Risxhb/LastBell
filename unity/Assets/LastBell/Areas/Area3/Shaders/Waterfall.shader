// Area 3 waterfall sheet. Flow is driven by world height and the shared _LB_FlowSpeed, so every sheet falls at the same
// speed whatever its length (the old UV repeat made long and short sheets disagree), accelerating slightly lower down.
// Three streak layers at different widths and speeds, bright foam bands that travel down, a sideways wobble, a churning
// foam lip, and a thinning, broken bottom that dissolves into the shared cloud fog (LBCloud.hlsl). UV v = 1 at the lip.
// _Seed offsets the pattern so the second, offset sheet the builder adds for depth doesn't repeat the first.
Shader "LastBell/Area3/Waterfall"
{
    Properties
    {
        _Deep ("Water Colour", Color) = (0.12, 0.48, 0.50, 1)
        _Foam ("Foam Colour", Color) = (1.15, 1.25, 1.22, 1)
        _Width ("Streak Density", Float) = 9
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Seed ("Pattern Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float4 _Deep, _Foam; float _Width, _Opacity, _Seed;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Vt { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 w : TEXCOORD1; float fog : TEXCOORD2; };
            Vt vert(A v)
            {
                Vt o; float3 w = TransformObjectToWorld(v.positionOS.xyz);
                float fall = 1 - v.uv.y;   // 0 at the lip, 1 at the bottom
                w.xz += _LB_Wind.xy * _LB_Wind.z * 0.12 * fall * fall * (lbcGust(w) + 0.5); // the lower sheet bends on the wind
                o.w = w; o.positionCS = TransformWorldToHClip(w); o.uv = v.uv; o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 frag(Vt i) : SV_Target
            {
                float fall = 1 - i.uv.y, t = _LB_Time;
                // flow coordinate in metres travelled: world height scrolled at the flow speed, slightly faster lower down
                float flow = i.w.y + t * _LB_FlowSpeed * (1 + fall * 0.35) + _Seed * 13.1;
                float x = i.uv.x * _Width + sin(i.w.y * 0.35 + t * 1.3 + _Seed) * 0.35;   // sideways wobble
                float s1 = lbcFbm(float2(x * 1.6, flow * 0.11));
                float s2 = lbcFbm(float2(x * 3.1 + 7, flow * 0.2 + t * 0.4));
                float s3 = lbcFbm(float2(x * 0.7 + 3, flow * 0.03));
                float streak = smoothstep(0.42, 0.72, s1 * 0.5 + s2 * 0.3 + s3 * 0.2); // soft white streaks stretched along the fall
                // foam clumps travelling down: thin, staggered per column and broken across the width, so they read as churning water, not ripples
                float stag = lbcFbm(float2(x * 1.1, 1.7)) * 2.2;                                                   // continuous offset across the width (no column steps)
                float bp = frac(flow / 7.0 + stag); float band = smoothstep(0.55, 0.85, bp) * smoothstep(1.0, 0.9, bp) * smoothstep(0.42, 0.72, lbcFbm(float2(x * 0.9, flow * 0.06))) * 1.1; // soft-edged on both sides (no sawtooth) // foam bands every ~4 m travelling down
                float lipM = fall * 34;                                                                         // metres below the lip (sheets are ~34 m)
                float lip = smoothstep(1.6, 0.2, lipM + (lbcNoise(float2(x * 3, t * 4)) - 0.5) * 0.8) * (0.75 + 0.25 * lbcNoise(float2(x * 5, t * 6))); // churning foam lip
                float edge = smoothstep(0, 0.12, i.uv.x) * smoothstep(1, 0.88, i.uv.x);
                float breakup = smoothstep(0.55, 1.0, fall) * (1 - smoothstep(0.25, 0.75, lbcNoise(float2(x * 1.7, flow * 0.2)))); // thins into spray
                float3 col = lerp(_Deep.rgb, _Foam.rgb, saturate(streak * 0.9 + band * 0.8 + lip + breakup * 0.6));
                float a = (0.62 + streak * 0.35 + band * 0.3 + lip * 0.4) * edge * (1 - breakup * 0.85) * smoothstep(1.0, 0.7, fall) * _Opacity;
                col = lerp(lbcApply(col, i.w), col, 0.45); // falls stay readable through the cloud they pour into
                return half4(MixFog(col, i.fog), saturate(a));
            }
            ENDHLSL
        }
    }
}
