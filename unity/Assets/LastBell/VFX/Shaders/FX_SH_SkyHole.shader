// The open sky seen through a hole in the Skyward Spire floor (Aurelion's Last Light, LastLightFloorFx): the arena's
// below-horizon sky (the LastBell/SkywardSky maths, deepening to the SkyBelow sea blue straight down) under three
// drifting cloud decks 22, 45 and 85 m down. Each deck is a plane the view ray meets, so the clouds sit deep below the
// floor and shift with the camera. Drawn after the arena, SkyArchitecture included (AlphaTest, Geometry+450), at Geometry+470
// with no depth write; it marks its pixels in the stencil so LastBell/SkyHoleDepth (Geometry+471) can push the depth there
// to the far plane, letting the falling slab and the cut edges (Geometry+480) draw down into the hole.
Shader "LastBell/SkyHole"
{
    Properties
    {
        _Open ("Open (fade in)", Range(0, 1)) = 1
        _Horizon ("Horizon", Color) = (0.81, 0.89, 0.98, 1)
        _Low ("Below horizon", Color) = (0.48, 0.66, 0.93, 1)
        _Deep ("Straight down", Color) = (0.30, 0.60, 0.88, 1)
        _CloudLit ("Cloud lit", Color) = (1.0, 0.99, 0.97, 1)
        _CloudShade ("Cloud shade", Color) = (0.70, 0.78, 0.92, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+470" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off ZTest LEqual Cull Off
        Stencil { Ref 32 WriteMask 32 Comp Always Pass Replace }   // marks the opening for LastBell/SkyHoleDepth
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Open;
            float4 _Horizon, _Low, _Deep, _CloudLit, _CloudShade;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; };
            v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.wpos = mul(unity_ObjectToWorld, v).xyz; return o; }

            float hash (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float noise (float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + 1), u.x), u.y);
            }
            float fbm (float2 p) { float v = 0, a = 0.5; for (int k = 0; k < 5; k++) { v += a * noise(p); p = p * 2.03 + 17.1; a *= 0.5; } return v; }

            // one cloud deck `depth` m below the floor: rgb = lit cumulus hazed with distance, a = cover
            float4 Deck (float3 ro, float3 d, float depth, float scale, float2 drift, float cover)
            {
                float t = (-depth - ro.y) / min(d.y, -0.08);
                float2 p = (ro.xz + d.xz * t) * scale + drift * _Time.y;
                float n = fbm(p);
                float dens = smoothstep(cover, cover + 0.22, n);
                float lit = saturate((n - cover) * 3 + 0.35 + (fbm(p * 1.7 + 5.3) - 0.5) * 0.6);   // domes lit from above
                float3 c = lerp(_CloudShade.rgb, _CloudLit.rgb, lit);
                c = lerp(c, _Horizon.rgb, saturate(t / 260) * 0.7);
                return float4(c, dens);
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 ro = i.wpos, d = normalize(i.wpos - _WorldSpaceCameraPos);
                float h = d.y;
                float3 c = lerp(_Horizon.rgb, _Low.rgb, smoothstep(0, -0.35, h));
                c = lerp(c, _Deep.rgb, smoothstep(-0.5, -0.95, h));
                float4 k = Deck(ro, d, 85, 0.018, float2(0.010, 0.004), 0.48); c = lerp(c, k.rgb, k.a);
                k = Deck(ro, d, 45, 0.030, float2(0.014, 0.006), 0.52); c = lerp(c, k.rgb, k.a);
                k = Deck(ro, d, 22, 0.050, float2(0.020, 0.008), 0.60); c = lerp(c, k.rgb, k.a * 0.85);
                return float4(c, _Open); // Color properties arrive already converted to linear
            }
            ENDHLSL
        }
    }
    Fallback Off
}
