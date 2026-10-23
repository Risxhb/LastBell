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
        public static void BuildWindows()
        {
            ConfigureLogo();
            if(!File.Exists(ConfrontationMenu.ScenePath))Prepare();
            var output="E:/Collab/builds/confrontation-menu/LastBell.exe";Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=MenuAppearance.BuildScenes,locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            Debug.Log($"Confrontation build: {report.summary.result}, {report.summary.totalErrors} errors/{report.summary.totalWarnings} warnings");
            if(Application.isBatchMode)EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
        }
    }
}

