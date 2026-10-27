// Area 3 sea of mist (Windblown teardown §6): one of several stacked layers between about -10 and -36 m. Density comes
// from the shared cloud noise (LBCloud.hlsl), so the wisps here line up with the height fog on the towers, and drifts on
// the shared wind at the layer's own speed. Depth fade (soft intersection) means pillars never show a hard cut line.
// Brighter toward the sun and around waterfall impacts, darker in the abyss under the platform.
Shader "LastBell/Area3/Mist"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 0.5
        _LayerSeed ("Layer Seed", Float) = 0
        _Speed ("Drift Speed", Float) = 1
        _SoftDistance ("Soft Intersection (m)", Float) = 3
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "LBCloud.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Opacity, _LayerSeed, _Speed, _SoftDistance;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct Vt { float4 positionCS : SV_POSITION; float3 w : TEXCOORD0; float4 screen : TEXCOORD1; };
            Vt vert(A v) { Vt o; o.w = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.w); o.screen = ComputeScreenPos(o.positionCS); return o; }
            half4 frag(Vt i) : SV_Target
            {
                float2 xz = i.w.xz + _LB_Wind.xy * _LB_Time * 0.4 * (_Speed - 1);          // each layer drifts at its own speed
                float dens = lbcDensity(xz, _LayerSeed), d = length(i.w.xz);
                float impact = lbcImpact(i.w.xz, 5);
                float gap = smoothstep(0.32, 0.5, lbcNoise(i.w.xz * 0.011 + lbcDrift(0.5) * 0.3 + 4.1));            // open patches over the water
                dens = saturate(dens * lerp(0.15, 1, gap) + impact * 0.45);                    // spray thickens the cloud where falls land
                float3 col = lerp(_LB_CloudThin.rgb, _LB_CloudThick.rgb, dens);
                float2 sunXZ = normalize(_MainLightPosition.xz + 1e-4);
                col *= lerp(0.55, 1, smoothstep(15, 60, d));                                 // darker under the platform
                col *= 1 + 0.45 * saturate(dot(normalize(i.w.xz + 1e-4), sunXZ)) * smoothstep(25, 110, d); // brighter on the sun side
                col += _LB_CloudThick.rgb * impact * 0.35;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(i.screen.xy / i.screen.w), _ZBufferParams), fragZ = i.screen.w;
                float soft = saturate((sceneZ - fragZ) / _SoftDistance);                     // soft intersection with geometry
                return half4(col, dens * _Opacity * soft * smoothstep(300, 140, d));
            }
            ENDHLSL
        }
    }
}
