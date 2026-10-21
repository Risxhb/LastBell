// Soft cloud puff for the sea of clouds around the key-art arena. Unlit, alpha-blended, always faces the camera
// (the quad's size comes from the object's scale).
Shader "LastBell/CloudBillboard"
{
    Properties
    {
        _MainTex ("Puff", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.48, 0.36, 0.66, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Tint; float _Opacity;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                float3 centre = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float2 size = float2(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 world = centre + right * v.vertex.x * size.x + up * v.vertex.y * size.y;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.uv = v.uv;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                return float4(_Tint.rgb * t.rgb, t.a * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
