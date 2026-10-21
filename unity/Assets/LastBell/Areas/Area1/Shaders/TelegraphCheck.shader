// Placeholder warning shape for Area 1's readability check (not a boss mechanic): an unlit, alpha-blended decal
// lying on the floor. The texture carries the shape (deep red-orange fill, bright inner rim, dark outer rim).
Shader "LastBell/TelegraphCheck"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Boost ("HDR boost", Float) = 3.2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float _Boost;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag (v2f i) : SV_Target { float4 c = tex2D(_MainTex, i.uv); return float4(c.rgb * _Boost, c.a); }
            ENDHLSL
        }
    }
    Fallback Off
}
