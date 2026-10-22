// Soft cumulus billboards for Area 1 — The Skyward Spire (a sibling of Arenas/Shaders/CloudBillboard, which stays as is).
// Camera-facing quads that pick one cell of an 8-shape puff atlas (2 x 4). The atlas red channel is baked light
// (bright billowy tops, darker flat bottoms); the shader maps it from the lavender-blue underside to the sunlit top
// and adds a thin warm rim on the side facing the sun. Alpha fades where the puff nears opaque geometry
// (camera depth texture, _SoftDistance metres), so clouds wrap round towers instead of slicing them with a straight
// edge. The camera must request the depth texture (SkywardSpireBuilder turns it on per camera).
Shader "LastBell/SkyCloud"
{
    Properties
    {
        _MainTex ("Puff atlas (2 x 4)", 2D) = "white" {}
        _Cell ("Atlas cell (0-7)", Float) = 0
        _FlipX ("Mirror the puff", Float) = 0
        _Lit ("Sunlit colour", Color) = (1, 1, 1, 1)
        _Shade ("Underside colour", Color) = (0.725, 0.796, 0.902, 1)
        _Rim ("Sun rim colour", Color) = (1, 0.93, 0.8, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _SoftDistance ("Soft intersection (m)", Float) = 4.5
        _SunDir ("Sun direction (world)", Vector) = (0.5, 0.82, -0.28, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Lit, _Shade, _Rim, _SunDir;
                float _Cell, _FlipX, _Opacity, _SoftDistance;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; float2 sun : TEXCOORD2; float eyeDepth : TEXCOORD3; float4 screen : TEXCOORD4; float fog : TEXCOORD5; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float2 size = float2(length(GetObjectToWorldMatrix()._m00_m10_m20), length(GetObjectToWorldMatrix()._m01_m11_m21));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 world = centre + right * v.positionOS.x * size.x + up * v.positionOS.y * size.y;
                o.positionCS = TransformWorldToHClip(world);
                float cell = floor(_Cell + 0.5), col = fmod(cell, 2), row = floor(cell / 2);
                float u = _FlipX > 0.5 ? 1 - v.uv.x : v.uv.x;
                o.uv = float2((col + u) / 2, (row + v.uv.y) / 4);
                o.local = v.uv;
                float3 s = normalize(_SunDir.xyz); o.sun = normalize(float2(dot(s, right), dot(s, up)) + 1e-4);
                o.eyeDepth = -TransformWorldToView(world).z;
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 col = lerp(_Shade.rgb, _Lit.rgb, t.r);
                float edge = 1 - smoothstep(0.15, 0.85, t.a), facing = saturate(dot(normalize(i.local - 0.5 + 1e-4), i.sun));
                col += _Rim.rgb * edge * facing * 0.35;
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((scene - i.eyeDepth) / max(0.01, _SoftDistance));
                col = MixFog(col, i.fog);
                return half4(col, t.a * _Opacity * soft);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
