using System;
using System.Collections;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // Opt-in authoring capture. Uses fully initialized playable scenes and never saves a scene or changes an arena.
    public sealed class MapPreviewCapture:MonoBehaviour
    {
        public static bool AuthoringRequested;
        public static bool Requested=>AuthoringRequested||Environment.GetCommandLineArgs().Contains("-lastbellCapturePreviews");
        public static void Launch(){var go=new GameObject("Arena preview capture");DontDestroyOnLoad(go);go.AddComponent<MapPreviewCapture>();}
        IEnumerator Start()
        {
            Application.runInBackground=true;MenuFlowTest.SetResolution(960,540);
            string directory=Application.dataPath+"/LastBell/Menu/Resources/Menu/Maps";Directory.CreateDirectory(directory);
            var sources=new System.Text.StringBuilder("Actual Unity Play Mode captures. Camera at (0,26,20), target origin. No scenes saved.\n");
            foreach(var map in RunMaps.All)
            {
                yield return SceneManager.LoadSceneAsync(map.RunScene);yield return new WaitForSeconds(2);
                if(RunDirector.Instance)RunDirector.Instance.enabled=false;
                foreach(var hud in FindObjectsByType<RunHud>())hud.enabled=false;
                var cam=Camera.main;cam.orthographic=false;cam.rect=new Rect(0,0,1,1);cam.fieldOfView=40;cam.aspect=16f/9;
                cam.transform.SetPositionAndRotation(new Vector3(0,26,20),Quaternion.LookRotation(-new Vector3(0,26,20)));
                yield return new WaitForSeconds(.5f);yield return new WaitForEndOfFrame();
                var texture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);texture.Apply();
                File.WriteAllBytes(directory+"/"+map.Id+".png",texture.EncodeToPNG());Destroy(texture);
                using(var sha=System.Security.Cryptography.SHA256.Create())sources.AppendLine(map.Id+" | "+map.RunScene+" | SHA256 "+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(map.RunScene))).Replace("-","").ToLowerInvariant());
            }
            File.WriteAllText("E:/Collab/build-evidence/menu-flow-20261022/preview-sources.txt",sources.ToString());
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            foreach(var map in RunMaps.All)
            {
                var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath("Assets/LastBell/Menu/Resources/Menu/Maps/"+map.Id+".png");
                importer.npotScale=UnityEditor.TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.textureCompression=UnityEditor.TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
    }
}
