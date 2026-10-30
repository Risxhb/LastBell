// Aged, rain-wet brass for the main menu's dial rim (October 26, 2026; docs/main-menu/menu-refinement-20261026.md).
// No UVs needed: the finish comes from the surface direction and world position.
//   top-facing surfaces: polished, brighter brass worn by contact (the "worn edge")
//   sides: darker tarnished body with vertical streaks
//   low, outward/downward faces: a hint of verdigris
// Lit with URP's PBR (main + additional lights, reflection probes) so lanterns and lightning glint on it.
Shader "LastBell/MenuAgedBrass"
{
    Properties
    {
        _Polish("Polished brass", Color) = (0.78, 0.58, 0.28, 1)
        _Body("Tarnished body", Color) = (0.24, 0.17, 0.08, 1)
        _Verdigris("Verdigris", Color) = (0.16, 0.24, 0.20, 1)
        _PolishSmooth("Polished smoothness", Range(0,1)) = 0.78
        _BodySmooth("Body smoothness (wet)", Range(0,1)) = 0.55
        _PolishStart("Worn-top start (normal.y)", Range(0,1)) = 0.55
        _VerdigrisAmount("Verdigris amount", Range(0,1)) = 0.35
        _StreakScale("Tarnish streak scale", Float) = 6.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Polish, _Body, _Verdigris;
                half _PolishSmooth, _BodySmooth, _PolishStart, _VerdigrisAmount, _GoldTint;
                float _StreakScale;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 wn : TEXCOORD1; float fog : TEXCOORD2; };

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float noise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1,0,0)), f.x), lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x), lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p) { float n = 0, a = .5; for (int k = 0; k < 4; k++) { n += noise(p) * a; p *= 2.07; a *= .5; } return n; }

            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wp = p.positionWS; o.wn = TransformObjectToWorldNormal(i.n); o.fog = ComputeFogFactor(p.positionCS.z); return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.wn);
                // vertical tarnish streaks (rain runs down the sides) and broad patchy wear
                float streak = fbm(float3(i.wp.x * _StreakScale, i.wp.y * _StreakScale * 0.18, i.wp.z * _StreakScale));
                float patch = fbm(i.wp * 0.9);
                float top = smoothstep(_PolishStart, 0.95, n.y);                      // worn, polished top edge
                float polish = saturate(top * (0.75 + 0.5 * patch));
                float verd = saturate((-n.y * 0.6 + 0.4) * smoothstep(0.35, 0.6, fbm(i.wp * 2.3 + 7)) * _VerdigrisAmount * 2.2) * (1 - polish);
                half3 body = _Body.rgb * (0.45 + 1.1 * streak);
                half3 albedo = lerp(lerp(body, _Polish.rgb, polish), _Verdigris.rgb, verd);
                half metallic = lerp(lerp(0.40, 0.95, polish), 0.05, verd);   // tarnish is oxide: far less metallic than the worn lip
                half smooth = lerp(lerp(_BodySmooth * (0.8 + 0.4 * streak), _PolishSmooth, polish), 0.25, verd);

                InputData d = (InputData)0;
                d.positionWS = i.wp; d.normalWS = n; d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.wp);
                d.shadowCoord = TransformWorldToShadowCoord(i.wp); d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n); d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                d.shadowMask = half4(1,1,1,1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = metallic; s.smoothness = smooth; s.occlusion = 1; s.alpha = 1; s.specular = 0;
                half4 c = UniversalFragmentPBR(d, s);
                // grazing Fresnel and the violet probe turned the highlight silver/lavender: pull reflections toward the brass hue
                return c;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
