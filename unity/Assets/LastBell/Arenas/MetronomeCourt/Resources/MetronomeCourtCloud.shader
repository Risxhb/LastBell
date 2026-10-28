// Metronome Court cloud sea (October 27, 2026, Risxhb: "clouds like the main menu, with structures rising out of them").
// Soft puff cards like the menu's Sea of Clouds, batched into a few meshes: every vertex carries its puff centre
// (position), its corner (uv0, -1..1) and half-size (uv1); the card turns to face the camera in the vertex shader.
// Painted light: a lit top and a shadowed violet underside per puff, dissolving into the fog colour with distance.
// Safety (presentation only): pixels fade out above y = _MaxY and near the court drum, so no cloud ever covers
// the combat floor, and puffs fade when the camera comes close.
Shader "LastBell/MetronomeCourtCloud"
{
    Properties
    {
        _MainTex ("Puff", 2D) = "white" {}
        _Lit ("Lit top", Color) = (0.52, 0.52, 0.70, 1)
        _Shade ("Shadowed underside", Color) = (0.17, 0.17, 0.29, 1)
        _Far ("Distance colour", Color) = (0.20, 0.21, 0.32, 1)
        _Deep ("Lower layer colour", Color) = (0.30, 0.38, 0.52, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _MaxY ("Highest visible y", Float) = -1.2
        _DrumRadius ("Keep-clear radius near the court", Float) = 14.5
        _FarStart ("Distance tint start", Float) = 45
        _FarEnd ("Distance tint end", Float) = 170
        _SoftDepth ("Soft edges against geometry (needs the depth texture)", Float) = 0
        _SoftRange ("Soft edge range (m)", Float) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Lit, _Shade, _Far, _Deep; float _Opacity, _MaxY, _DrumRadius, _FarStart, _FarEnd, _SoftDepth, _SoftRange;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 size : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; float4 color : COLOR; float camDist : TEXCOORD2; float4 screen : TEXCOORD3; float eye : TEXCOORD4; };
            v2f vert (appdata v)
            {
                v2f o;
                float3 centre = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 world = centre + right * v.uv.x * v.size.x + up * v.uv.y * v.size.y;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.uv = v.uv * 0.5 + 0.5; o.world = world; o.color = v.color;
                o.camDist = distance(_WorldSpaceCameraPos, centre);
                o.screen = ComputeScreenPos(o.pos); o.eye = -mul(UNITY_MATRIX_V, float4(world, 1)).z;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                // pass 4 (critic: "one flat noise layer from the gameplay camera"): each puff is lit as a soft dome, so from
                // the steep combat camera its centre and sunward edge catch the key light and its rim falls into violet shadow
                // (from a side camera this is still a lit top and a shadowed underside)
                float2 q = i.uv * 2 - 1;
                float3 n = normalize(UNITY_MATRIX_V[0].xyz * q.x * 0.85 + UNITY_MATRIX_V[1].xyz * q.y * 0.85 + UNITY_MATRIX_V[2].xyz * sqrt(saturate(1 - dot(q, q) * 0.72)));
                float shape = saturate(dot(n, normalize(float3(-0.3, 0.85, 0.42))) * 0.65 + 0.4);
                float lit = saturate(shape * (0.62 + 0.55 * t.r)) * i.color.r;
                float3 c = lerp(_Shade.rgb, _Lit.rgb, lit);
                // the lower sea turns a cooler, darker blue, so the gaps between banks read as depth
                c = lerp(c, _Deep.rgb * (0.55 + 0.6 * lit), saturate((-13.5 - i.world.y) / 9.0) * 0.75);
                c = lerp(c, _Far.rgb, saturate((i.camDist - _FarStart) / (_FarEnd - _FarStart)) * 0.75);
                float a = t.a * _Opacity * i.color.a;
                a *= 1 - smoothstep(_MaxY - 2.0, _MaxY, i.world.y);                                   // never above the play plane
                float rr = length(i.world.xz);
                a *= saturate(1 - smoothstep(-7.0, -1.5, i.world.y) * (1 - smoothstep(_DrumRadius, _DrumRadius + 3.0, rr)));   // clear of the drum top
                a *= smoothstep(6.0, 16.0, i.camDist);                                                 // no puff smothers a close camera
                if (_SoftDepth > 0.5)
                {   // soft edge where a card meets masonry, instead of a straight cut line
                    float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                    a *= saturate((scene - i.eye) / _SoftRange);
                }
                return float4(c, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
