// Effect surface shader (docs/design/spell-vfx-pipeline.md §6), a hand-written stand-in for SG_FX_ColorDissolve until
// Shader Graph / VFX Graph are added to the project. Unlit. A procedural grayscale shape is coloured by a gradient
// remap (_ColorLow -> _ColorHigh) and eaten away by an erosion dissolve with a hot edge (_ColorEdge).
// Meshes are unwrapped with U along the motion and V across it. Vertex colour multiplies in (particles drive fades).
//   _Mode 0 Band:   a bright front at U = _Progress with a tail behind it (shockwaves, sweeps, trails)
//   _Mode 1 Dot:    a soft round sprite (sparks, motes; particle quads)
//   _Mode 2 Pillar: bright at the base, fading up V (light columns, rising walls)
Shader "LastBell/FX/ColorDissolve"
{
    Properties
    {
        [Enum(Band,0,Dot,1,Pillar,2)] _Mode ("Shape", Float) = 0
        [HDR] _ColorLow ("Low", Color) = (0.063, 0.094, 0.145, 1)
        [HDR] _ColorHigh ("High", Color) = (2.4, 1.0, 0.75, 1)
        [HDR] _ColorEdge ("Dissolve edge", Color) = (3.0, 2.1, 1.1, 1)
        _Progress ("Front position (U)", Range(-0.5, 1.5)) = 0.5
        _Width ("Front width", Float) = 0.08
        _Tail ("Tail length", Float) = 0.25
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _Soft ("Dissolve softness", Range(0.01, 1)) = 0.15
        _NoiseScale ("Noise scale (U, V)", Vector) = (8, 3, 0, 0)
        _Scroll ("Noise scroll (U, V)", Vector) = (0, -0.6, 0, 0)
        _Intensity ("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+60" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Mode, _Progress, _Width, _Tail, _Dissolve, _Soft, _Intensity;
            float4 _ColorLow, _ColorHigh, _ColorEdge, _NoiseScale, _Scroll;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            float4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv; float s;
                if (_Mode < 0.5)
                {
                    float du = uv.x - _Progress;
                    float along = du > 0 ? exp(-du * du / (_Width * _Width)) : exp(du / max(0.001, _Tail));
                    float across = 1 - pow(abs(uv.y * 2 - 1), 2.5);
                    s = along * across;
                }
                else if (_Mode < 1.5) { s = saturate(1 - length(uv * 2 - 1)); s *= s; }
                else s = pow(saturate(1 - uv.y), 1.6) * (0.75 + 0.25 * sin(uv.x * 6.28318 * 3));
                float n = vnoise(uv * _NoiseScale.xy + _Scroll.xy * _Time.y) * 0.65 + vnoise(uv * _NoiseScale.xy * 2.3) * 0.35;
                float shapeN = s * (0.55 + 0.45 * n);
                float mask = saturate((shapeN - _Dissolve) / _Soft);
                float edge = mask * (1 - saturate((shapeN - _Dissolve - _Soft * 0.5) / _Soft));
                float3 col = lerp(_ColorLow.rgb, _ColorHigh.rgb, smoothstep(0.1, 0.85, shapeN));
                col += _ColorEdge.rgb * edge * step(0.001, _Dissolve);
                float a = mask * i.color.a * _Intensity;
                return float4(col * i.color.rgb * a, a);    // premultiplied: works with One/One and One/OneMinusSrcAlpha
            }
            ENDHLSL
        }
    }
    Fallback Off
}
