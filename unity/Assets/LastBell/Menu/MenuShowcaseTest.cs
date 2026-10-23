using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // Opt-in rendered runtime acceptance, with real 15-second timing and two full rotations.
    public sealed class MenuShowcaseTest : MonoBehaviour
    {
        static bool installed;
        readonly List<string> report = new List<string>(), errors = new List<string>();
        int failures;
        string output;
        public static void Install()
        {
            if (installed || !Environment.GetCommandLineArgs().Contains("-lastbellShowcaseTest")) return;
            installed = true; var go = new GameObject("Showcase acceptance lifetime"); DontDestroyOnLoad(go); go.AddComponent<MenuShowcaseTest>();
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellOut");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.persistentDataPath + "/showcase-test";
            Directory.CreateDirectory(output); Application.logMessageReceived += Log;
            Application.runInBackground = true; Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(2);
            var menu = FindAnyObjectByType<MainMenu>(); var show = menu.GetComponent<MenuShowcase>(); var music = menu.Music; var source = music.Source;
            Check("single combined main menu is current scene", SceneManager.GetActiveScene().path == MainMenu.ScenePath && SceneManager.sceneCount == 1);
            Check("opening song plays first", music.PlayingOpening && source.clip.name == "MainMenuOpening" && source.isPlaying && source.time > 0);
            Check("all catalog stages embedded", show.Stages.Length == RunMaps.All.Length && show.Stages.Select(s => s.Name).SequenceEqual(RunMaps.All.Select(m => m.Name)));
            Check("original menu environment preserved inactive", GameObject.Find("Original menu environment (preserved, inactive)") == null);
            int materials = 0, objects = 0; float previousSample = source.time; int previousChanges = 0;
            for (int visit = 0; visit <= 10; visit++)
            {
                float due = visit * MenuShowcase.StageSeconds + (visit == 0 ? 10 : 6);
                while (show.Elapsed < due) yield return null;
                int index = visit % show.Stages.Length; var stage = show.Stages[index];
                Check("stage order " + visit + " " + stage.Name, show.CurrentStage == index && show.Changes == visit);
                Check("one visible environment " + visit, show.Stages.Count(s => s.Environment.activeInHierarchy) == 1 && stage.Environment.activeInHierarchy);
                Check("party and boss " + visit, menu.Battle.PartySize == stage.PartySize && menu.Battle.ActorCount == stage.PartySize + 1 && stage.PartySize >= 3 && menu.Battle.BossResource == stage.Boss && menu.Battle.AllModelsPresent);
                report.Add("ACTIVITY " + visit + " choreography=" + menu.Battle.Elapsed + " travel=" + string.Join(",", menu.Battle.ActorTravel.Take(menu.Battle.ActorCount)));
                Check("active movement and skills " + visit, menu.Battle.ActorTravel.Take(menu.Battle.ActorCount).All(d => d > .1f) && menu.Battle.SpellsPresented > 0);
                Check("one camera listener and event system " + visit, FindObjectsByType<Camera>().Count(c => c.enabled) == 1 && FindObjectsByType<AudioListener>().Count(c => c.enabled) == 1 && FindObjectsByType<EventSystem>().Length == 1);
                Check("presentation has no combat authority " + visit, !RunDirector.Instance && !FindAnyObjectByType<BossEncounter>() && FindObjectsByType<PlayerController>().All(p => !p.enabled && p.World == null && p.Kit == null));
                Check("same uninterrupted music source " + visit, source == music.Source && source.isPlaying && (!music.PlayingOpening || source.time >= previousSample));
                previousSample = source.time;
                if (visit > 0) Check("15-second covered swap " + visit, show.LastChangeCovered && Mathf.Abs(show.LastChangeTime - visit * 15) < .5f);
                if (visit < 5) { yield return Shot("arena-" + index + ".png"); }
                if (visit == 5) { materials = Resources.FindObjectsOfTypeAll<Material>().Length; objects = FindObjectsByType<Transform>().Length; }
                if (visit == 10)
                {
                    report.Add("MATERIALS first=" + materials + " second=" + Resources.FindObjectsOfTypeAll<Material>().Length);
                    Check("bounded materials after second full rotation", Resources.FindObjectsOfTypeAll<Material>().Length <= materials + 4);
                    Check("bounded objects after second full rotation", FindObjectsByType<Transform>().Length <= objects + 8);
                    break;
                }
                while (show.Elapsed < (visit + 1) * 15 - .05f) yield return null;
                // The facade is in front of the battle and below the menu, with no raycast targets.
                Check("building covers background " + visit, show.CoversScreen);
                var eventData = new PointerEventData(EventSystem.current) { position = menu.SettingsButton.transform.position };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(eventData, hits);
                Check("menu raycast survives wipe " + visit, hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(menu.SettingsButton.transform));
                ExecuteEvents.Execute(menu.SettingsButton.gameObject, eventData, ExecuteEvents.pointerClickHandler);
                Check("settings usable during wipe " + visit, menu.SettingsOpen);
                if (visit == 0) { yield return Shot("building-wipe-settings.png"); }
                menu.CloseSettings(); previousChanges = show.Changes;
                while (show.Changes == previousChanges) yield return null;
            }
            Check("eight-player party included", show.Stages.Any(s => s.PartySize == 8));
            // Seek the genuine opening clip to its end to exercise normal playlist advancement.
            if (music.PlayingOpening)
            {
                source.time = Mathf.Max(0, source.clip.length - .5f);
                float trackDeadline = Time.realtimeSinceStartup + 15;
                while ((music.PlayingOpening || !source.isPlaying) && Time.realtimeSinceStartup < trackDeadline) yield return null;
            }
            report.Add("PLAYLIST opening=" + music.PlayingOpening + " clip=" + source.clip.name + " state=" + source.clip.loadState + " playing=" + source.isPlaying + " time=" + source.time);
            Check("existing looping playlist resumes after opening", !music.PlayingOpening && source.clip.name == "MainMenuMusic" && source.loop && source.isPlaying);
            menu.OpenSelection(false); Check("showcase stops on navigation", !show.enabled && !menu.Battle.enabled);
            float deadline = Time.realtimeSinceStartup + 45;
            while (!FindAnyObjectByType<HeroSelection>() && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(1);
            Check("selection cleans up showcase", FindAnyObjectByType<HeroSelection>() && !FindAnyObjectByType<MenuShowcase>() && !FindAnyObjectByType<MenuBattle>());
            Check("selection preserves music", FindObjectsByType<MenuMusic>().Length == 1 && music.Source == source && source.isPlaying);
            SceneManager.LoadSceneAsync(MainMenu.ScenePath);
            deadline = Time.realtimeSinceStartup + 45;
            while (!FindAnyObjectByType<MainMenu>() && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(2);
            Check("return starts clean first arena", FindObjectsByType<MenuShowcase>().Length == 1 && FindAnyObjectByType<MenuShowcase>().CurrentStage == 0 && FindObjectsByType<MenuBattle>().Length == 1);
            Check("return preserves music source", music.Source == source && source.isPlaying && FindObjectsByType<MenuMusic>().Length == 1);
            Check("zero runtime errors", errors.Count == 0); report.AddRange(errors);
            report.Add($"RESULT: {report.Count(l => l.StartsWith("PASS"))} passed / {failures} failed");
            File.WriteAllLines(Path.Combine(output, "showcase-report.txt"), report); Application.logMessageReceived -= Log;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit(failures == 0 ? 0 : 1);
#endif
        }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG()); Destroy(image);
        }
        void Check(string name, bool pass) { report.Add((pass ? "PASS " : "FAIL ") + name); if (!pass) failures++; Debug.Log(report[report.Count - 1]); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
    }
}
