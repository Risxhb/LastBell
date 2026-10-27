// Boss danger telegraph (docs/design/spell-vfx-pipeline.md §7.4). Drawn on a flat quad lying on the floor; the
// shape is a signed distance computed from the same numbers the attack resolves with, so the border sits exactly on
// the true boundary (and the boundary counts as dangerous). Object space: x = right, y = forward, metres.
// Three layers: geometry (fill + hard border), identity (rays / ripples / chevrons / clock ticks per shape) and
// timing (a fill that grows from the origin as _Progress goes 0 -> 1 at resolve). _Lock brightens the border when
// tracking stops; _Flash is the short resolve flash.
Shader "LastBell/FX/Telegraph"
{
    Properties
    {
        [Enum(Circle,0,Annulus,1,Cone,2,Lane,3)] _Shape ("Shape", Float) = 0
        _Radius ("Radius / outer radius / lane length", Float) = 2
        _Inner ("Inner radius / lane width", Float) = 1
        _HalfAngle ("Cone half-angle (deg)", Float) = 40
        _Progress ("Timing progress 0-1", Range(0, 1)) = 0
        _Lock ("Locked", Range(0, 1)) = 0
        _Flash ("Resolve flash", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 1
        [HDR] _ColorFill ("Fill (Ember Coral)", Color) = (0.92, 0.41, 0.36, 1)
        [HDR] _ColorBorder ("Border", Color) = (1.25, 0.5, 0.36, 1)
        [HDR] _ColorFlash ("Resolve (amber/white)", Color) = (3.0, 2.2, 1.3, 1)
        _ColorInk ("Deep Ink", Color) = (0.063, 0.094, 0.145, 1)
        _Border ("Border width (m)", Float) = 0.09
        _Memory ("Inactive memory outline (dashed, unfilled)", Range(0, 1)) = 0
        _ClipRadius ("Floor clip radius (0 = off)", Float) = 0
        _ClipCentre ("Floor clip centre (world xz)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off Offset -2, -2
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Shape, _Radius, _Inner, _HalfAngle, _Progress, _Lock, _Flash, _Fade, _Border, _Memory, _ClipRadius; float4 _ClipCentre;
            float4 _ColorFill, _ColorBorder, _ColorFlash, _ColorInk;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 p : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.p = v.vertex.xz;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz; return o;
            }
            // signed distance (m, negative inside) and a 0-1 coordinate the timing fill grows along
            void shape (float2 p, out float d, out float u, out float ident)
            {
                float r = length(p), ang = atan2(p.x, p.y);
                ident = 0;
                if (_Shape < 0.5)          // circle (Shattered Chime marker): clock ticks around the rim
                {
                    d = r - _Radius; u = r / _Radius;
                    float tick = abs(frac(ang / 6.28318 * 12 + 0.5) - 0.5);
                    ident = step(tick, 0.06) * step(_Radius - 0.35, r);
                }
                else if (_Shape < 1.5)     // annulus (Hollow Toll): concentric ripples
                {
                    d = max(_Inner - r, r - _Radius); u = (r - _Inner) / max(0.01, _Radius - _Inner);
                    ident = step(0.82, frac(r * 1.4 - _Time.y * 0.6));
                }
                else if (_Shape < 2.5)     // cone (Bell Sweep): harmonic rays
                {
                    float a = radians(_HalfAngle); float2 q = float2(abs(p.x), p.y);
                    d = max(r - _Radius, dot(q, float2(cos(a), -sin(a)))); u = r / _Radius;
                    float ray = abs(frac(ang / a * 3 + 0.5) - 0.5);
                    ident = step(ray, 0.04) * step(1.0, r);
                }
                else                       // lane (Pendulum Charge): chevrons pointing down the lane
                {
                    float w = _Inner * 0.5;
                    d = max(abs(p.x) - w, max(-p.y, p.y - _Radius)); u = p.y / _Radius;
                    float chev = frac((p.y - abs(p.x) * 0.8) * 0.55 - _Time.y * 1.2);
                    ident = step(0.78, chev) * step(abs(p.x), w * 0.8);
                }
            }
            float4 frag (v2f i) : SV_Target
            {
                float d, u, ident; shape(i.p, d, u, ident);
                float aa = fwidth(d) * 1.2;
                float inside = 1 - smoothstep(-aa, aa * 0.5, d);       // the boundary itself counts as inside
                if (inside <= 0) discard;
                // optional: the warning stops at the arena floor edge (the hit test cannot reach past the floor either)
                if (_ClipRadius > 0 && length(i.world.xz - _ClipCentre.xy) > _ClipRadius) discard;
                float border = inside * smoothstep(-_Border - aa, -_Border, d);   // 1 in the band just inside the edge
                float timed = step(u, _Progress);
                float hatch = step(0.5, frac((i.world.x + i.world.z) * 1.2));
                // fill: ink under coral so it reads on bright floors; the timed part is brighter
                float3 fill = lerp(_ColorInk.rgb, _ColorFill.rgb, 0.6 + 0.3 * timed + 0.08 * hatch);
                float fillA = 0.40 + 0.22 * timed + 0.05 * hatch + 0.18 * ident;
                fill += _ColorFill.rgb * ident * 0.6;
                // Unfinished Hour memory (Bellkeeper spec v0.3 §3.1): no fill, a broken border, never read as live
                fillA *= 1 - 0.9 * _Memory;
                border *= lerp(1, step(0.5, frac((i.world.x - i.world.z) * 1.6)), _Memory);
                float3 col = lerp(fill, _ColorBorder.rgb * (1 + 0.8 * _Lock), border);
                float a = lerp(fillA, 0.95, border);
                col = lerp(col, _ColorFlash.rgb, _Flash); a = lerp(a, 0.85, _Flash);
                return float4(col, a * inside * _Fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
