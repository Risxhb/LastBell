using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Art-review capture for the Silt Carillon (miniboss art pass, 9 Nov 2026), not a test:  -siltCarillonArtShots -lastbellOut <folder>
    // Starts the practice fight, holds the script (ArtHold, a labelled dev hook), plays every body clip and writes two frames
    // of each: the real game camera (HUD included) and a close three-quarter camera. Then it lets the real attacks run for the
    // props: a High Tide bell set (surfacing, waiting, tolling, the Drained slump), a Silt Fall on a still player (mounds) and
    // a Drowned Peal. Writes NN_<name>.png and art-shots.txt, then quits.
    public sealed class SiltCarillonArtShots : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-siltCarillonArtShots", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "silt-carillon-art");
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<SiltCarillonArtShots>()) return;
            var go = new GameObject("Silt Carillon art shots"); DontDestroyOnLoad(go); go.AddComponent<SiltCarillonArtShots>();
        }
        sealed class Still : IInputSource { public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + Vector3.forward, Move = Vector2.zero }; }

        // seconds: (clip length, frame to capture). Loops are captured 0.8 s after they start.
        static readonly (string clip, float len, float at)[] OneShots =
        {
            ("TideRaise", 1.2f, 0.45f), ("BellToll", 1.0f, 0.30f), ("SiltPour", 0.8f, 0.7f), ("PealWind", 1.0f, 0.9f), ("PealStrike", 0.9f, 0.20f),
            ("DrainSlump", 0.8f, 0.75f), ("DrainRecover", 0.8f, 0.45f), ("PhaseShift", 2.0f, 1.0f), ("Defeat", 2.5f, 2.45f),
        };

        RunDirector d; SiltCarillonEncounter boss; string dir; int shots; readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        void Start() { dir = OutDir; Directory.CreateDirectory(dir); Application.runInBackground = true; StartCoroutine(Main()); }
        void Note(string s) { log.AppendLine(s); Debug.Log("[silt-art] " + s); }
        void Keep() { if (d && d.World != null) foreach (var p in d.World.Players) if (!p.Down && p.Hp < 100) p.DebugSetHp(100); }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        IEnumerator Until(Func<bool> c, float timeout) { float t = 0; while (!c() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; } }

        void Shot(string name, Vector3 pos, Vector3 look, float fov, int w = 1280, int h = 720)
        {
            var go = new GameObject("capture camera"); var cam = go.AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.fieldOfView = fov; cam.aspect = w / (float)h; cam.orthographic = false;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }
        IEnumerator Game(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        // a close three-quarter view of the boss from its front-left
        void Close(string name)
        {
            var b = boss.transform; var at = b.position + b.forward * 6.8f + b.right * -3.4f + Vector3.up * 3.0f;
            Shot(name, at, b.position + Vector3.up * 1.9f, 42);
        }
        IEnumerator Both(string name) { Close(name + "_close"); yield return Game(name + "_game"); }

        IEnumerator Main()
        {
            yield return new WaitForSeconds(1f);
            int enc = RunDirector.SiltCarillonPractice, vmap = RunMaps.IndexOf(RunDirector.SylvaraMap);
            float t0 = 0; while (RunDirector.Instance == null && t0 < 4) { t0 += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[vmap].RunScene);
            t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            if (!d) { Note("no RunDirector"); Done(); yield break; }
            d.SelectAllies(0); d.SelectDifficulty(Difficulty.Medium);
            d.StartPractice("Tollbreaker", enc);
            t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 40) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            d.RequestReady();
            yield return Until(() => d.Boss is SiltCarillonEncounter, 5);
            boss = d.Boss as SiltCarillonEncounter;
            if (!boss) { Note("no boss"); Done(); yield break; }
            boss.ArtHold = true; boss.Seed = 20261109;
            yield return Until(() => d.State == RunState.Combat, 10);
            d.Player.Input = new Still();
            // the gameplay camera sits on the +Z side looking toward -Z: a player at +Z has the boss in frame
            d.Player.transform.position = new Vector3(0, 0, 7.5f);
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8);
            yield return Wait(1.5f);
            var pr = boss.Presenter;
            Note($"model loaded: {pr && pr.HasModel}; placeholder: {boss.Placeholder}; missing clips: {string.Join(", ", SiltCarillonEncounter.ClipNames.Where(c => !pr || !pr.Has(c)))}");
            Shot("overview_body", new Vector3(0, 3.2f, 9.5f), new Vector3(0, 1.9f, 0), 40);

            // every body clip
            foreach (var c in SiltCarillonEncounter.ClipNames)
            {
                var one = OneShots.FirstOrDefault(o => o.clip == c);
                if (one.clip == null)
                {
                    boss.Cues.Loop = c; yield return Wait(0.8f);
                    yield return Both("clip_" + c);
                    boss.Cues.Loop = "Idle";
                }
                else
                {
                    boss.Cues.Loop = "Idle"; boss.Cues.Play(c, Mathf.RoundToInt(one.len * Sim.Hz));
                    yield return Wait(one.at);
                    yield return Both("clip_" + c);
                    yield return Wait(Mathf.Max(0.3f, one.len - one.at + 0.3f));
                }
            }
            boss.Cues.Loop = "Idle"; yield return Wait(1f);

            // props: a real High Tide bell set; the player waits beside the bell, then rings it
            boss.ArtHold = false; boss.ForcedNext = "TideBells";
            yield return Until(() => boss.LiveBells.Count > 0, 10);
            yield return Wait(1.3f);
            var bell = boss.LiveBells.Count > 0 ? boss.LiveBells[0].h.Origin : Vector3.zero;
            Note($"bell views {boss.BellViews} (model {boss.BellModelLoaded}) at {bell}");
            d.Player.transform.position = bell + (bell - Vector3.zero).normalized * 2.6f;
            yield return Wait(0.6f);
            yield return Game("prop_bell_waiting_game");
            Shot("prop_bell_waiting_close", bell + new Vector3(2.2f, 1.6f, -2.8f), bell + Vector3.up * 0.5f, 40);
            Shot("prop_bell_tide_overhead", new Vector3(0, 26, -12), new Vector3(0, 0, 0), 50);
            d.Player.transform.position = bell;
            yield return Until(() => boss.LiveBells.Count == 0, 12);
            yield return Wait(0.15f);
            Shot("prop_bell_toll_close", bell + new Vector3(2.2f, 1.6f, -2.8f), bell + Vector3.up * 0.5f, 40);
            yield return Game("body_belltoll_live_game");
            yield return Until(() => boss.Drained, 4);
            yield return Wait(0.9f);
            yield return Both("body_drained_live");
            yield return Wait(0.6f);
            Shot("prop_bell_sink_close", bell + new Vector3(2.2f, 1.6f, -2.8f), bell + Vector3.up * 0.5f, 40);
            yield return Until(() => boss.Current == "between blocks", 20);
            boss.ArtHold = true; yield return Wait(0.5f);

            // Silt Fall on a player who stands still: the mounds pile up round them
            d.Player.transform.position = new Vector3(2.5f, 0, 7f);
            boss.ArtHold = false; boss.ForcedNext = "Fall";
            yield return Until(() => boss.MoundsSpawned >= 2, 10);
            yield return Wait(0.3f);
            yield return Both("body_pourloop_live");
            d.Player.transform.position = new Vector3(-2.5f, 0, 8f);
            yield return Until(() => boss.MoundsSpawned >= 4, 6);
            yield return Wait(0.2f);
            Note($"mound views {boss.MoundViews} (model {boss.MoundModelLoaded}), live {boss.MoundViewsLive}");
            yield return Game("prop_mounds_game");
            var mr = boss.PropRoots.FirstOrDefault(); var mp = mr ? mr.position : new Vector3(2.5f, 0, 7f);
            Shot("prop_mounds_close", mp + new Vector3(1.8f, 1.5f, 2.4f), mp + Vector3.up * 0.1f, 40);
            yield return Until(() => boss.Current == "between blocks", 20);
            boss.ArtHold = true; yield return Wait(0.5f);

            // Drowned Peal (outgoing): the haul-back, then the first band
            d.Player.transform.position = new Vector3(0, 0, 7.5f);
            boss.ArtHold = false; boss.ForcedNext = "Peal";
            yield return Until(() => boss.Current.StartsWith("Drowned Peal"), 6);
            boss.ArtHold = true;
            yield return Wait(1.8f);
            yield return Both("body_pealloop_live");
            int pb = boss.PealBands; yield return Until(() => boss.PealBands > pb, 4);
            yield return Wait(0.02f);
            yield return Both("body_pealstrike_live");
            yield return Wait(3f);
            Done();
        }

        void Done()
        {
            Note($"shots {shots}");
            File.WriteAllText(Path.Combine(dir, "art-shots.txt"), log.ToString());
            Application.Quit(0);
        }
    }
}
