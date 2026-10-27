// Arcanist spell surfaces (docs/design/arcanist-fx-prompts.md). Unlit, premultiplied output. Two procedural noise
// layers at different scales and speeds drift through each other (light flowing through the shape); erosion removes
// the weakest regions first and leaves a glowing edge. Animation time is supplied in _T (the effect's own age), so a
// paused game freezes the flow and the same moment always draws the same. Property names match
// FX_SH_ColorDissolve, so PlayerFx's burst pool drives both.
//   _Mode 0 Band:    a bright front at U = _Progress with a tail behind it (expanding rings, trails, swipes)
//   _Mode 1 Dot:     a soft round sprite (motes, cores, particle quads)
//   _Mode 2 Stroke:  drawn from U = 0 up to _Progress with a bright head; thin across V (runes, rings drawn as timers)
//   _Mode 3 Beam:    a thin core (_Width) inside a wider glow across V, flowing along U (rays, ribbons, threads)
//   _Mode 4 Shell:   rim-lit (fresnel, _Fresnel power) surface for orbs and silhouettes; needs normals
//   _Mode 5 Flake:   a ragged sprite whose erosion follows the particle's life (ash); vertex alpha 1 -> 0 erodes it
//   _Mode 6 Shadow:  a soft round dark spot, hardness _Width (alpha-blend it with a dark _ColorLow)
//   _Mode 7 Dart:    a diamond along U with a hot tip at U = 1 (missiles, shards)
//   _Mode 8 Pillar:  bright at the base, fading up V, with vertical strands (light columns; cylinder mesh U around)
//   _Mode 9 Rock:    opaque charred stone (_ColorLow, lit by a fixed key light per facet) with thin molten cracks
//                    (_ColorHigh, from 3D noise in object space, width _Width) and a white-hot face toward _HeatDir
//                    (_ColorEdge x _EdgeGlow); erosion burns it away with a glowing edge. Alpha-blend it.
Shader "LastBell/FX/ArcaneFlow"
{
    Properties
    {
        [Enum(Band,0,Dot,1,Stroke,2,Beam,3,Shell,4,Flake,5,Shadow,6,Dart,7,Pillar,8,Rock,9)] _Mode ("Shape", Float) = 3
        [HDR] _ColorLow ("Body", Color) = (0.75, 0.25, 1.9, 1)
        [HDR] _ColorHigh ("Hot", Color) = (2.4, 2.2, 2.6, 1)
        [HDR] _ColorEdge ("Erosion edge", Color) = (2.0, 0.45, 1.5, 1)
        _Progress ("Front / drawn length (U)", Range(-0.5, 1.5)) = 0.5
        _Width ("Width / core / hardness", Float) = 0.15
        _Tail ("Tail length", Float) = 0.3
        _Dissolve ("Erosion", Range(0, 1)) = 0
        _Soft ("Erosion softness", Range(0.01, 1)) = 0.12
        _EdgeGlow ("Edge glow", Float) = 1
        _NoiseScale ("Noise scale (U, V)", Vector) = (6, 2, 0, 0)
        _Flow ("Flow A (U,V) / flow B (U,V)", Vector) = (-1.2, 0.2, -0.5, -0.35)
        _NoiseAmt ("Noise amount", Range(0, 1)) = 0.45
        _Fresnel ("Shell rim power", Float) = 2.5
        _T ("Effect time", Float) = 0
        _ClipY ("World height clip (off below -50)", Float) = -100
        _Intensity ("Intensity", Float) = 1
        _HeatDir ("Rock: hot face direction (world)", Vector) = (0, -1, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Off = both sides)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+60" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off Cull [_Cull]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Mode, _Progress, _Width, _Tail, _Dissolve, _Soft, _EdgeGlow, _NoiseAmt, _Fresnel, _T, _Intensity, _ClipY;
            float4 _ColorLow, _ColorHigh, _ColorEdge, _NoiseScale, _Flow, _HeatDir;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float rim : TEXCOORD1; float wy : TEXCOORD2; float3 wn : TEXCOORD3; float3 op : TEXCOORD4; };
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 vd = normalize(WorldSpaceViewDir(v.vertex));
                o.rim = 1 - abs(dot(n, vd));
                o.wy = mul(unity_ObjectToWorld, v.vertex).y; o.wn = n; o.op = v.vertex.xyz;
                return o;
            }
            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            float hash3 (float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float vnoise3 (float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                float a = lerp(lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x), lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x), lerp(hash3(i + float3(0, 1, 1)), hash3(i + 1), f.x), f.y);
                return lerp(a, b, f.z);
            }
            // distances to the nearest and second-nearest cell points (3D Voronoi): F2 - F1 is small along cell borders
            float2 voro3 (float3 p)
            {
                float3 i = floor(p), f = frac(p); float d1 = 8, d2 = 8;
                for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z), c = i + g;
                    float3 r = g + float3(hash3(c), hash3(c + 17.1), hash3(c + 31.7)) - f; float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return float2(sqrt(d1), sqrt(d2));
            }
            float4 Rock (v2f i)
            {
                float3 N = normalize(i.wn), H = normalize(_HeatDir.xyz + float3(0, 1e-4, 0));
                float3 p = i.op * _NoiseScale.x;
                float n1 = vnoise3(p + _Flow.xyz * _T), n2 = vnoise3(p * 2.7 + 11.3), n3 = vnoise3(p * 0.5 + 3.7);
                float face = dot(N, H), heat = saturate(face);
                float key = saturate(dot(N, normalize(float3(-0.35, 0.85, -0.4))));
                float3 col = _ColorLow.rgb * (0.55 + 0.45 * key) * (0.6 + 0.8 * n2);                      // charred stone: soft facet shading, mottled
                float2 vo = voro3(p + (n3 - 0.5) * 0.8);                                                 // cracks along cell borders
                float vein = 1 - smoothstep(0, _Width * (0.4 + n2), vo.y - vo.x);
                vein *= smoothstep(0.35, 0.6, n3) * (0.6 + 0.8 * n1);                                    // only some borders split; light pulses along them
                col += _ColorHigh.rgb * saturate(vein) * (0.4 + 1.6 * smoothstep(-0.3, 0.9, face));          // cracks hottest on the leading face
                col += _ColorEdge.rgb * pow(heat, 6) * _EdgeGlow;                                         // a small hot cap on the leading face
                float nn = n1 * 0.5 + n2 * 0.5, mask = 1, edge = 0;
                if (_Dissolve > 0.001)
                {
                    mask = saturate((nn - _Dissolve) / _Soft);
                    edge = mask * (1 - saturate((nn - _Dissolve - _Soft * 0.5) / _Soft));
                    col += _ColorHigh.rgb * edge * 1.5;
                }
                float a = saturate(mask * i.color.a * _Intensity);
                return float4(col * i.color.rgb * a, a);   // premultiplied
            }
            float4 frag (v2f i) : SV_Target
            {
                if (_Mode > 8.5) return Rock(i);
                float2 uv = i.uv; float s = 1; float dissolve = _Dissolve; float a0 = i.color.a;
                if (_Mode < 0.5)
                {
                    float du = uv.x - _Progress;
                    float along = du > 0 ? exp(-du * du / max(1e-4, _Width * _Width)) : exp(du / max(0.001, _Tail));
                    s = along * (1 - pow(abs(uv.y * 2 - 1), 2.5));
                }
                else if (_Mode < 1.5) { s = saturate(1 - length(uv * 2 - 1)); s *= s; }
                else if (_Mode < 2.5)
                {
                    float du = uv.x - _Progress;
                    float drawn = du <= 0 ? 1 : 0;
                    float head = exp(-du * du / max(1e-4, _Width * _Width * 0.25));
                    s = (drawn * 0.75 + head * 0.9) * (1 - pow(abs(uv.y * 2 - 1), 3));
                }
                else if (_Mode < 3.5)
                {
                    float v = abs(uv.y * 2 - 1);
                    float core = exp(-v * v / max(1e-4, _Width * _Width));
                    float glow = exp(-v * v / 0.18) * 0.45;
                    s = core + glow * (1 - core);
                    s *= smoothstep(0, 0.04, uv.x) * smoothstep(1, 0.96, uv.x);
                }
                else if (_Mode < 4.5) { s = pow(saturate(i.rim), _Fresnel) * 0.95 + 0.04; }
                else if (_Mode < 5.5)
                {
                    float2 c = uv * 2 - 1; s = saturate(1.15 - max(abs(c.x), abs(c.y)));
                    dissolve = saturate(_Dissolve + (1 - a0) * 0.85); a0 = 1;
                }
                else if (_Mode < 6.5) { float r = length(uv * 2 - 1); s = 1 - smoothstep(saturate(1 - _Width), 1, r); }
                else if (_Mode > 7.5) { s = pow(saturate(1 - uv.y), 0.9) * (0.7 + 0.3 * sin(uv.x * 6.28318 * 5)); }
                else
                {
                    float taper = uv.x < 0.7 ? uv.x / 0.7 : (1 - uv.x) / 0.3;   // widest at 70 % along, a point at the tip
                    s = saturate(taper - abs(uv.y * 2 - 1)) * 2;
                    s = saturate(s) + exp(-pow((uv.x - 0.9) * 9, 2)) * 0.8 * (1 - abs(uv.y * 2 - 1));
                }
                float2 q = uv * _NoiseScale.xy;
                float n = vnoise(q + _Flow.xy * _T) * 0.6 + vnoise(q * 2.3 + _Flow.zw * _T) * 0.4;
                float shapeN = s * (1 - _NoiseAmt + _NoiseAmt * n * 1.6);
                float mask = saturate((shapeN - dissolve) / _Soft);
                float edge = mask * (1 - saturate((shapeN - dissolve - _Soft * 0.5) / _Soft)) * step(0.001, dissolve);
                // world-height clip: nothing above the cut, a bright band just under it
                float band = 0;
                if (_ClipY > -50) { if (i.wy > _ClipY) return 0; band = exp(-pow((_ClipY - i.wy) / 0.05, 2)); shapeN = max(shapeN, band); mask = max(mask, band); }
                // brightness and coverage follow the shape: weak regions fade out instead of glowing at full body colour
                float bright = smoothstep(0.0, 0.7, shapeN);
                float3 col = lerp(_ColorLow.rgb, _ColorHigh.rgb, smoothstep(0.8, 1.35, shapeN)) * bright;   // hot only at the peaks
                col += _ColorEdge.rgb * edge * _EdgeGlow;
                float cover = max(bright, edge);
                float a = saturate(mask * a0 * _Intensity * cover);
                if (_Mode > 5.5 && _Mode < 6.5) { float sa = saturate(mask * a0 * _Intensity); return float4(_ColorLow.rgb * sa + _ColorEdge.rgb * edge * sa, sa); }
                return float4(col * i.color.rgb * mask * a0 * _Intensity, a);   // premultiplied
            }
            ENDHLSL
        }
    }
    Fallback Off
}
