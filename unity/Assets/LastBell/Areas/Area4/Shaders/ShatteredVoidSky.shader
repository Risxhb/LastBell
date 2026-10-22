// Skybox for The Shattered Hour: a violet-indigo cosmic void with drifting fbm nebula, lavender wisps and sparse
// twinkling stars. Mirrors the dome shader in prototype/arena-3d/areas/area-4/shattered-hour.html.
Shader "LastBell/Area4/ShatteredVoidSky"
{
    Properties
    {
        _Base ("Base", Color) = (0.043, 0.031, 0.125, 1)
        _Mid ("Mid", Color) = (0.075, 0.04, 0.17, 1)
        _Nebula ("Nebula", Color) = (0.48, 0.31, 0.82, 1)
        _Wisp ("Wisp", Color) = (0.61, 0.53, 0.91, 1)
        _NebulaStrength ("Nebula strength", Float) = 0.8
        _StarDensity ("Star density", Range(0.99, 1)) = 0.9975
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Base, _Mid, _Nebula, _Wisp; float _NebulaStrength, _StarDensity;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }
            float h3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float n3(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(h3(i), h3(i + float3(1, 0, 0)), f.x), lerp(h3(i + float3(0, 1, 0)), h3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(h3(i + float3(0, 0, 1)), h3(i + float3(1, 0, 1)), f.x), lerp(h3(i + float3(0, 1, 1)), h3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            float fbm(float3 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += a * n3(p); p *= 2.03; a *= 0.5; } return s; }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir); float y = d.y, t = _Time.y;
                float3 c = lerp(_Base.rgb, _Mid.rgb, smoothstep(-0.9, 0.05, y));
                c = lerp(c, _Base.rgb * 1.3, smoothstep(0.2, 0.95, y));
                float neb = fbm(d * 3 + float3(0, t * 0.01, t * 0.004));
                float wisp = fbm(d * 7 - neb * 1.6 + float3(t * 0.006, 0, 0));
                c += _Nebula.rgb * pow(neb, 3.2) * _NebulaStrength + _Wisp.rgb * pow(wisp, 4) * 0.9;
                float north = smoothstep(0.35, 1, -d.z) * exp(-abs(d.y - 0.05) * 3.5), horizon = exp(-abs(d.y + 0.02) * 9);
                c += (_Nebula.rgb * 0.55 + _Wisp.rgb * 0.25 * neb) * north * (0.5 + neb) + _Wisp.rgb * 0.12 * horizon;
                float s = h3(floor(d * 420));
                c += float3(1, 0.92, 0.97) * step(_StarDensity, s) * (0.6 + 0.4 * sin(t * 2 + s * 90));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
