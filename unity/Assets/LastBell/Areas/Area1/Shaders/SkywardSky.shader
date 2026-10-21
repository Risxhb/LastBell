// Midday sky for Area 1 — The Skyward Spire: cobalt zenith, pale blue horizon, soft warm glow round the sun.
// Same maths as the sky dome in prototype/arena-3d/areas/area-1/skyward-spire-env.js.
Shader "LastBell/SkywardSky"
{
    Properties
    {
        _SunDir ("Sun direction", Vector) = (0.5, 0.82, -0.28, 0)
        _Zenith ("Zenith", Color) = (0.20, 0.42, 0.84, 1)
        _Mid ("Mid sky", Color) = (0.42, 0.63, 0.93, 1)
        _Horizon ("Horizon", Color) = (0.81, 0.89, 0.98, 1)
        _Low ("Below horizon", Color) = (0.48, 0.66, 0.93, 1)
        _Glow ("Sun glow", Color) = (1.0, 0.95, 0.85, 1)
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
                    ? lerp(lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0, 0.28, h)), _Zenith.rgb, smoothstep(0.28, 0.95, h))
                    : lerp(_Horizon.rgb, _Low.rgb, smoothstep(0, -0.35, h));
                c += _Glow.rgb * pow(s, 10) * 0.25;
                return float4(c, 1); // Color properties arrive already converted to linear
            }
            ENDHLSL
        }
    }
    Fallback Off
}
