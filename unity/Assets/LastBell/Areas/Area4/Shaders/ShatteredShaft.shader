// Faint additive light: soft-edged vertical shafts in the void and the cone under the bell's mouth.
// uv.x runs across the quad (soft both edges), uv.y along it; _Fade sets which end fades (0 = both ends, 1 = top only).
Shader "LastBell/Area4/ShatteredShaft"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.72, 0.38, 1)
        _Alpha ("Alpha", Float) = 0.12
        _Fade ("Fade mode", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color; float _Alpha, _Fade;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float x = 1 - abs(i.uv.x * 2 - 1);
                float along = _Fade > 0.5 ? i.uv.y * i.uv.y : smoothstep(0, 0.4, i.uv.y) * smoothstep(1, 0.7, i.uv.y);
                return half4(_Color.rgb * _Alpha * x * x * along, 1);
            }
            ENDHLSL
        }
    }
}
