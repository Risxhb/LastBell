// The slow swirling vortex far below the platform: violet-blue spiral arms and a dim pale core (kept dim so it never
// blooms out past the bottom of the gameplay frame). Expects a disc mesh with 0..1 UVs centred on 0.5.
Shader "LastBell/Area4/ShatteredVortex"
{
    Properties
    {
        _ArmA ("Arm colour A", Color) = (0.16, 0.08, 0.34, 1)
        _ArmB ("Arm colour B", Color) = (0.22, 0.26, 0.6, 1)
        _Core ("Core", Color) = (0.6, 0.55, 1, 1)
        _CoreStrength ("Core strength", Float) = 0.25
        _Opacity ("Opacity", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _ArmA, _ArmB, _Core; float _CoreStrength, _Opacity;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv * 2 - 1; return o; }
            float h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + float2(1, 1)), f.x), f.y); }
            half4 frag(V i) : SV_Target
            {
                float r = length(i.uv), a = atan2(i.uv.y, i.uv.x), t = _Time.y;
                float sw = a * 2 + log(r + 0.02) * 5 - t * 0.06;
                float arms = n(float2(sw * 1.5, r * 2)) * 0.7 + n(float2(sw * 4, r * 5)) * 0.3; arms = 0.5 + (arms - 0.5) * 0.5;
                float core = smoothstep(0.22, 0, r);
                float3 c = lerp(_ArmA.rgb, _ArmB.rgb, arms) * arms * 1.1 + _Core.rgb * core * _CoreStrength;
                return half4(c, smoothstep(1, 0.45, r) * (_Opacity + core * 0.4));
            }
            ENDHLSL
        }
    }
}
