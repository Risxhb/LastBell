// Background architecture for Area 1 — The Skyward Spire. Standard URP PBR shading plus the three things that make
// towers sink naturally into the cloud abyss:
//   1. height fade: below _FadeTop the surface blends toward cloud colour (pale near the top, lavender-blue deeper),
//      reaching _FadeMax by _FadeBottom;
//   2. cut-off: between _VanishTop and _VanishY the surface ends along a world-space noise edge (no dither, which read as
//      see-through glass); the stone is fully cloud-coloured by then, so the edge vanishes into the cloud. The shadow and depth
//      passes clip the same way, so shadows and the soft clouds' depth test agree with what is drawn;
//   3. aerial perspective: with distance from the arena centre and depth below the floor, colour drifts toward pale
//      sky blue and loses contrast.
// SkywardSpireBuilder swaps the imported glTF materials for these, copying base map, tiling, colour, metal and smoothness.
Shader "LastBell/SkyArchitecture"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base colour", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _EmissionMap ("Emission map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Cutoff ("Alpha cutoff (0 = off)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        _FadeTop ("Fade starts (world y)", Float) = -10
        _FadeBottom ("Fade full (world y)", Float) = -20
        _FadeMax ("Fade strength at bottom", Range(0, 1)) = 1
        _CloudTop ("Cloud colour, upper", Color) = (0.95, 0.96, 0.985, 1)
        _CloudDeep ("Cloud colour, deep", Color) = (0.776, 0.831, 0.918, 1)
        _VanishTop ("Dissolve starts (world y)", Float) = -18
        _VanishY ("Gone by (world y)", Float) = -24
        _NoiseScale ("Dissolve noise scale", Float) = 0.35
        _AerialColor ("Aerial perspective colour", Color) = (0.81, 0.878, 0.96, 1)
        _AerialStart ("Aerial starts (m from centre)", Float) = 24
        _AerialEnd ("Aerial full (m from centre)", Float) = 220
        _AerialMax ("Aerial strength", Range(0, 1)) = 0.7
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _BaseColor, _EmissionMap_ST, _EmissionColor, _CloudTop, _CloudDeep, _AerialColor;
        float _Metallic, _Smoothness, _Cutoff, _Cull, _FadeTop, _FadeBottom, _FadeMax, _VanishTop, _VanishY, _NoiseScale;
        float _AerialStart, _AerialEnd, _AerialMax;
    CBUFFER_END
    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
    TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

    float Hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
    float Noise3(float3 x)
    {
        float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
        return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
    }
    float MistNoise(float3 p) { p *= _NoiseScale; return Noise3(p) * 0.6 + Noise3(p * 2.7 + 11.3) * 0.4; }
    static const float kBayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    float Bayer4(float2 px) { uint2 q = uint2(px) % 4; return (kBayer4[q.y * 4 + q.x] + 0.5) / 16.0; }
    // keep = 1 above _VanishTop, 0 at _VanishY; noise bends the edge into mist-like lobes
    float VanishKeep(float3 positionWS)
    {
        float span = max(0.001, _VanishTop - _VanishY);
        return saturate((positionWS.y - _VanishY) / span + (MistNoise(positionWS) - 0.5) * 0.6);
    }
    void ClipAlpha(float2 uv)
    {
        if (_Cutoff > 0) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a - _Cutoff);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float fogFactor : TEXCOORD3; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                clip(VanishKeep(i.positionWS) - 0.5);
                ClipAlpha(i.uv);
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;

                SurfaceData s = (SurfaceData)0;
                s.albedo = tex.rgb; s.metallic = _Metallic; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
                s.normalTS = float3(0, 0, 1);
                s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS;
                d.normalWS = normalize(front ? i.normalWS : -i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                half3 col = UniversalFragmentPBR(d, s).rgb;

                // 1. sink into the cloud: toward pale cloud near the top of the band, lavender-blue deeper
                float h = smoothstep(_FadeTop, _FadeBottom, i.positionWS.y);
                half3 cloud = lerp(_CloudTop.rgb, _CloudDeep.rgb, h);
                col = lerp(col, cloud, h * _FadeMax);
                // 3. aerial perspective: distance from the centre plus depth below the floor
                float a = saturate((length(i.positionWS.xz) - _AerialStart) / max(1, _AerialEnd - _AerialStart)) * _AerialMax
                        + saturate(-i.positionWS.y / 40) * 0.3;   // lower = paler and bluer
                col = lerp(col, _AerialColor.rgb, saturate(a));
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 dir = normalize(_LightPosition - o.positionWS);
            #else
                float3 dir = _LightDirection;
            #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, n, dir));
            #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag (Varyings i) : SV_Target { clip(VanishKeep(i.positionWS) - 0.45); ClipAlpha(i.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; };
            Varyings vert (Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap); return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                clip(VanishKeep(i.positionWS) - 0.5); ClipAlpha(i.uv);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
