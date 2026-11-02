// The second half of a Last Light hole (LastLightFloorFx): where LastBell/SkyHole drew the open sky (stencil bit 32), the
// depth goes to the far plane, so the stone drum just under the floor no longer hides what falls into the hole. Anything
// truly in front of the opening (a player, the boss, the rim) failed SkyHole's depth test, so it keeps its pixels and depth.
Shader "LastBell/SkyHoleDepth"
{
    SubShader
    {
        Tags { "Queue" = "Geometry+471" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite On ZTest Always Cull Off ColorMask 0
        Stencil { Ref 32 ReadMask 32 Comp Equal }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float frag (float4 pos : SV_POSITION) : SV_Depth
            {
            #if UNITY_REVERSED_Z
                return 0;
            #else
                return 1;
            #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
