Shader "LastBell/MenuStormParticle"
{
    Properties { _MainTex("Texture",2D)="white"{} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            V vert(A a){V o;o.pos=TransformObjectToHClip(a.pos.xyz);o.uv=a.uv;o.color=a.color;return o;}
            half4 frag(V i):SV_Target{return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*i.color;}
            ENDHLSL
        }
    }
}
