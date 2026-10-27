// Area 3 sky: deep teal overhead, misty turquoise at the horizon, a warm haze where the sun sits, darker teal below.
Shader "LastBell/Area3/Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.13, 0.31, 0.34, 1)
        _Horizon ("Horizon", Color) = (0.14, 0.36, 0.37, 1)
        _Below ("Below", Color) = (0.07, 0.235, 0.25, 1)
        _SunTint ("Sun Haze", Color) = (0.95, 0.80, 0.55, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "LBCloud.hlsl"
            float4 _Top, _Horizon, _Below, _SunTint;
            struct A { float4 positionOS : POSITION; };
            struct Vt { float4 positionCS : SV_POSITION; float3 d : TEXCOORD0; };
            Vt vert(A v) { Vt o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.d = v.positionOS.xyz; return o; }
            half4 frag(Vt i) : SV_Target
            {
                float3 d = normalize(i.d); float y = d.y;
                float3 hor = _LB_HazeColor.a > 0 ? _LB_HazeColor.rgb : _Horizon.rgb; // the horizon matches the aerial haze exactly: no seam
                float3 c = lerp(hor, _Top.rgb, smoothstep(0.0, 0.6, y));
                c = lerp(c, lerp(hor, _Below.rgb, 0.6), smoothstep(0.0, -0.35, y));
                float s = saturate(dot(d, _MainLightPosition.xyz)); c += _SunTint.rgb * (pow(s, 6) * 0.5 + pow(s, 2) * 0.12); // a warmer, brighter band toward the sun
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
