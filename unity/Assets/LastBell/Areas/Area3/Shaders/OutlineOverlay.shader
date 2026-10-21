// Area 3 outlines (Windblown teardown §3): depth-based edges drawn by a full-screen triangle in the transparent queue,
// after URP has copied the camera depth, so no renderer feature (a shared project setting) is needed. Depth jumps give
// silhouettes; the depth Laplacian gives creases. Water, mist and glows write no depth, so they get no lines and draw on
// top. Lines fade out with distance so far scenery stays soft. All values come from LookSettings globals.
Shader "LastBell/Area3/OutlineOverlay"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _LB_OutlineColor; float _LB_OutlineOpacity, _LB_OutlineThickness, _LB_OutlineFadeStart, _LB_OutlineFadeEnd;
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Vt { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; };
            // the mesh is a big quad (see VerdantChimesBuilder): its uv becomes clip space so it always covers the screen
            Vt vert(A v)
            {
                Vt o; o.positionCS = float4(v.uv * 2 - 1, UNITY_NEAR_CLIP_VALUE, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    o.positionCS.y = -o.positionCS.y;
                #endif
                o.screen = ComputeScreenPos(o.positionCS); return o;
            }
            float D(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }
            half4 frag(Vt i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w, px = _LB_OutlineThickness / _ScreenParams.xy; float c = D(uv);
                if (c > 400) return 0;
                float l = D(uv - float2(px.x, 0)), r = D(uv + float2(px.x, 0)), d = D(uv - float2(0, px.y)), u = D(uv + float2(0, px.y));
                float jump = max(max(abs(l - c), abs(r - c)), max(abs(d - c), abs(u - c)));
                float lap = abs(l + r - 2 * c) + abs(u + d - 2 * c);
                float e = max(smoothstep(0.03 * c, 0.06 * c, jump), smoothstep(0.006 * c, 0.016 * c, lap));
                e *= 1 - smoothstep(_LB_OutlineFadeStart, _LB_OutlineFadeEnd, c);
                return half4(_LB_OutlineColor.rgb, e * _LB_OutlineOpacity);
            }
            ENDHLSL
        }
    }
}
