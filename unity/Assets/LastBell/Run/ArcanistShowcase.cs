using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace LastBell.Run
{
    // Review captures for the Arcanist's spells and clips (docs/design/arcanist-fx-prompts.md). Launch with
    //   LastBell.exe -lastbellPractice aurelion -lastbellClass Arcanist -arcanistShowcase <out dir> [-arcanistOnly name,name]
    // Waits for practice combat, holds the boss still, then casts each spell with a scripted input on a fixed 30 fps clock
    // (Time.captureFramerate), saving every frame from the gameplay camera (g_###.png) and from a closer review camera
    // (c_###.png) into <out>/<sequence>/. Quits when done. Presentation only: no checks, nothing is changed in the kit.
    public sealed class ArcanistShowcase : MonoBehaviour
    {
        sealed class Bot : IInputSource
        {
            public Vector3 Aim, MoveWorld; public bool[] press = new bool[5], hold = new bool[5]; public bool dash;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Dash = dash, P0 = press[0], P1 = press[1], P2 = press[2], P3 = press[3], P4 = press[4],
                    H0 = hold[0] || press[0], H1 = hold[1] || press[1], H2 = hold[2], H3 = hold[3], H4 = hold[4] };
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward; var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(MoveWorld, right), Vector3.Dot(MoveWorld, fwd)), 1);
                for (int i = 0; i < 5; i++) press[i] = false; dash = false; return r;
            }
            public void Clear() { MoveWorld = Vector3.zero; for (int i = 0; i < 5; i++) press[i] = hold[i] = false; dash = false; }
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            if (Arg("-arcanistShowcase") == null) return;
            var go = new GameObject("Arcanist showcase"); DontDestroyOnLoad(go); go.AddComponent<ArcanistShowcase>();
        }

        readonly Bot bot = new Bot();
        string outDir, only; Camera close; RenderTexture rt; Texture2D grab, grabClose;
        PlayerController watch; string stateLog;   // when set, Record writes his HP / dash state for every frame
        Func<Vector3> closeTarget; Vector3 closeOffset;

        IEnumerator Start()
        {
            outDir = Arg("-arcanistShowcase"); only = Arg("-arcanistOnly"); Directory.CreateDirectory(outDir);
            RunDirector d = null; float wait = 0;
            while ((d = RunDirector.Instance) == null || d.State != RunState.Combat || d.Player == null || d.Boss == null)
            {
                wait += Time.unscaledDeltaTime; if (wait > 60) { Log("no practice combat after 60 s"); Application.Quit(2); yield break; }
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var p = d.Player; var k = p.Kit as ArcanistKit;
            if (k == null) { Log("player is not the Arcanist"); Application.Quit(3); yield break; }
            d.InputOverride = bot; p.Input = bot;
            close = new GameObject("Review camera").AddComponent<Camera>(); close.fieldOfView = 40; close.nearClipPlane = 0.1f; close.farClipPlane = 200;
            var main = Camera.main; if (main) { close.clearFlags = main.clearFlags; close.backgroundColor = main.backgroundColor; close.cullingMask = main.cullingMask; }
            rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; close.targetTexture = rt;
            grabClose = new Texture2D(960, 540, TextureFormat.RGB24, false);
            Log($"showcase on {RunMaps.CurrentName} vs {d.Boss.GetType().Name}");

            IEnumerator Reset()
            {
                bot.Clear(); d.Boss.Halt(); d.World.ClearTransient(); p.DebugSetHp(PlayerController.MaxHp); k.ResetAll();
                var b = d.Boss.transform.position; var cm = Camera.main;
                // stand between the camera and the boss, a little to the side, so the gameplay view shows both
                var back = cm ? -Sim.Flat(cm.transform.forward).normalized : Vector3.back; var side = Vector3.Cross(Vector3.up, -back);
                p.transform.position = d.World.Constrain(b + back * (4.5f + d.Boss.BodyRadius) - side * 2f, PlayerController.Radius);
                p.FaceNow(b); bot.Aim = b; yield return Frames(20);
            }
            Vector3 Boss() => d.Boss.transform.position;
            Vector3 Me() => p.transform.position;
            void FocusMe() { closeTarget = () => Me() + Vector3.up * 1.2f; closeOffset = new Vector3(2.6f, 1.6f, 3.4f); }
            void FocusMid() { closeTarget = () => Vector3.Lerp(Me(), Boss(), 0.45f) + Vector3.up * 1.0f; closeOffset = new Vector3(3.5f, 7.5f, 9.5f); }
            void FocusBoss() { closeTarget = () => Boss() + Vector3.up * 0.8f; closeOffset = new Vector3(4.5f, 8f, 8.5f); }

            Time.captureFramerate = 30;
            try
            {
                if (Want("missile_tap"))
                {
                    yield return Reset(); FocusMid(); var rec = StartCoroutine(Record("missile_tap", 36));
                    for (int i = 0; i < 3; i++) { bot.press[0] = true; yield return Frames(9); }
                    yield return rec;
                }
                if (Want("missile_charge"))
                {
                    yield return Reset(); FocusMe(); var rec = StartCoroutine(Record("missile_charge", 66));
                    bot.press[0] = true; bot.hold[0] = true; yield return Frames(33);
                    bot.hold[0] = false; FocusMid(); yield return rec;
                }
                if (Want("missile_cancel"))
                {
                    yield return Reset(); FocusMe(); var rec = StartCoroutine(Record("missile_cancel", 36));
                    bot.press[0] = true; bot.hold[0] = true; yield return Frames(26);
                    bot.dash = true; bot.MoveWorld = Vector3.right; yield return Frames(6); bot.Clear(); yield return rec;
                }
                if (Want("disintegrate"))
                {
                    yield return Reset(); FocusMid(); var rec = StartCoroutine(Record("disintegrate", 84));
                    bot.press[1] = true; bot.hold[1] = true; yield return Frames(84); bot.hold[1] = false; yield return rec;
                    yield return Frames(15);
                }
                if (Want("disintegrate_broken"))
                {
                    yield return Reset();
                    FocusMid(); var rec = StartCoroutine(Record("disintegrate_broken", 45));
                    bot.press[1] = true; bot.hold[1] = true; yield return Frames(30);
                    bot.dash = true; bot.MoveWorld = Vector3.left; yield return Frames(4); bot.hold[1] = false; bot.Clear(); yield return rec;
                }
                if (Want("rune_sigil"))
                {
                    yield return Reset(); k.DebugSetGlyphs(1); yield return Frames(12);
                    FocusBoss(); var rec = StartCoroutine(Record("rune_sigil", 54));
                    bot.press[2] = true; yield return rec;
                }
                if (Want("glyph_build"))
                {
                    // a full Disintegrate on the boss: a glyph carved at each step, then the three glyphs orbiting him
                    yield return Reset(); FocusMid(); var rec = StartCoroutine(Record("glyph_build", 96));
                    bot.press[1] = true; bot.hold[1] = true; yield return Frames(84); bot.hold[1] = false; yield return rec;
                    FocusMe(); closeOffset = new Vector3(2.2f, 1.4f, 3.0f); yield return Record("glyphs_idle", 45);
                }
                if (Want("rune_sigil_weak"))
                {
                    yield return Reset(); FocusBoss(); var rec = StartCoroutine(Record("rune_sigil_weak", 54));
                    bot.press[2] = true; yield return rec;
                }
                if (Want("arcane_comet"))
                {
                    yield return Reset(); k.DebugSetGlyphs(1); yield return Frames(12);
                    FocusBoss(); var rec = StartCoroutine(Record("arcane_comet", 165));   // through the whole aftermath
                    bot.press[3] = true; yield return rec;
                }
                if (Want("arcane_comet_weak"))
                {
                    yield return Reset(); FocusBoss(); var rec = StartCoroutine(Record("arcane_comet_weak", 150));   // through the impact
                    bot.press[3] = true; yield return rec;
                }
                if (Want("surge"))
                {
                    // 3 glyphs: the cast pulls them in, the release, then a 5-missile volley and a fast sigil while
                    // empowered, then the last second flickering and the fold at the end of its 6 s
                    yield return Reset(); k.DebugSetGlyphs(3); yield return Frames(15);
                    FocusMe(); closeOffset = new Vector3(3.2f, 2.0f, 4.2f); var rec = StartCoroutine(Record("surge", 40));
                    bot.press[4] = true; yield return rec;
                    FocusMid(); rec = StartCoroutine(Record("surge_active", 110));
                    bot.press[0] = true; bot.hold[0] = true; yield return Frames(30); bot.hold[0] = false; yield return Frames(20);
                    bot.press[2] = true; yield return Frames(26);
                    bot.press[0] = true; yield return Frames(10); bot.press[0] = true; yield return rec;   // two surged taps (two darts each)
                    yield return Frames(5); FocusMe(); closeOffset = new Vector3(3.2f, 2.0f, 4.2f); yield return Record("surge_end", 45);
                }
                if (Want("surge_cancel"))
                {
                    yield return Reset(); k.DebugSetGlyphs(3); yield return Frames(15);
                    FocusMe(); closeOffset = new Vector3(3.2f, 2.0f, 4.2f); var rec = StartCoroutine(Record("surge_cancel", 30));
                    bot.press[4] = true; yield return Frames(3); bot.dash = true; bot.MoveWorld = Vector3.right; yield return Frames(4); bot.Clear(); yield return rec;
                }
                if (Want("blink_blocked"))
                {
                    // a hit lands two ticks into the Blink: he is stunned mid-blink and shown where he stands
                    yield return Reset(); FocusMe(); closeOffset = new Vector3(3.5f, 2.2f, 4.5f);
                    var rec = StartCoroutine(Record("blink_blocked", 20));
                    yield return Frames(2);
                    d.World.Hazards.Spawn(new Hazard { Source = "Showcase strike", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = p.transform.position, Radius = 2.5f, ResolveAt = 6, Damage = 5 });
                    yield return Frames(2); bot.dash = true; bot.MoveWorld = Vector3.right; yield return Frames(6); bot.Clear(); yield return rec;
                    p.DebugSetHp(PlayerController.MaxHp);
                }
                if (Want("blink_hit"))
                {
                    // no invincibility on Blink: a 25-damage strike resolves 4 ticks into the dash and must land
                    yield return Reset(); FocusMe(); closeOffset = new Vector3(3.5f, 2.2f, 4.5f);
                    watch = p; stateLog = Path.Combine(outDir, "blink_hit_state.csv"); File.WriteAllText(stateLog, "frame,hp,dashing,hidden,stunned\n");
                    var rec = StartCoroutine(Record("blink_hit", 30));
                    yield return Frames(3);
                    d.World.Hazards.Spawn(new Hazard { Source = "Test strike during Blink", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = p.transform.position, Radius = 3f, ResolveAt = 8, Damage = 25 });
                    yield return Frames(2); bot.dash = true; bot.MoveWorld = Vector3.right; yield return Frames(8); bot.Clear(); yield return rec;
                    Log($"blink_hit: hp after {p.Hp} (started at {PlayerController.MaxHp}); the hit landed during the dash, no invincibility");
                    watch = null; p.DebugSetHp(PlayerController.MaxHp);
                }
                if (Want("blink"))
                {
                    yield return Reset(); FocusMe(); closeOffset = new Vector3(3.5f, 2.2f, 4.5f);
                    var rec = StartCoroutine(Record("blink", 24));
                    yield return Frames(3); bot.dash = true; bot.MoveWorld = Vector3.right; yield return Frames(10); bot.Clear(); yield return rec;
                }
                // astrolabe review (orb critique, Oct 28): only when named in -arcanistOnly. The close camera frames the
                // device itself; <name>_orb.csv logs where it sits relative to him every frame.
                Transform astro = null, headT = null;
                if (p.Presenter) foreach (var t in p.Presenter.Visual.GetComponentsInChildren<Transform>(true)) { if (t.name == "Astrolabe") astro = t; if (t.name == "head") headT = t; }
                void FocusOrb(Vector3 off) { closeTarget = () => astro ? astro.position : Me() + Vector3.up * 1.5f; closeOffset = off; }
                void OrbLog(string name) { orbLog = Path.Combine(outDir, name + "_orb.csv"); orbT = astro; orbHead = headT; orbP = p; File.WriteAllText(orbLog, "frame,right,up,forward,head_dist,yaw,tilt\n"); }
                if (Named("orb_idle"))
                {
                    yield return Reset(); FocusOrb(new Vector3(0.7f, 0.5f, 1.7f)); OrbLog("orb_idle"); yield return Record("orb_idle", 60);
                    FocusOrb(new Vector3(1.4f, 0.2f, -1.6f)); OrbLog("orb_front"); yield return Record("orb_front", 45);
                }
                if (Named("orb_walk"))
                {
                    yield return Reset(); FocusMe(); closeOffset = new Vector3(2.2f, 1.2f, 2.8f); OrbLog("orb_walk");
                    var rec = StartCoroutine(Record("orb_walk", 75));
                    bot.MoveWorld = Vector3.right; yield return Frames(30); bot.MoveWorld = Vector3.zero; yield return Frames(15);
                    bot.MoveWorld = Vector3.left; yield return Frames(20); bot.Clear(); yield return rec;
                }
                if (Named("orb_lens"))
                {
                    yield return Reset(); FocusOrb(new Vector3(1.6f, 0.7f, 1.4f)); OrbLog("orb_lens");
                    var rec = StartCoroutine(Record("orb_lens", 96));
                    bot.press[1] = true; bot.hold[1] = true; yield return Frames(84); bot.hold[1] = false; yield return rec;
                    yield return Frames(15);
                }
                if (Named("orb_surge"))
                {
                    yield return Reset(); k.DebugSetGlyphs(3); yield return Frames(15);
                    FocusOrb(new Vector3(1.3f, 0.8f, 2.0f)); OrbLog("orb_surge");
                    var rec = StartCoroutine(Record("orb_surge", 60));
                    bot.press[4] = true; yield return rec;
                    yield return Frames(200);   // let the empowered state run out before the next sequence
                }
                if (Named("orb_blink"))
                {
                    yield return Reset(); FocusMe(); closeOffset = new Vector3(2.6f, 1.4f, 3.2f); OrbLog("orb_blink");
                    var rec = StartCoroutine(Record("orb_blink", 30));
                    yield return Frames(3); bot.dash = true; bot.MoveWorld = Vector3.right; yield return Frames(10); bot.Clear(); yield return rec;
                }
                if (Named("orb_cream"))
                {
                    // on the cream stone outside the navy clock face (gameplay contrast on a light floor)
                    yield return Reset(); var cm2 = Camera.main; var bk = cm2 ? -Sim.Flat(cm2.transform.forward).normalized : Vector3.back;
                    p.transform.position = d.World.Constrain(Boss() + bk * 10.5f, PlayerController.Radius); p.FaceNow(Boss()); yield return Frames(15);
                    FocusMe(); closeOffset = new Vector3(2.2f, 1.2f, 2.8f); OrbLog("orb_cream");
                    var rec = StartCoroutine(Record("orb_cream", 60));
                    yield return Frames(20); bot.MoveWorld = Vector3.right; yield return Frames(25); bot.Clear(); yield return rec;
                }
                if (Named("orb_comet"))
                {
                    yield return Reset(); k.DebugSetGlyphs(1); yield return Frames(12);
                    FocusOrb(new Vector3(1.4f, 0.6f, -1.8f)); OrbLog("orb_comet");
                    var rec = StartCoroutine(Record("orb_comet", 36)); bot.press[3] = true; yield return rec;
                    yield return Frames(150);
                }
                orbLog = null;
            }
            finally { Time.captureFramerate = 0; }
            Log("showcase done");
            yield return null;
            Application.Quit(0);
        }

        bool Want(string name) => string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), name) >= 0;
        bool Named(string name) => !string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), name) >= 0;
        string orbLog; Transform orbT, orbHead; PlayerController orbP;
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        void Log(string s) { Debug.Log("[arcanist showcase] " + s); File.AppendAllText(Path.Combine(outDir, "showcase.log"), s + "\n"); }

        IEnumerator Record(string name, int frames)
        {
            var dir = Path.Combine(outDir, name); Directory.CreateDirectory(dir);
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForEndOfFrame();
                var main = Camera.main;
                if (grab == null || grab.width != Screen.width || grab.height != Screen.height) grab = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                grab.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); grab.Apply(false);
                File.WriteAllBytes(Path.Combine(dir, $"g_{i:000}.png"), grab.EncodeToPNG());
                if (watch && stateLog != null) File.AppendAllText(stateLog, $"{i},{watch.Hp},{watch.Dashing},{watch.Presenter.Hidden},{watch.Stunned}\n");
                if (orbLog != null && orbT && orbP)
                {
                    var tr = orbP.transform; var dd = orbT.position - tr.position;
                    float yaw = Vector3.SignedAngle(Sim.Flat(tr.forward), Sim.Flat(orbT.forward), Vector3.up), tilt = Vector3.Angle(orbT.up, Vector3.up);
                    float hd = orbHead ? Vector3.Distance(orbHead.position, orbT.position) : -1;
                    File.AppendAllText(orbLog, FormattableString.Invariant($"{i},{Vector3.Dot(dd, tr.right):0.000},{dd.y:0.000},{Vector3.Dot(dd, tr.forward):0.000},{hd:0.000},{yaw:0.0},{tilt:0.0}\n"));
                }
                if (close && closeTarget != null)
                {
                    var t = closeTarget(); var cam = main ? main.transform : null;
                    // offset is in the gameplay camera's frame (x right, y up, z toward the camera), so views stay consistent
                    var right = cam ? Sim.Flat(cam.right).normalized : Vector3.right; var back = cam ? -Sim.Flat(cam.forward).normalized : Vector3.back;
                    close.transform.position = t + right * closeOffset.x + Vector3.up * closeOffset.y + back * closeOffset.z;
                    close.transform.LookAt(t);
                    close.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    grabClose.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); grabClose.Apply(false); RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, $"c_{i:000}.png"), grabClose.EncodeToPNG());
                }
            }
        }
    }
}
