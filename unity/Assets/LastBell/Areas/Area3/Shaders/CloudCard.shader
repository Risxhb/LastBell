// Area 3 cloud cards (after Windblown's S_Li_FogPlane, teardown §6): soft camera-facing cloud puffs hugging the bases of
// towers, piers and bastions. One mesh holds every card: four corners share a centre, uv = corner, uv2 = (seed, size).
// Cards drift on the shared wind, pulse gently, are lit teal from below and a little warm on top, and fade softly where
// they meet geometry (depth fade) so they never clip hard.
Shader "LastBell/Area3/CloudCard"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 0.75
        _SoftDistance ("Soft Intersection (m)", Float) = 4
        _WarmTop ("Warm Top", Color) = (0.95, 0.86, 0.70, 1)
        _Brightness ("Brightness", Float) = 1.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-15" "RenderPipeline" = "UniversalPipeline" "DisableBatching" = "True" }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "LBCloud.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Opacity, _SoftDistance, _Brightness; float4 _WarmTop;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Vt { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; float3 w : TEXCOORD2; float seed : TEXCOORD3; };
            Vt vert(A v)
            {
                Vt o; float s = v.uv2.x, t = _LB_Time;
                float3 c = TransformObjectToWorld(v.positionOS.xyz);
                c.xz += _LB_Wind.xy * sin(t * 0.07 + s * 6.28) * 2.5 + float2(sin(t * 0.11 + s * 9), cos(t * 0.09 + s * 7)) * 1.2; // slow drift
                c.y += sin(t * 0.13 + s * 5) * 0.6;
                float size = v.uv2.y * (1 + sin(t * 0.21 + s * 11) * 0.08);                                                  // gentle pulse
                float3 cv = TransformWorldToView(c); cv.xy += (v.uv - 0.5) * size * float2(1.6, 1);
                o.positionCS = TransformWViewToHClip(cv); o.uv = v.uv; o.screen = ComputeScreenPos(o.positionCS); o.w = c; o.seed = s;
                return o;
            }
            half4 frag(Vt i) : SV_Target
            {
                float2 q = (i.uv - 0.5) * 2; float r = length(q * float2(1, 1.25));
                float puff = lbcFbm(i.uv * 3.1 + i.seed * 17 + _LB_Time * 0.02) ;
                float a = saturate(1 - r) * smoothstep(0.25, 0.75, puff + (1 - r) * 0.35);
                float shade = saturate(0.35 + q.y * 0.45 + (puff - 0.5) * 0.9);
                float3 col = lerp(_LB_CloudThin.rgb * 0.75, _LB_CloudThick.rgb * 1.08, shade);
                col = lerp(col, col * _WarmTop.rgb * 1.15, saturate(q.y) * 0.5);                                           // warm on top
                float sceneZ = LinearEyeDepth(SampleSceneDepth(i.screen.xy / i.screen.w), _ZBufferParams);
                float soft = saturate((sceneZ - i.screen.w) / _SoftDistance);
                return half4(col * _Brightness, a * _Opacity * soft);
            }
            ENDHLSL
        }
    }
}
