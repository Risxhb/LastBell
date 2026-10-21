// Area 3 waterfall sheet: noise streaks scrolling down, a white foam lip at the top, soft side edges, fading into mist at
// the bottom (same maths as FALL_FS in prototype/arena-3d/areas/area-3/verdant-arena.js). UV v = 1 at the top.
Shader "LastBell/Area3/Waterfall"
{
    Properties
    {
        _Deep ("Water Colour", Color) = (0.50, 0.84, 0.82, 1)
        _Foam ("Foam Colour", Color) = (0.9, 1, 0.98, 1)
        _Rep ("Vertical Repeats", Float) = 3.7
        _Width ("Streak Density", Float) = 9
        _Speed ("Speed", Float) = 1.5
        _Opacity ("Opacity", Range(0, 1)) = 1
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
            CBUFFER_START(UnityPerMaterial)
                float4 _Deep, _Foam; float _Rep, _Width, _Speed, _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Vt { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            Vt vert(A v) { Vt o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.fog = ComputeFogFactor(o.positionCS.z); return o; }
            float h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + float2(1, 1)), f.x), f.y); }
            half4 frag(Vt i) : SV_Target
            {
                float y = (1 - i.uv.y) * _Rep; float2 p = float2(i.uv.x * _Width, y * 1.4 - _Time.y * _Speed);
                float s = n(p) * 0.6 + n(p * float2(2.3, 1.9) + 7) * 0.4, streak = smoothstep(0.42, 0.85, s);
                float edge = smoothstep(0, 0.14, i.uv.x) * smoothstep(1, 0.86, i.uv.x);
                float foam = exp(-y * 5) * (0.6 + 0.4 * n(float2(i.uv.x * _Width * 2, _Time.y * 2)));
                float3 col = lerp(_Deep.rgb, _Foam.rgb, saturate(streak * 0.85 + foam));
                float a = (0.68 + streak * 0.3 + foam * 0.5) * edge * smoothstep(_Rep, _Rep * 0.55, y) * _Opacity;
                return half4(MixFog(col, i.fog), saturate(a));
            }
            ENDHLSL
        }
    }
}
