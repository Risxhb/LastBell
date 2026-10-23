Shader "LastBell/MenuStormSky"
{
    Properties
    {
        _Zenith("Zenith",Color)=(.018,.014,.045,1)
        _Mid("Cloud light",Color)=(.15,.10,.24,1)
        _Horizon("Horizon",Color)=(.24,.17,.34,1)
        _Low("Low",Color)=(.06,.05,.12,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Zenith,_Mid,_Horizon,_Low;
            struct V {float4 pos:SV_POSITION;float3 dir:TEXCOORD0;};
            V vert(float4 p:POSITION){V o;o.pos=UnityObjectToClipPos(p);o.dir=p.xyz;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
            float fbm(float2 p){float n=0,a=.55;for(int i=0;i<5;i++){n+=noise(p)*a;p=mul(float2x2(1.6,-1.2,1.2,1.6),p)+7.7;a*=.5;}return n;}
            fixed4 frag(V i):SV_Target
            {
                float3 d=normalize(i.dir);float h=d.y;
                float3 base=lerp(_Horizon.rgb,_Zenith.rgb,smoothstep(0,.7,h));
                float2 p=d.xz/max(.13,h+.22)*2.2+float2(_Time.y*.004,0);
                float n=fbm(p+fbm(p*.7)*2);float detail=fbm(p*2.3);
                float clouds=smoothstep(.28,.78,n);
                float3 c=lerp(base*.55,lerp(_Mid.rgb,_Horizon.rgb,detail),clouds*.8);
                c=lerp(_Low.rgb,c,smoothstep(-.1,.1,h));return float4(c,1);
            }
            ENDHLSL
        }
    }
}
