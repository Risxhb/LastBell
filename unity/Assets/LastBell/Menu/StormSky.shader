Shader "LastBell/MenuStormSky"
{
    Properties
    {
        _Zenith("Zenith",Color)=(.018,.014,.045,1)
        _Mid("Cloud light",Color)=(.15,.10,.24,1)
        _Horizon("Horizon",Color)=(.24,.17,.34,1)
        _Low("Low",Color)=(.06,.05,.12,1)
        // defined storm clouds (October 26, 2026; docs/main-menu/menu-refinement-20261026.md). 0 = the original soft sky.
        _Defined("Defined clouds (0/1)",Float)=0
        _CloudLit("Cloud edge light",Color)=(.42,.30,.58,1)
        _CloudDark("Cloud underside",Color)=(.07,.05,.13,1)
        _Coverage("Cloud coverage",Range(0,1))=.52
        _Sharp("Edge sharpness",Range(.02,.5))=.12
        _ZenithCalm("Zenith calm (keeps the top dark)",Range(0,1))=.65
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
            float4 _Zenith,_Mid,_Horizon,_Low,_CloudLit,_CloudDark;float _Defined,_Coverage,_Sharp,_ZenithCalm;
            float _MenuStormFlash; // global, driven by ConfrontationWeather; 0 elsewhere
            struct V {float4 pos:SV_POSITION;float3 dir:TEXCOORD0;};
            V vert(float4 p:POSITION){V o;o.pos=UnityObjectToClipPos(p);o.dir=p.xyz;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
            float fbm(float2 p){float n=0,a=.55;for(int i=0;i<5;i++){n+=noise(p)*a;p=mul(float2x2(1.6,-1.2,1.2,1.6),p)+7.7;a*=.5;}return n;}
            fixed4 frag(V i):SV_Target
            {
                float3 d=normalize(i.dir);float h=d.y;
                float3 base=lerp(_Horizon.rgb,_Zenith.rgb,smoothstep(0,.7,h));
                float3 c;
                if(_Defined<.5)
                {
                    // the original soft sky (kept identical for _Defined = 0)
                    float2 p=d.xz/max(.13,h+.22)*2.2+float2(_Time.y*.004,0);
                    float n=fbm(p+fbm(p*.7)*2);float detail=fbm(p*2.3);
                    float clouds=smoothstep(.28,.78,n);
                    c=lerp(base*.55,lerp(_Mid.rgb,_Horizon.rgb,detail),clouds*.8);
                    // Distant lightning lights cloud undersides only; brightness is capped by the driver.
                    c+=(_Mid.rgb*2.2+float3(.05,.04,.09))*clouds*saturate(1-h*1.4)*_MenuStormFlash;
                }
                else
                {
                    // Defined storm banks (refined after the critic, October 26, 2026). Two layers with parallax, wider than
                    // tall; sharp coverage edges; bodies darken with depth; rims lit by the horizon glow from below/outward
                    // (a directional density test); the horizon itself fades into glow instead of sub-pixel noise.
                    float2 q=d.xz*float2(1,1.8)/max(.10,h+.12);
                    float2 out2=normalize(q+1e-4);
                    float2 pHi=q*.55+float2(_Time.y*.0025,_Time.y*.0008), pLo=q*1.1+float2(_Time.y*.0065,-_Time.y*.0015);
                    float wHi=fbm(pHi+fbm(pHi*.5+3.1)*1.1), wLo=fbm(pLo+fbm(pLo*.6-1.7)*1.3);
                    float horizonFade=smoothstep(.0,.07,h), calm=1-_ZenithCalm*smoothstep(.25,.85,h), nearHorizon=saturate(1-h*1.6);
                    float covHi=_Coverage, covLo=_Coverage+.04;
                    float cHi=smoothstep(covHi-_Sharp,covHi+_Sharp,wHi)*horizonFade*calm, cLo=smoothstep(covLo-_Sharp,covLo+_Sharp,wLo)*horizonFade*calm*saturate(1-h*1.8);
                    // directional density test toward the light (outward/down = toward the horizon glow): thinner there = lit rim
                    float litHi=saturate((wHi-fbm(pHi+out2*.18))*6), litLo=saturate((wLo-fbm(pLo+out2*.18))*6);
                    float bodyHi=saturate((wHi-covHi)/.08), bodyLo=saturate((wLo-covLo)/.08);
                    float3 hi=lerp(lerp(_CloudLit.rgb,_CloudDark.rgb,bodyHi),_CloudLit.rgb,litHi*(.35+.65*nearHorizon));
                    float3 lo=lerp(lerp(_CloudLit.rgb,_CloudDark.rgb*.85,bodyLo),_CloudLit.rgb*1.08,litLo*(.45+.55*nearHorizon));
                    float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,smoothstep(0,.7,h));
                    sky*=.88+.24*fbm(d.xz/max(.13,h+.22)*1.2+float2(_Time.y*.002,0));   // gentle variation in the gaps only
                    c=lerp(sky,hi,cHi);
                    c=lerp(c,lo,cLo);
                    c+=(_CloudLit.rgb*1.8+float3(.05,.04,.09))*max(bodyHi*cHi,bodyLo*cLo)*saturate(1-h*1.4)*_MenuStormFlash;   // lightning lights the undersides
                    c+=(hash(i.pos.xy)-.5)/255.;                                  // dither against banding in the dark zenith
                }
                c=lerp(_Low.rgb,c,smoothstep(-.1,.1,h));return float4(c,1);
            }
            ENDHLSL
        }
    }
}
