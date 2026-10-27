// Sunset sky for the key-art arena: violet zenith, lavender band, pink-gold horizon glow toward the sun.
// Same maths as the sky dome in prototype/arena-3d/arena-env.js (there it mixes in sRGB; here in linear, close enough).
Shader "LastBell/SunsetSky"
{
    Properties
    {
        _SunDir ("Sun direction", Vector) = (-0.75, 0.16, -0.64, 0)
        _Zenith ("Zenith", Color) = (0.16, 0.12, 0.36, 1)
        _Mid ("Mid sky", Color) = (0.45, 0.33, 0.68, 1)
        _Horizon ("Horizon", Color) = (0.98, 0.62, 0.55, 1)
        _Low ("Below horizon", Color) = (0.30, 0.22, 0.50, 1)
        _Glow ("Sun glow", Color) = (1.0, 0.72, 0.42, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _SunDir, _Zenith, _Mid, _Horizon, _Low, _Glow;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.dir = v.xyz; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y, s = max(dot(d, normalize(_SunDir.xyz)), 0);
                float3 c = h > 0
                    ? lerp(lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0, 0.25, h)), _Zenith.rgb, smoothstep(0.25, 0.9, h))
                    : lerp(_Horizon.rgb, _Low.rgb, smoothstep(0, -0.35, h));
                c += _Glow.rgb * pow(s, 6) * 0.55 + float3(1, 0.85, 0.6) * pow(s, 60) * 0.6;
                return float4(c, 1); // Color properties arrive already converted to linear
            }
            ENDHLSL
        }
    }
    Fallback Off
}
