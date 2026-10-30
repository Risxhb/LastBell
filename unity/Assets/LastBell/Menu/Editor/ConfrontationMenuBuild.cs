using System;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Menu.EditorTools
{
    public static class ConfrontationMenuBuild
    {
        const string Gen="Assets/LastBell/Menu/ConfrontationGenerated";
        [MenuItem("Last Bell/Alternative Menu/Open Confrontation Scene")]
        public static void Open()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(!File.Exists(ConfrontationMenu.ScenePath))Prepare();
            else EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
        }
        public static void Prepare()
        {
            AssetDatabase.Refresh();
            if(File.Exists(ConfrontationMenu.ScenePath))throw new IOException("Alternative scene exists; open it instead of overwriting edits.");
            AssetDatabase.CreateFolder("Assets/LastBell/Menu","ConfrontationGenerated");
            AssetDatabase.CopyAsset(RunMaps.All[0].SourceScene,ConfrontationMenu.ScenePath);
            var scene=EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var t in root.GetComponentsInChildren<Transform>(true))if(PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))PrefabUtility.UnpackPrefabInstance(t.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))if(!(b is UniversalAdditionalCameraData)&&!(b is UniversalAdditionalLightData))UnityEngine.Object.DestroyImmediate(b);
                foreach(var source in root.GetComponentsInChildren<AudioSource>(true))UnityEngine.Object.DestroyImmediate(source);
                foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith("ScaleReference")||t.name.StartsWith("Telegraph")||t.name=="Sea of Clouds")t.gameObject.SetActive(false);
            }
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.20f,.19f,.34f);RenderSettings.ambientEquatorColor=new Color(.12f,.13f,.22f);RenderSettings.ambientGroundColor=new Color(.07f,.07f,.12f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.012f;RenderSettings.fogColor=new Color(.075f,.065f,.13f);
            var sky=new Material(Shader.Find("LastBell/MenuStormSky"));sky.SetColor("_Zenith",new Color(.018f,.014f,.045f));sky.SetColor("_Mid",new Color(.10f,.065f,.19f));sky.SetColor("_Horizon",new Color(.24f,.17f,.34f));sky.SetColor("_Low",new Color(.06f,.05f,.12f));sky.SetColor("_Glow",new Color(.13f,.11f,.22f));AssetDatabase.CreateAsset(sky,Gen+"/StormSky.mat");RenderSettings.skybox=sky;
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if(l.type==LightType.Directional){l.color=new Color(.52f,.50f,.85f);l.intensity=.7f;l.transform.rotation=Quaternion.Euler(40,-35,0);}
                else {l.color=new Color(1,.48f,.16f);l.range=9;l.intensity=2.7f;l.shadows=LightShadows.None;}
            }
            var floorSource=AssetDatabase.LoadAssetAtPath<Material>("Assets/LastBell/Arenas/KeyArt/Generated/Paving.mat");
            var floor=new Material(floorSource);floor.SetFloat("_Smoothness",.8f);floor.SetFloat("_Metallic",.25f);floor.SetColor("_BaseColor",new Color(.34f,.37f,.46f));AssetDatabase.CreateAsset(floor,Gen+"/WetPaving.mat");
            foreach(var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){var mats=r.sharedMaterials;bool changed=false;for(int i=0;i<mats.Length;i++)if(mats[i]==floorSource){mats[i]=floor;changed=true;}if(changed)r.sharedMaterials=mats;}
            foreach(var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                var profile=UnityEngine.Object.Instantiate(v.sharedProfile);profile.name="Confrontation post";
                if(profile.TryGet<MotionBlur>(out var motion))motion.active=false;if(profile.TryGet<DepthOfField>(out var dof))dof.active=false;
                if(profile.TryGet<Bloom>(out var bloom))bloom.intensity.value=.18f;AssetDatabase.CreateAsset(profile,Gen+"/StormPost.asset");v.sharedProfile=profile;
            }
            var host=new GameObject("Confrontation menu — presentation only");host.AddComponent<ConfrontationMenu>();
            host.AddComponent<ConfrontationWeather>().ParticleShader=Shader.Find("LastBell/MenuStormParticle");
            LightAt("Hero cool rim",new Vector3(3,3,4),new Color(.35f,.45f,1),16,7);
            LightAt("Hero amber edge",new Vector3(-1,2,7),new Color(1,.55f,.22f),12,6);
            LightAt("Bellwarden silhouette",new Vector3(0,7,-11),new Color(.6f,.4f,1),40,16);
            LightAt("Bellwarden warm key",new Vector3(2,5,-3),new Color(1,.55f,.22f),28,12);
            var cam=Camera.main;cam.transform.SetPositionAndRotation(new Vector3(0,1.7f,10.5f),Quaternion.LookRotation(new Vector3(-1,2.8f,-7)-new Vector3(0,1.7f,10.5f)));cam.fieldOfView=42;
            // A dedicated baked probe is economical and never changes gameplay probes/materials.
            var probe=new GameObject("Wet stone reflection").AddComponent<ReflectionProbe>();probe.transform.position=new Vector3(0,2,0);probe.size=new Vector3(32,14,32);probe.resolution=128;probe.mode=UnityEngine.Rendering.ReflectionProbeMode.Realtime;probe.refreshMode=UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;probe.timeSlicingMode=UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("Confrontation scene created. Default EditorBuildSettings preserved.");
        }
        static void LightAt(string name,Vector3 pos,Color color,float intensity,float range){var l=new GameObject(name).AddComponent<Light>();l.type=LightType.Point;l.transform.position=pos;l.color=color;l.intensity=intensity;l.range=range;}
        public static void Refine()
        {
            var scene=EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            var menu=UnityEngine.Object.FindAnyObjectByType<ConfrontationMenu>();
            menu.HeroPosition=new Vector3(1.05f,0,7.6f);menu.BossPosition=new Vector3(0,0,-17);menu.CameraTarget=new Vector3(-1,2.1f,-7);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())
            {
                if(light.name=="Bellwarden silhouette")light.transform.position=new Vector3(0,7,-22);
                if(light.name=="Bellwarden warm key"){light.transform.position=new Vector3(2,5,-12);light.intensity=20;}
                if(light.name=="Hero amber edge")light.intensity=8;
                if(light.type==LightType.Directional)light.intensity=.45f;
            }
            RenderSettings.ambientSkyColor=new Color(.14f,.13f,.24f);RenderSettings.ambientEquatorColor=new Color(.09f,.1f,.17f);
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(t.name.Contains("Tower")||t.name.Contains("Bridge"))Debug.Log("ARCHITECTURE "+t.name+" "+t.position);
            foreach(var material in UnityEngine.Object.FindObjectsByType<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())Debug.Log("SCENERY MATERIAL "+material.name+" "+material.shader.name);
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        public static void FinishArt()
        {
            var scene=EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            var menu=UnityEngine.Object.FindAnyObjectByType<ConfrontationMenu>();
            menu.CameraPosition=new Vector3(0,1.05f,10.5f);menu.CameraTarget=new Vector3(-1,1.8f,-7);menu.HeroPosition=new Vector3(.95f,0,7.8f);
            var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach(var t in all)if(t.name=="Bush"||t.name=="HangingRock")t.gameObject.SetActive(false);
            var dial=all.FirstOrDefault(t=>t.name=="DialFace");
            if(dial&&dial.TryGetComponent<MeshRenderer>(out var dialRenderer))
            {
                var mat=new Material(dialRenderer.sharedMaterial){name="Rain darkened dial"};
                if(mat.HasProperty("baseColorFactor"))mat.SetColor("baseColorFactor",new Color(.045f,.055f,.09f,1));
                if(mat.HasProperty("roughnessFactor"))mat.SetFloat("roughnessFactor",.16f);
                if(mat.HasProperty("metallicFactor"))mat.SetFloat("metallicFactor",.45f);
                AssetDatabase.CreateAsset(mat,Gen+"/WetDial.mat");dialRenderer.sharedMaterial=mat;
            }
            var wet=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/WetPaving.mat");wet.SetColor("_BaseColor",new Color(.16f,.19f,.25f));EditorUtility.SetDirty(wet);
            // Reuse complete authored floating bell-tower groups as the distant skyline.
            var island=all.Where(t=>t.name=="Island").OrderByDescending(t=>GroupBounds(t).size.y).FirstOrDefault();
            if(island&&!GameObject.Find("Confrontation bell skyline"))
            {
                var skyline=new GameObject("Confrontation bell skyline").transform;
                var spots=new[]{new Vector3(14,-3,-39),new Vector3(-11,-4,-43),new Vector3(3,-5,-58)};
                for(int i=0;i<spots.Length;i++){var copy=UnityEngine.Object.Instantiate(island.gameObject,skyline);copy.name="Reused bell tower "+i;copy.transform.position=spots[i];copy.transform.localScale*=.65f;}
            }
            if(!GameObject.Find("Distant Bellwarden dais"))
            {
                var dais=GameObject.CreatePrimitive(PrimitiveType.Cylinder);dais.name="Distant Bellwarden dais";dais.transform.position=new Vector3(0,-.32f,-17);dais.transform.localScale=new Vector3(14,.3f,14);dais.GetComponent<Renderer>().sharedMaterial=wet;UnityEngine.Object.DestroyImmediate(dais.GetComponent<Collider>());
            }
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        // Read-only inventory of visible scenery materials; does not save the scene.
        public static void Inspect()
        {
            var scene=EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(false)))
                foreach(var m in r.sharedMaterials.Where(m=>m))
                {
                    string props=string.Join(" ",new[]{"_BaseColor","baseColorFactor","_EmissionColor","emissiveFactor","_Smoothness","roughnessFactor","_Metallic","metallicFactor"}.Where(m.HasProperty).Select(p=>p+"="+(p.Contains("Color")||p.Contains("Factor")&&!p.Contains("roughness")&&!p.Contains("metallic")?m.GetColor(p).ToString():m.GetFloat(p).ToString("0.00"))));
                    Debug.Log($"INSPECT {PathOf(r.transform)} | {m.name} | {m.shader.name} | {AssetDatabase.GetAssetPath(m)} | {props} | b={r.bounds.center} s={r.bounds.size}");
                }
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))Debug.Log($"INSPECT LIGHT {l.name} {l.type} {l.transform.position} {l.color} i={l.intensity} r={l.range}");
            foreach(var p in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))Debug.Log($"INSPECT PROBE {p.name} i={p.intensity}");
            Debug.Log($"INSPECT FOG {RenderSettings.fogDensity} {RenderSettings.fogColor}");
        }
        // Art-direction refinement pass. Absolute values, so re-running is safe. Shared glTF materials
        // are never edited: menu-only copies are created here and remapped in this scene alone.
        public static void PolishPass()
        {
            var scene=EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            var menu=UnityEngine.Object.FindAnyObjectByType<ConfrontationMenu>();
            menu.CameraPosition=new Vector3(.6f,.75f,10.9f);menu.CameraTarget=new Vector3(-.6f,2.2f,-7);
            menu.HeroPosition=new Vector3(1.45f,0,8.3f);menu.BossPosition=new Vector3(0,0,-15);
            menu.Classes=new[]{
                new ConfrontationMenu.Presentation{Class="Vanguard",Offset=new Vector3(.15f,0,0),Yaw=6,Scale=1},
                new ConfrontationMenu.Presentation{Class="Chimesinger",Offset=new Vector3(.1f,0,.15f),Yaw=4,Scale=.97f},
                new ConfrontationMenu.Presentation{Class="Arcanist",Offset=new Vector3(0,0,.15f),Yaw=4,Scale=1}};
            EditorUtility.SetDirty(menu);
            // 1. Damp stone instead of a mirror-bright graphic disc.
            var dial=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/WetDial.mat");dial.SetFloat("metallicFactor",.08f);dial.SetFloat("roughnessFactor",.42f);EditorUtility.SetDirty(dial);
            var paving=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/WetPaving.mat");paving.SetFloat("_Smoothness",.55f);paving.SetFloat("_Metallic",.05f);EditorUtility.SetDirty(paving);
            foreach(var p in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))p.intensity=.45f;
            // 2. Quieter windows and violet backlight so the boss separates from the tower wall.
            var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).ToArray();
            Remap(renderers,"GlowWindow","MenuWindowGlow",m=>m.SetColor("baseColorFactor",new Color(1.202f,.841f,.447f,1)*.42f),_=>true);
            Remap(renderers,"GlowRose","MenuRoseGlow",m=>m.SetColor("baseColorFactor",new Color(1.339f,1.043f,.637f,1)*.3f),_=>true);
            // 6. Darker damp gold only on the large dial rim ring in front of the camera.
            var gold=renderers.Where(r=>PathOf(r.transform).StartsWith("Bellkeeper's Dais/Platform/")&&r.bounds.size.x>10)
                .SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m&&m.HasProperty("metallicFactor")&&m.GetFloat("metallicFactor")>.7f);
            if(gold||AssetDatabase.LoadAssetAtPath<Material>(Gen+"/MenuDampGold.mat"))Remap(renderers,gold?gold.name:"","MenuDampGold",m=>{m.SetColor("baseColorFactor",new Color(.55f,.42f,.22f,1));m.SetFloat("roughnessFactor",.5f);m.SetFloat("metallicFactor",.6f);m.SetColor("emissiveFactor",new Color(.021f,.008f,.001f,1));},
                r=>PathOf(r.transform).StartsWith("Bellkeeper's Dais/Platform/")&&r.bounds.size.x>10,gold);
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if(l.name=="Bellwarden silhouette"){l.transform.position=new Vector3(0,6,-24);l.intensity=70;l.range=22;l.color=new Color(.55f,.42f,1);}
                if(l.name=="Hero amber edge"){l.transform.position=new Vector3(-.4f,1.8f,6.4f);l.intensity=6;l.range=4.5f;}
            }
            RenderSettings.fogDensity=.02f;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("Confrontation polish pass applied"+(gold?" (rim "+gold.name+")":" (rim material not found)"));
        }
        // Second art-direction pass (AAA-bar critic). Re-runnable: generated objects are rebuilt each time and
        // all values are absolute. Production Brass Foundry towers replace the flat key-art skyline; every material
        // is a menu-only copy, so the gameplay arenas are unaffected.
        const string Area2="Assets/LastBell/Areas/Area2";
        public static void AaaPass()
        {
            var scene=EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            T Find<T>(string name) where T:Component=>all.Where(t=>t&&t.name==name).Select(t=>t.GetComponent<T>()).FirstOrDefault(c=>c);
            foreach(var name in new[]{"Confrontation skyline — production towers","Confrontation menu lanterns","Confrontation foreground framing","Bellwarden front fill"})
                foreach(var t in all.Where(t=>t&&t.name==name).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
            all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();

            // R1. Remove the flat key-art towers behind the boss; keep the plaza, lanterns, banners and debris.
            foreach(var t in all.Where(t=>t.name=="Confrontation bell skyline"))t.gameObject.SetActive(false);
            var dais=all.FirstOrDefault(t=>t.name=="Bellkeeper's Dais"&&t.parent==null);
            foreach(Transform child in dais)
                if(child.name.StartsWith("Island")){child.gameObject.SetActive(true);var b=GroupBounds(child);bool behind=b.center.z<-14;child.gameObject.SetActive(!behind);if(behind)Debug.Log("AAA: hid placeholder "+child.name+" "+b.center);}
            var masonry=MenuCopy(Area2+"/Generated/Materials/MasonryDark.mat","MenuMasonry",m=>{m.SetColor("_BaseColor",new Color(.34f,.31f,.44f,1));m.SetFloat("_Smoothness",.12f);});
            var roof=MenuCopy(Area2+"/Generated/Materials/RoofIron.mat","MenuRoofIron",m=>{m.SetColor("_BaseColor",new Color(.19f,.17f,.25f,1));m.SetFloat("_Smoothness",.3f);});
            var brass=MenuCopy(Area2+"/Generated/Materials/DarkBrass.mat","MenuBrassDark",m=>m.SetColor("_BaseColor",new Color(.30f,.22f,.13f,1)));
            var slit=MenuCopy(Area2+"/Generated/Materials/GlowWindow.mat","MenuWindowSlit",m=>{var c=new Color(1,.55f,.2f,1)*.6f;c.a=1;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Color"))m.SetColor("_Color",c);});
            Material Slot(string slot){switch(slot.Split(' ','.')[0]){case "Roof":case "Iron":case "Chain":case "CityRoof":return roof;case "Brass":case "DarkBrass":return brass;
                case "Glow":case "GlowDim":case "GlowWarm":case "GlowHot":case "Furnace":return slit;default:return masonry;}}
            var skyline=new GameObject("Confrontation skyline — production towers").transform;
            var center=new Vector3(0,0,-15);
            // Tops land in the upper third; fog density sets ~47% (near pair) to ~76–90% (far ring) haze.
            var towers=new[]{("SM_Area2_Tower_Far",new Vector3(-28,-1,-58),25f,1.5f),("SM_Area2_Tower_Far",new Vector3(-10,-3,-80),-30f,1.7f),("SM_Area2_Tower_Far",new Vector3(16,-1,-66),35f,1.6f),
                ("SM_Area2_Tower_Far",new Vector3(32,0,-52),-18f,1.4f),("SM_Area2_Tower_Far",new Vector3(4,-5,-98),15f,1.8f),
                ("SM_Area2_Tower_Near",new Vector3(-20,-4,-34),-40f,1.15f),("SM_Area2_Tower_Near",new Vector3(22,-4,-36),30f,1.15f)};
            for(int i=0;i<towers.Length;i++)
            {
                var (model,pos,yaw,scale)=towers[i];
                var go=Place(Area2+"/Models/"+model+".glb",skyline,model+" "+i,pos,Quaternion.LookRotation(center-pos)*Quaternion.Euler(0,yaw,0),scale);
                foreach(var r in go.GetComponentsInChildren<Renderer>(true))
                {r.sharedMaterials=r.sharedMaterials.Select(m=>Slot(m?m.name:"")).ToArray();r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
                Debug.Log($"AAA: tower {go.name} bounds {GroupBounds(go.transform).min} .. {GroupBounds(go.transform).max}");
            }

            // R2. Fog-separated layers and a cloud floor between them.
            RenderSettings.fogDensity=.016f;RenderSettings.fogColor=new Color(.11f,.09f,.19f);
            var sky=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/StormSky.mat");sky.SetColor("_Horizon",new Color(.30f,.20f,.40f));EditorUtility.SetDirty(sky);
            foreach(var t in all.Where(t=>t.name=="Sea of Clouds"))t.gameObject.SetActive(true);
            // From a 0.75 m camera the floor is seen at grazing angles, where gloss mirrors the whole sky.
            foreach(var p in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))p.intensity=.25f;

            // R3. Darker wet stone; numerals become damp gold instead of glowing ivory.
            var paving=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/WetPaving.mat");paving.SetColor("_BaseColor",new Color(.10f,.11f,.15f));paving.SetFloat("_Smoothness",.32f);EditorUtility.SetDirty(paving);
            var dial=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/WetDial.mat");dial.SetColor("emissiveFactor",new Color(.35f,.26f,.12f,1));dial.SetFloat("roughnessFactor",.62f);EditorUtility.SetDirty(dial);
            // The pale "floor" is the platform body's white paving slots showing through the dial; darken a menu copy.
            var body=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Where(r=>PathOf(r.transform).EndsWith("Platform/Node-1")).ToArray();
            var white=body.SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m&&m.HasProperty("baseColorFactor")&&m.GetColor("baseColorFactor").r>.9f);
            var wetStone=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/MenuWetStone.mat");
            if(white||wetStone)
            {
                wetStone=MenuCopy("","MenuWetStone",m=>{m.SetColor("baseColorFactor",new Color(.16f,.17f,.24f,1));m.SetFloat("roughnessFactor",.62f);m.SetFloat("metallicFactor",0);},white);
                int swapped=0;foreach(var r in body){var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)if(mats[i]&&mats[i]!=wetStone&&mats[i].HasProperty("baseColorFactor")&&mats[i].GetColor("baseColorFactor").r>.9f){mats[i]=wetStone;swapped++;}r.sharedMaterials=mats;}
                Debug.Log("AAA: platform paving slots darkened: "+swapped);
            }
            var gold=AssetDatabase.LoadAssetAtPath<Material>(Gen+"/MenuDampGold.mat");if(gold){gold.SetColor("baseColorFactor",new Color(.30f,.22f,.12f,1));EditorUtility.SetDirty(gold);}

            // R4. Hero as a backlit silhouette; keep the boss readable with its own fill.
            var rim=Find<Light>("Hero cool rim");if(rim){rim.transform.position=new Vector3(3.5f,3,6.5f);rim.intensity=12;rim.range=6;}
            var amber=Find<Light>("Hero amber edge");if(amber){amber.transform.position=new Vector3(-1.2f,1.6f,7.2f);}
            RenderSettings.ambientSkyColor=new Color(.12f,.11f,.22f);RenderSettings.ambientEquatorColor=new Color(.08f,.08f,.14f);RenderSettings.ambientGroundColor=new Color(.05f,.05f,.08f);
            var fill=new GameObject("Bellwarden front fill").AddComponent<Light>();fill.type=LightType.Point;fill.transform.position=new Vector3(1,4.5f,-9);fill.color=new Color(.75f,.7f,1);fill.intensity=14;fill.range=9;fill.shadows=LightShadows.None;
            // Warm points stepping back toward the boss: copies of the plaza's own lantern.
            var source=all.FirstOrDefault(t=>t.name=="Lantern"&&t.GetComponentInChildren<Renderer>(true));
            var lanterns=new GameObject("Confrontation menu lanterns").transform;
            if(source)foreach(var p in new[]{new Vector3(6,0,-3),new Vector3(8,0,-11),new Vector3(-7,0,-24)})
            {
                var copy=UnityEngine.Object.Instantiate(source.gameObject,lanterns);copy.name="Menu Lantern";copy.SetActive(true);
                copy.transform.position=new Vector3(p.x,source.position.y,p.z);
                var light=copy.GetComponentInChildren<Light>(true);
                if(!light){var top=GroupBounds(copy.transform);light=new GameObject("Lantern Light").AddComponent<Light>();light.transform.SetParent(copy.transform,false);light.transform.position=new Vector3(p.x,top.max.y-.25f,p.z);}
                light.type=LightType.Point;light.color=new Color(1,.48f,.16f);light.range=4;light.intensity=2.4f;light.shadows=LightShadows.None;
            }

            // R5. Out-of-focus dark rocks frame the lower corners.
            var debris=all.FirstOrDefault(t=>t.name=="Debris"&&t.GetComponent<Renderer>());
            var frame=new GameObject("Confrontation foreground framing").transform;
            if(debris)
            {
                var dark=MenuCopy(AssetDatabase.GetAssetPath(debris.GetComponent<Renderer>().sharedMaterial),"MenuForegroundRock",m=>{if(m.HasProperty("baseColorFactor"))m.SetColor("baseColorFactor",new Color(.02f,.018f,.03f,1));if(m.HasProperty("roughnessFactor"))m.SetFloat("roughnessFactor",.9f);},debris.GetComponent<Renderer>().sharedMaterial);
                foreach(var (pos,scale,yaw) in new[]{(new Vector3(3.3f,-.25f,9.5f),1.5f,20f),(new Vector3(2.5f,-.35f,10.1f),.9f,140f),(new Vector3(-2.5f,-.25f,9.7f),1.3f,75f)})
                {
                    var rock=UnityEngine.Object.Instantiate(debris.gameObject,frame);rock.name="Foreground rock";rock.transform.position=pos;rock.transform.rotation=Quaternion.Euler(0,yaw,0);rock.transform.localScale=Vector3.one*scale;
                    foreach(var r in rock.GetComponentsInChildren<Renderer>()){r.sharedMaterial=dark;r.shadowCastingMode=ShadowCastingMode.Off;}
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log($"AAA pass applied: {towers.Length} towers, lanterns={(source?3:0)}, framing={(debris?3:0)}");
        }
        static Material MenuCopy(string sourcePath,string name,Action<Material> adjust,Material source=null)
        {
            var path=Gen+"/"+name+".mat";source=source??AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            var copy=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!copy){if(!source)throw new FileNotFoundException("Menu copy source missing: "+sourcePath);copy=new Material(source){name=name};AssetDatabase.CreateAsset(copy,path);}
            else if(source)copy.CopyPropertiesFromMaterial(source);
            adjust(copy);EditorUtility.SetDirty(copy);return copy;
        }
        static GameObject Place(string modelPath,Transform parent,string name,Vector3 pos,Quaternion rot,float scale)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);if(!asset)throw new FileNotFoundException(modelPath);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            go.name=name;go.transform.SetPositionAndRotation(pos,rot);go.transform.localScale=Vector3.one*scale;return go;
        }
        static void Remap(Renderer[] renderers,string sourceName,string copyName,Action<Material> adjust,Func<Renderer,bool> include,Material source=null)
        {
            var path=Gen+"/"+copyName+".mat";
            source=source??(string.IsNullOrEmpty(sourceName)?null:renderers.SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m&&m.name==sourceName));
            var copy=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!source&&!copy){Debug.LogWarning("Polish: "+sourceName+" not found");return;}
            if(!copy){copy=new Material(source){name=copyName};AssetDatabase.CreateAsset(copy,path);}
            else if(source&&source!=copy)copy.CopyPropertiesFromMaterial(source);
            adjust(copy);EditorUtility.SetDirty(copy);
            if(!source||source==copy)return;
            int count=0;
            foreach(var r in renderers.Where(include)){var mats=r.sharedMaterials;bool changed=false;for(int i=0;i<mats.Length;i++)if(mats[i]==source){mats[i]=copy;changed=true;}if(changed){r.sharedMaterials=mats;count++;}}
            Debug.Log($"Polish: {sourceName} -> {copyName} on {count} renderers");
        }
        static string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name:t.name;
        static Bounds GroupBounds(Transform root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>(true);var b=new Bounds(root.position,Vector3.zero);foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
        [MenuItem("Last Bell/Alternative Menu/Preview in Play Mode")]
        public static void Preview(){Open();MenuAppearance.Selected=MenuStyle.Alternative;EditorApplication.isPlaying=true;}
        public static void ValidatePlayMode(){ConfigureLogo();EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);EditorApplication.playModeStateChanged+=FocusGame;EditorApplication.isPlaying=true;}
        static void ConfigureLogo()
        {
            AssetDatabase.Refresh();
            ConfigureMenus();
            var importer=AssetImporter.GetAtPath("Assets/LastBell/Menu/Resources/Menu/ConfrontationLogo.png") as TextureImporter;
            if(!importer)throw new FileNotFoundException("ConfrontationLogo.png is required.");
            if(importer.textureType==TextureImporterType.Sprite&&importer.alphaIsTransparency)return;
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        public static void ConfigureMenus()
        {
            if(!EditorBuildSettings.scenes.Any(s=>s.path==ConfrontationMenu.ScenePath&&s.enabled))
                EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(s=>s.path!=ConfrontationMenu.ScenePath).Concat(new[]{new EditorBuildSettingsScene(ConfrontationMenu.ScenePath,true)}).ToArray();
        }
        static void FocusGame(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode)return;EditorApplication.playModeStateChanged-=FocusGame;
            var game=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));game.Show();game.Focus();
        }
        [MenuItem("Last Bell/Current Player/Build Windows Player")]
        public static void BuildWindows()
        {
            ConfigureLogo();
            // Keep running when the window loses focus (Risxhb, October 26, 2026). The web demo build turns this off for
            // browsers, so the Windows build sets it back every time.
            PlayerSettings.runInBackground = true;
            if(!File.Exists(ConfrontationMenu.ScenePath))Prepare();
            var output=Environment.GetEnvironmentVariable("LASTBELL_WINDOWS_OUTPUT");
            if(string.IsNullOrEmpty(output))output="Builds/Player/LastBell.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=MenuAppearance.BuildScenes,locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            Debug.Log($"Confrontation build: {report.summary.result}, {report.summary.totalErrors} errors/{report.summary.totalWarnings} warnings");
            foreach(var step in report.steps)foreach(var msg in step.messages)if(msg.type==LogType.Error||msg.type==LogType.Exception)Debug.Log("BUILD ERROR ["+step.name+"]: "+msg.content);
            if(Application.isBatchMode)EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
        }
    }
}

