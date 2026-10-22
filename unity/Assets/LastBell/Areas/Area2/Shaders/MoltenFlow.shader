// Flowing molten brass for Area 2, The Brass Foundry: the channel ring, the falls down the drum, and the rivers and
// pools in the city below. Unlit HDR, fog-aware, SRP Batcher compatible.
//  - The mesh UV is in metres. With _FlowAxis = 0, U runs along the flow and V across it; with 1 they swap. _Across
//    gives the across range (bank to bank) so the speed can fall off parabolically toward the banks.
//  - Two-phase flow: two noise samples half a period apart are cross-faded, so the stream never visibly resets.
//  - Noise R = crust plates (cell interiors, 0 at the cracks), G = heat. Crust coverage shifts slowly over time so
//    plates break apart and re-form; cracks glow orange to hot yellow-white; cell-noise bubbles swell and pop.
//  - Time comes from the global _LB_Area2Time (BrassFoundryArena sets it in Play mode and in the editor), so the
//    batch capture can set it directly for deterministic frames. _Quiet dims emission to 60% while telegraphs are up.
Shader "LastBell/Area2/MoltenFlow"
{
    Properties
    {
        _NoiseTex ("Noise (R plates, G heat)", 2D) = "gray" {}
        _CrustColor ("Crust", Color) = (0.165, 0.047, 0.016, 1)
        _CrustColor2 ("Crust (warm)", Color) = (0.29, 0.078, 0.024, 1)
        _GlowColor ("Glow", Color) = (1, 0.416, 0.102, 1)
        _HotColor ("Hot core", Color) = (1, 0.824, 0.478, 1)
        _Emission ("Base emission", Float) = 2.8
        _CorePeak ("Core peak", Float) = 5
        _Speed ("Flow speed (m/s, signed)", Float) = 0.3
        _FlowAxis ("Flow along V (0 = U, 1 = V)", Float) = 0
        _Across ("Across range (x = min, y = max, m)", Vector) = (0, 2, 0, 0)
        _Scale ("Noise tile (m)", Float) = 3.5
        _Crust ("Crust coverage", Range(0, 1)) = 0.5
        _Bank ("Bank width (m)", Float) = 0.3
        _Undulate ("Vertex undulation (m)", Float) = 0.03
        _Bubbles ("Bubbles", Range(0, 1)) = 1
        _Streaks ("Fall streaks", Range(0, 1)) = 0
        _Lip ("Fall lip (along, m)", Float) = 1000
        _Quiet ("Quiet", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "MoltenFlow"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _CrustColor, _CrustColor2, _GlowColor, _HotColor;
                float4 _Across;
                float _Emission, _CorePeak, _Speed, _FlowAxis, _Scale, _Crust, _Bank, _Undulate, _Bubbles, _Streaks, _Lip, _Quiet;
            CBUFFER_END
            float _LB_Area2Time;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 flow : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            // x = along the flow, y = across it
            float2 FlowCoords(float2 uv) { return _FlowAxis > 0.5 ? uv.yx : uv; }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 f = FlowCoords(v.uv); float t = _LB_Area2Time;
                float3 p = v.positionOS.xyz;
                p.y += _Undulate * (sin(f.x * 1.7 - t * 1.3 + f.y * 2.3) * 0.6 + sin(f.x * 0.63 + t * 0.9) * 0.4);
                o.positionCS = TransformObjectToHClip(p);
                o.flow = f; o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _LB_Area2Time;
                float along = i.flow.x, across = i.flow.y;
                float w = max(1e-3, _Across.y - _Across.x);
                float a01 = saturate((across - _Across.x) / w);
                float prof = saturate(1 - (2 * a01 - 1) * (2 * a01 - 1));          // 1 mid-stream, 0 at the banks
                float speed = _Speed * (0.2 + 0.8 * prof);

                // two-phase flow, period T: sample 0 resets at p0 = 0, sample 1 at p0 = 0.5; each is hidden when it resets
                const float T = 3.0;
                float p0 = frac(t / T), p1 = frac(t / T + 0.5), blend = abs(p0 * 2 - 1);
                // noise space is stretched 2:1 along the flow and domain-warped by a slow, drifting low-frequency field,
                // so plates read as rafts carried downstream rather than a regular lattice
                float2 base = float2(along / (_Scale * 2), across / _Scale);
                half2 warp = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, base * 0.31 + float2(-t * 0.01, 0.73)).gr - 0.5;
                base += warp * 0.45;
                float2 uv0 = base - float2(speed * T * p0 / (_Scale * 2), 0);
                float2 uv1 = base - float2(speed * T * p1 / (_Scale * 2), 0) + float2(0.37, 0.21);
                half4 n = lerp(SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv0), SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv1), blend);
                // a second, smaller crust scale: fragments drifting between the big plates
                half4 f = lerp(SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv0 * 2.7 + 0.19), SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv1 * 2.7 + 0.61), blend);
                half slow = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, base * 0.23 + float2(t * 0.004, 0.5)).g;

                // crust plates: a threshold on the cell interiors that drifts over time, so plates crack apart and re-form;
                // a low-frequency mask clumps the crust instead of spreading it evenly
                float clump = smoothstep(0.3, 0.7, slow);
                float c0 = lerp(0.62, 0.08, _Crust) + 0.07 * sin(t * 0.35 + slow * 6.2832) + (0.5 - clump) * 0.22;
                float db = min(across - _Across.x, _Across.y - across);
                float bankK = smoothstep(0, max(1e-3, _Bank), db);
                float crust = smoothstep(c0, c0 + 0.08, n.r);
                crust = max(crust, smoothstep(c0 + 0.12, c0 + 0.18, f.r) * 0.5 * clump);
                crust *= 1 - pow(prof, 8) * 0.6;                                        // a hot, mostly open mid-line
                crust = saturate(crust + (1 - bankK) * 0.55);                          // cooler, crusted banks
                float core = (1 - crust) * saturate(smoothstep(0.45, 0.95, n.g) * (1 - smoothstep(0, max(1e-3, c0), n.r) * 0.5) + pow(prof, 6) * 0.45);
                float rim = smoothstep(0.1, 0.0, abs(n.r - c0)) * crust;               // plate edges still glow faintly

                half3 crustCol = lerp(_CrustColor.rgb, _CrustColor2.rgb, n.g) + _GlowColor.rgb * rim * 0.55;
                half3 lava = lerp(_GlowColor.rgb * _Emission, _HotColor.rgb * _CorePeak, core);
                half3 col = lerp(lava, crustCol, crust);
                col *= lerp(0.35, 1, bankK);

                // slow pulse (±8% over ~4 s) and hot bubbles that swell and pop over ~1.5 s
                col *= 1 + 0.08 * sin(t * 1.5708 + along * 0.05);
                float2 bc = float2(along - _Speed * 0.8 * t, across) / 1.1;
                float2 cell = floor(bc), fr = frac(bc) - 0.5;
                float h = Hash21(cell);
                float life = frac(t / 1.5 + h * 7.3);
                float2 jit = float2(Hash21(cell + 7.1), Hash21(cell + 3.7)) - 0.5;
                float rad = 0.2 * sin(life * 3.14159);
                float bub = step(h, 0.3 * _Bubbles) * smoothstep(rad, rad * 0.35, length(fr - jit * 0.5)) * step(life, 0.95) * bankK;
                col += _HotColor.rgb * _CorePeak * bub * 0.7 * (1 - crust * 0.6);

                // falls: vertical streaks, brighter at the lip
                half s = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(across * 0.9, along * 0.04 - t * 0.35)).g;
                col *= lerp(1, 0.5 + 1.0 * s, _Streaks);
                col *= 1 + 0.8 * _Streaks * smoothstep(_Lip - 3, _Lip, along);

                col *= lerp(1, 0.6, _Quiet);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
