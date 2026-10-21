// Area 3 sea of mist (Windblown teardown §6): two scrolling fbm layers in world XZ, lit teal, fading with distance.
Shader "LastBell/Area3/Mist"
{
    Properties
    {
        _Thin ("Thin Colour", Color) = (0.10, 0.32, 0.33, 1)
        _Thick ("Thick Colour", Color) = (0.62, 0.88, 0.85, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Thin, _Thick; float _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct Vt { float4 positionCS : SV_POSITION; float3 w : TEXCOORD0; };
            Vt vert(A v) { Vt o; o.w = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.w); return o; }
            float h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + float2(1, 1)), f.x), f.y); }
            float fbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += n(p) * a; p *= 2.03; a *= 0.5; } return s; }
            half4 frag(Vt i) : SV_Target
            {
                float2 p = i.w.xz * 0.03; float d = length(i.w.xz), t = _Time.y;
                float c = fbm(p + float2(t * 0.012, t * 0.006)) * 0.6 + fbm(p * 2.1 - float2(t * 0.02, 0)) * 0.4, dens = smoothstep(0.38, 0.78, c);
                return half4(lerp(_Thin.rgb, _Thick.rgb, dens) * lerp(0.55, 1, smoothstep(15, 60, d)), dens * _Opacity * smoothstep(260, 120, d)); // darker in the abyss under the platform
            }
            ENDHLSL
        }
    }
}
