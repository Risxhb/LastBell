// Area 3 water far below: glowing turquoise with slow caustic-like brightness and drifting foam, fogged.
Shader "LastBell/Area3/Pool"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.07, 0.30, 0.31, 1)
        _Glow ("Glow", Color) = (0.32, 0.88, 0.82, 1)
        _Foam ("Foam", Color) = (0.8, 0.95, 0.92, 1)
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
            CBUFFER_START(UnityPerMaterial)
                float4 _Deep, _Glow, _Foam;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct Vt { float4 positionCS : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; };
            Vt vert(A v) { Vt o; o.w = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.w); o.fog = ComputeFogFactor(o.positionCS.z); return o; }
            float h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + float2(1, 1)), f.x), f.y); }
            half4 frag(Vt i) : SV_Target
            {
                float2 p = i.w.xz * 0.08; float t = _Time.y;
                float c = n(p + t * 0.05) * 0.5 + n(p * 2.3 - t * 0.07) * 0.5;
                float foam = smoothstep(0.7, 0.9, n(p * 0.6 + float2(t * 0.02, 0)) * 0.6 + n(p * 3.1) * 0.4);
                float3 col = lerp(_Deep.rgb, _Glow.rgb, smoothstep(0.3, 0.8, c) * 0.7 + exp(-length(i.w.xz) * 0.015) * 0.5) + _Foam.rgb * foam * 0.35;
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
