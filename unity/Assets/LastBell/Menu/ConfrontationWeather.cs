using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastBell.Menu
{
    // Two bounded meshes instead of a new Particle System package dependency.
    public sealed class ConfrontationWeather : MonoBehaviour
    {
        public Shader ParticleShader;
        sealed class Field
        {
            public Mesh Mesh; public Vector3[] Vertices, Seeds; public Color[] Colors; public int Count; public bool Rain, Near;
        }
        readonly List<Field> fields=new List<Field>();
        readonly List<Material> materials=new List<Material>();
        readonly List<(Transform t,Quaternion rotation)> banners=new List<(Transform,Quaternion)>();
        readonly List<Light> torches=new List<Light>();
        Texture2D puff;Camera view;
        Light sun;float sunBase,nextFlash,flashStart=-10;
        // Lightning: irregular 7–12 s gaps, ~0.15 s double pulse, capped (no regular strobing).
        const float FlashPeak=.55f;
        void Start()
        {
            view=Camera.main;
            puff=new Texture2D(32,32,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++){float d=Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/15.5f;puff.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-d),2)));}puff.Apply();
            bool low=QualitySettings.GetQualityLevel()==0;
            CreateField("Rain",low?180:420,true);CreateField("Drifting ground mist",low?12:26,false);
            CreateField("Near rain streaks",low?50:140,true,true);
            foreach(var light in FindObjectsByType<Light>())if(light.type==LightType.Directional&&light.name=="Sun"){sun=light;sunBase=light.intensity;}
            nextFlash=Time.unscaledTime+Random.Range(4f,7f);
            foreach(var root in gameObject.scene.GetRootGameObjects())foreach(var child in root.GetComponentsInChildren<Transform>(true))
                if(child.name.ToLowerInvariant().Contains("banner"))banners.Add((child,child.localRotation));
            foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.type==LightType.Point&&light.color.r>light.color.b&&light.name.Contains("Lantern"))torches.Add(light);
        }
        void CreateField(string label,int count,bool rain,bool near=false)
        {
            var f=new Field{Count=count,Rain=rain,Near=near,Mesh=new Mesh{name=label},Vertices=new Vector3[count*4],Seeds=new Vector3[count],Colors=new Color[count*4]};
            var uv=new Vector2[count*4];var triangles=new int[count*6];var rng=new System.Random(near?4111:rain?1729:733);
            for(int i=0;i<count;i++)
            {
                f.Seeds[i]=new Vector3((float)rng.NextDouble(),(float)rng.NextDouble(),(float)rng.NextDouble());
                int v=i*4,j=i*6;uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;
                for(int k=0;k<4;k++)f.Colors[v+k]=near?new Color(.66f,.7f,.95f,.12f):rain?new Color(.62f,.68f,.95f,.34f):new Color(.30f,.28f,.50f,.12f);
                triangles[j]=v;triangles[j+1]=v+2;triangles[j+2]=v+1;triangles[j+3]=v;triangles[j+4]=v+3;triangles[j+5]=v+2;
            }
            f.Mesh.MarkDynamic();f.Mesh.vertices=f.Vertices;f.Mesh.uv=uv;f.Mesh.colors=f.Colors;f.Mesh.triangles=triangles;f.Mesh.bounds=new Bounds(new Vector3(0,4,-2),new Vector3(40,20,45));
            var go=new GameObject(label);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=f.Mesh;
            var mat=new Material(ParticleShader);mat.mainTexture=puff;materials.Add(mat);
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            fields.Add(f);
        }
        void Update()
        {
            if(!view)return;float time=Time.unscaledTime;var right=view.transform.right;
            foreach(var f in fields)
            {
                for(int i=0;i<f.Count;i++)
                {
                    var seed=f.Seeds[i];Vector3 p,h,v;
                    // Rain spans the boss dais to just in front of the hero, so streaks cross the dark floor too.
                    // Near layer: long faint streaks just in front of the camera, read as diagonal rain.
                    if(f.Near){p=new Vector3(Mathf.Repeat(seed.x*10-time*1.8f,10)-4.5f,Mathf.Repeat(seed.y*6-time*15,6)-.5f,5+seed.z*4.5f);h=right*.007f;v=new Vector3(.07f,.7f,.03f);}
                    else if(f.Rain){p=new Vector3(Mathf.Repeat(seed.x*26-time*1.2f,26)-13,Mathf.Repeat(seed.y*12-time*12,12),seed.z*29-20);h=right*.022f;v=new Vector3(.04f,.23f,.02f);}
                    // Low mist; the far band pools around the Bellwarden's feet instead of forming a raised strip.
                    else {p=new Vector3(Mathf.Repeat(seed.x*30+time*.13f,30)-15,.2f+seed.y*.45f,seed.z*40-29);h=right*(3+seed.y*2);v=Vector3.up*(.45f+seed.y*.5f);if(p.z<-10){p.y+=.4f;v*=1.6f;}}
                    int at=i*4;f.Vertices[at]=p-h-v;f.Vertices[at+1]=p+h-v;f.Vertices[at+2]=p+h+v;f.Vertices[at+3]=p-h+v;
                }
                f.Mesh.vertices=f.Vertices;
            }
            for(int i=0;i<banners.Count;i++)if(banners[i].t)banners[i].t.localRotation=banners[i].rotation*Quaternion.Euler(0,0,Mathf.Sin(time*.9f+i)*1.2f);
            float flash=0,since=time-flashStart;
            if(time>=nextFlash){flashStart=time;since=0;nextFlash=time+Random.Range(7f,12f);}
            if(since<.6f)flash=FlashPeak*(Mathf.Exp(-since*28)+(since>.22f?.55f*Mathf.Exp(-(since-.22f)*22):0));
            Shader.SetGlobalFloat("_MenuStormFlash",Mathf.Min(flash,FlashPeak));
            if(sun)sun.intensity=sunBase+.3f*Mathf.Min(flash,FlashPeak)/FlashPeak;
            for(int i=0;i<torches.Count;i++)if(torches[i])torches[i].intensity=2.7f+Mathf.Sin(time*7+i*2)*.18f+Mathf.Sin(time*11+i)*.12f;
        }
        void OnDestroy(){Shader.SetGlobalFloat("_MenuStormFlash",0);foreach(var f in fields)if(f.Mesh)Destroy(f.Mesh);foreach(var m in materials)if(m)Destroy(m);if(puff)Destroy(puff);}
    }
}
