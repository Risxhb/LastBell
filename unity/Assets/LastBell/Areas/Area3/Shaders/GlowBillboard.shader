// Area 3 fake light (Windblown teardown §9): a camera-facing additive glow on a quad, slow pulse, dimmed while hazards are up.
Shader "LastBell/Area3/GlowBillboard"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (2.2, 1.5, 0.8, 1)
        _Size ("Size", Float) = 1.8
        _Pulse ("Pulse", Float) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "DisableBatching" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LBCloud.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float _Size, _Pulse;
            CBUFFER_END
            float _LB_DecoGlow;
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Vt { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Vt vert(A v)
            {
                Vt o; float3 c = TransformObjectToWorld(float3(0, 0, 0)), cv = TransformWorldToView(c);
                float ph = dot(c, float3(1.7, 0.3, 2.9)), s = _Size * (1 + sin(_LB_Time * 2.1 + ph) * _Pulse) * (0.6 + 0.4 * _LB_DecoGlow);
                cv.xy += (v.uv - 0.5) * s; o.positionCS = TransformWViewToHClip(cv); o.uv = v.uv; return o;
            }
            half4 frag(Vt i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2; float a = pow(saturate(1 - r), 2.2);
                return half4(_Color.rgb * a * (0.5 + 0.5 * _LB_DecoGlow), 1);
            }
            ENDHLSL
        }
    }
}
