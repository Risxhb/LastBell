// Dusk sky for the Metronome Court: blue-violet zenith, dusky rose horizon and a thin warm band just above it, brightest
// in the north behind the metronome. Used as RenderSettings.skybox at runtime (loaded from Resources), so no fog.
Shader "LastBell/MetronomeCourtSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.05, 0.06, 0.12, 1)
        _Mid ("Mid sky", Color) = (0.12, 0.12, 0.22, 1)
        _Horizon ("Horizon", Color) = (0.30, 0.22, 0.28, 1)
        _Band ("Warm band", Color) = (0.55, 0.35, 0.25, 1)
        _Low ("Below horizon", Color) = (0.13, 0.14, 0.22, 1)   // equals the fog colour
        _BandDir ("Brightest band direction", Vector) = (0, 0, -1, 0)
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
            float4 _Zenith, _Mid, _Horizon, _Band, _Low, _BandDir;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.dir = v.xyz; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 c = h > 0
                    ? lerp(lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.30, h)), _Zenith.rgb, smoothstep(0.30, 0.90, h))
                    : lerp(_Horizon.rgb, _Low.rgb, smoothstep(0.0, -0.12, h));
                // warm band 0-4 degrees above the horizon, stronger toward the north
                float facing = 0.45 + 0.55 * saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-5), normalize(_BandDir.xyz)));
                float band = smoothstep(-0.02, 0.02, h) * (1 - smoothstep(0.02, 0.18, h));   // a soft glow, not a stripe
                c = lerp(c, _Band.rgb, band * facing * 0.55);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
