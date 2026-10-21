// LB CustomLit for Area 3: Windblown's lighting model rebuilt from research/windblown-artstyle.html §1 (the HLSL there),
// written as a hand-made URP shader instead of a Shader Graph so the scene can be built and rebuilt from code.
// A remapped half-Lambert band with brush noise on the terminator and real URP shadows, a chosen shadow tint instead of
// darkness, hemisphere ambient plus a fake top ambient, a soft fill from the camera side, a backlight rim, and a ramp-style
// glint for brass. Every tunable is a global (_LB_*) pushed by LookSettings.SendToGPU, like Windblown's LightingDecoSettings.
// Optional per material: vertex colour, normal map, and the floor mask (R moss, G brass, B emissive mosaic grout).
Shader "LastBell/Area3/CustomLit"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        _BaseMap ("Base Map", 2D) = "white" {}
        [Toggle(_LB_VERTEXCOLOR)] _UseVertexColor ("Use Vertex Colour", Float) = 0
        [Toggle(_LB_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        [Toggle(_LB_MASKMAP)] _UseMask ("Use Floor Mask", Float) = 0
        _MaskMap ("Mask (R moss, G brass, B glow)", 2D) = "black" {}
        [HDR] _GlowColor ("Glow Colour", Color) = (0.25, 0.95, 0.85, 1)
        _GlowStrength ("Glow Strength", Float) = 0.6
        _Metal ("Brass Glint", Range(0, 1)) = 0
        [Toggle(_LB_STONEDETAIL)] _StoneDetail ("Cut Stone Detail", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float4 _BaseMap_ST; float _BumpScale; float4 _GlowColor; float _GlowStrength; float _Metal;
            float _UseVertexColor; float _UseNormalMap; float _UseMask; float _Cull; float _StoneDetail;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _LB_VERTEXCOLOR
            #pragma shader_feature_local _LB_NORMALMAP
            #pragma shader_feature_local _LB_MASKMAP
            #pragma shader_feature_local _LB_STONEDETAIL
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

            // LookSettings globals
            float _LB_DiffuseRemapMin, _LB_DiffuseRemapMax, _LB_BrushNoiseScale, _LB_BrushNoiseStr;
            float4 _LB_DiffuseUnlitTint, _LB_FakeTopAmbient, _LB_BackLightTint, _LB_AmbientSky, _LB_AmbientGround, _LB_FillColor, _LB_FillDirection;
            float _LB_FakeTopAmbientStart, _LB_FakeTopAmbientEnd, _LB_BacklightStr, _LB_BacklightTightness, _LB_DecoGlow;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3; float4 color : TEXCOORD4; float fogCoord : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.color = v.color; o.fogCoord = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float lbHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float lbNoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lbHash(i), lbHash(i + float2(1, 0)), f.x), lerp(lbHash(i + float2(0, 1)), lbHash(i + float2(1, 1)), f.x), f.y); }
            // brush-stroke noise in world space (Windblown samples a brush texture here; this is the procedural stand-in)
            float lbBrush(float3 p) { float s = _LB_BrushNoiseScale; return (lbNoise(p.xz * 3.1 * s + p.y * 2.3 * s) * 0.6 + lbNoise(p.xz * 9.7 * s - p.y * 5.1 * s) * 0.4) * 2 - 1; }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                #if defined(_LB_VERTEXCOLOR)
                    albedo *= i.color.rgb;
                #endif
                float3 N = normalize(i.normalWS);
                #if defined(_LB_STONEDETAIL)
                    // cut-stone courses projected along the dominant axis: 0.85 x 0.42 m blocks in running bond, per-block tint,
                    // darker mortar with moss creeping into it, so walls and decks stop reading as flat blockout slabs
                    float3 an = abs(N); float2 sp = an.y > 0.6 ? i.positionWS.xz : (an.x > an.z ? i.positionWS.zy : i.positionWS.xy);
                    float row = floor(sp.y / 0.42), bx = sp.x / 0.85 + row * 0.5; float2 cell = float2(floor(bx), row);
                    float2 f = float2(frac(bx) * 0.85, frac(sp.y / 0.42) * 0.42);
                    float edge = min(min(f.x, 0.85 - f.x), min(f.y, 0.42 - f.y)), mortar = 1 - smoothstep(0.012, 0.035, edge);
                    float tint = lbHash(cell * 1.31 + 0.7), mossy = smoothstep(0.55, 0.85, lbNoise(i.positionWS.xz * 0.35 + i.positionWS.y * 0.2));
                    albedo *= (0.88 + tint * 0.22) * (1 - mortar * 0.4);
                    albedo = lerp(albedo, float3(0.16, 0.24, 0.11), mortar * mossy * 0.8);
                #endif
                #if defined(_LB_NORMALMAP)
                    float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    float3 T = normalize(i.tangentWS.xyz), B = cross(N, T) * i.tangentWS.w;
                    N = normalize(mul(nTS, float3x3(T, B, N)));
                #endif
                float4 mask = 0;
                #if defined(_LB_MASKMAP)
                    mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);
                #endif
                #if defined(_LB_MASKMAP)
                    // while hazards are up the floor brass dims ~40% and the mosaic (mask alpha < 0.8) loses a little saturation;
                    // it stays turquoise so the quiet state itself never reads as a hazard (the grout glow halves below)
                    float q = saturate((_LB_DecoGlow - 0.35) / 0.65), mos = step(mask.a, 0.8);
                    albedo *= lerp(1, 0.6, mask.g * (1 - q));
                    float lum = dot(albedo, float3(0.3, 0.59, 0.11)); albedo = lerp(albedo, lum.xxx, mos * (1 - q) * 0.2);
                #endif
                float metal = saturate(_Metal + mask.g * 0.22);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                // 1-2. painterly terminator: brush noise nudges the half-Lambert term, then a remapped band times real shadows
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light ml = GetMainLight(shadowCoord);
                float hl = dot(N, ml.direction) * 0.5 + 0.5 + lbBrush(i.positionWS) * _LB_BrushNoiseStr;
                float band = smoothstep(_LB_DiffuseRemapMin, _LB_DiffuseRemapMax, hl) * lerp(0.15, 1, ml.shadowAttenuation);
                // 3. coloured shadows instead of black
                float3 col = lerp(albedo * _LB_DiffuseUnlitTint.rgb, albedo * ml.color, band);
                // hemisphere ambient, 4. fake sky ambient from above, soft fill from the camera side
                col += albedo * lerp(_LB_AmbientGround.rgb, _LB_AmbientSky.rgb, N.y * 0.5 + 0.5);
                col += albedo * _LB_FakeTopAmbient.rgb * smoothstep(_LB_FakeTopAmbientStart, _LB_FakeTopAmbientEnd, N.y);
                col += albedo * _LB_FillColor.rgb * saturate(dot(N, normalize(_LB_FillDirection.xyz)) * 0.5 + 0.2);
                // brass: a stylised glint ramp instead of reflections
                if (metal > 0)
                {
                    float3 H = normalize(ml.direction + V);
                    // broken into specks by noise so a flat floor of brass glints in places instead of glaring as one sheet
                    float g = smoothstep(0.86, 0.95, dot(N, H) + lbBrush(i.positionWS * 1.7) * 0.03) * band * smoothstep(0.35, 0.75, lbNoise(i.positionWS.xz * 5.0 + i.positionWS.y * 3.0));
                    col += ml.color * g * metal * (albedo * 1.6 + 0.25);
                }
                // point lights (lanterns that matter); Forward+ cluster loop or the classic loop
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                        float lb = smoothstep(_LB_DiffuseRemapMin, _LB_DiffuseRemapMax, dot(N, l.direction) * 0.5 + 0.5);
                        col += albedo * l.color * l.distanceAttenuation * lb;
                    LIGHT_LOOP_END
                #endif
                // 5. backlight rim on the side away from the sun
                float rim = pow(1 - saturate(dot(N, V)), _LB_BacklightTightness);
                float back = saturate(dot(-ml.direction, V) * 0.5 + 0.5);
                col += _LB_BackLightTint.rgb * rim * (0.35 + back * 0.65) * _LB_BacklightStr * (0.55 + 0.45 * albedo);
                // emissive mosaic grout, dimmed while hazards are up
                col += _GlowColor.rgb * mask.b * _GlowStrength * lerp(0.5, 1, saturate((_LB_DecoGlow - 0.35) / 0.65)); // halves while hazards are up; fully off would turn the mosaic charcoal
                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vt { float4 positionCS : SV_POSITION; };
            Vt vert(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v); Vt o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz), n = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - p);
                #else
                    float3 L = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, n, L));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs; return o;
            }
            half4 frag(Vt i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vt { float4 positionCS : SV_POSITION; };
            Vt vert(A v) { UNITY_SETUP_INSTANCE_ID(v); Vt o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Vt i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vt { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Vt vert(A v) { UNITY_SETUP_INSTANCE_ID(v); Vt o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Vt i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
