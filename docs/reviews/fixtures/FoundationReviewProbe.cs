// Diagnostic-only reproduction. Copy into a disposable validation project's Assets, never production Run/.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LastBell.Run;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public sealed class FoundationReviewProbe : MonoBehaviour
{
    readonly List<string> lines = new List<string>();
    bool finished;
    int failures;
    string output;
    float deadline;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class Input : IInputSource
    {
        public Vector2 Move;
        public Vector3 Aim;
        public bool Dash;
        public PlayerInput Read(PlayerController p)
        {
            var result = new PlayerInput { Move = Move, Aim = Aim, Dash = Dash };
            Dash = false;
            return result;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfRequested()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-foundationReviewProbe") < 0) return;
        new GameObject("Foundation diagnostic reproduction only").AddComponent<FoundationReviewProbe>();
    }
    void Start()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-foundationProbeOut");
        output = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "foundation-probe.txt");
        deadline = Time.realtimeSinceStartup + 45;
        lines.Add("DIAGNOSTIC REPRODUCTION ONLY; passing means vulnerabilities reproduced, not gameplay acceptance.");
        StartCoroutine(Boot());
    }
    void Update()
    {
        if (!finished && Time.realtimeSinceStartup > deadline)
        {
            failures++;
            lines.Add("ERROR: 45-second diagnostic timeout.");
            Finish();
        }
    }
    IEnumerator Boot()
    {
        while (RunDirector.Instance == null) yield return null;
        var d = RunDirector.Instance;
        if (!d.StartPractice("Chimesinger", 0) || !d.RequestReady())
        {
            failures++; lines.Add("ERROR: failed to start practice."); Finish(); yield break;
        }
        while (d.State != RunState.Combat) yield return null;
        d.SetPaused(true); // automatic frame tick stops; manual World.Step still exercises actual game code
        try { Probe(d); }
        catch (Exception e) { failures++; lines.Add("ERROR: unexpected probe exception: " + e); }
        Finish();
    }
    static FieldInfo Field(object o, string name) => o.GetType().GetField(name, PrivateInstance);
    static void Set(object o, string name, object value)
    {
        var f = Field(o, name);
        if (f == null) throw new MissingFieldException(o.GetType().Name, name);
        f.SetValue(o, value);
    }
    static T Get<T>(object o, string name) => (T)Field(o, name).GetValue(o);
    void Result(string name, bool reproduced, string details)
    {
        lines.Add((reproduced ? "REPRODUCED: " : "NOT REPRODUCED: ") + name + " | " + details);
        if (!reproduced) failures++;
    }
    void Probe(RunDirector d)
    {
        var w = d.World; var p = d.Player; var k = (ChimesingerKit)p.Kit;
        d.Boss.Halt(); w.ClearTransient();
        p.PrepareForEncounter(new Vector3(0, 0, 5), 0);
        var input = new Input { Move = Vector2.right, Aim = d.Boss.transform.position, Dash = true };
        p.Input = input;
        var start = p.transform.position;
        w.Step(); input.Move = Vector2.zero;
        for (int n = 0; n < PlayerController.DashTicks; n++) w.Step();
        float travelled = Vector3.Distance(start, p.transform.position);
        Result("13-increment dash", Mathf.Abs(travelled - 3.25f) < 0.002f,
            "documented=3m/12ticks; actual=" + travelled.ToString("F4") + "m/13ticks; stamina=" + p.Stamina.ToString("F2"));

        k.ResetAll();
        Set(k, "bellLeft", 480);
        Set(k, "<Strength>k__BackingField", 2);
        Set(k, "<Echoes>k__BackingField", 1);
        var echo = Get<int[]>(k, "echo"); echo[0] = 20;
        Set(k, "aim", d.Boss.transform.position);
        var strike = typeof(ChimesingerKit).GetMethod("DoStrike", PrivateInstance);
        strike.Invoke(k, null); int afterOne = echo[0];
        strike.Invoke(k, null); int afterTwo = echo[0];
        Result("echo amplification past strength cap", k.Strength == 2 && afterOne == 30 && afterTwo == 45,
            "seeded capped strength=2/echo=20; refreshed=" + afterOne + "," + afterTwo + "; capped expectation=20,20");

        k.ResetAll();
        Set(k, "buffered", 1); Set(k, "bufferAge", 1);
        input.Move = Vector2.zero;
        p.ReceiveHit(new Hazard { Source = "diagnostic stun", Damage = 1, Stun = 15, Knockback = 0 });
        int startTick = w.Tick;
        for (int n = 0; n < 15; n++) w.Step();
        int age = Get<int>(k, "bufferAge");
        w.Step();
        string act = Field(k, "act").GetValue(k).ToString();
        Result("stale buffer activates after stun", age == 1 && act == "Strike",
            "elapsed=" + (w.Tick - startTick) + " ticks; bufferAgeAfter15=" + age + "; actionAfterRecovery=" + act + "; TTL=9");

        w.ClearTransient();
        w.Projectiles.Fire(new ProjectileSystem.Projectile
        {
            Pos = d.Boss.transform.position, Vel = Vector3.zero, Range = 20, Radius = 0.35f,
            Damage = d.Boss.Hp, Ability = "diagnostic lethal projectile", Color = Color.white
        });
        bool threw = false; string exception = "none";
        try { w.Projectiles.Step(); }
        catch (ArgumentOutOfRangeException e) { threw = true; exception = e.GetType().Name + ": " + e.Message; }
        Result("projectile killing blow collection invalidation", threw && d.Boss.Dead,
            "bossDead=" + d.Boss.Dead + "; remainingProjectiles=" + w.Projectiles.Count + "; exception=" + exception);
    }
    void Finish()
    {
        if (finished) return; finished = true;
        lines.Add("Reproduction checks not matching expected vulnerability: " + failures);
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(output));
            Directory.CreateDirectory(parent);
            File.WriteAllLines(output, lines);
        }
        catch (Exception e) { Debug.LogError("Probe report write failed: " + e); failures++; }
        foreach (var line in lines) Debug.Log("[foundation-probe] " + line);
#if UNITY_EDITOR
        EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
        Application.Quit(failures == 0 ? 0 : 1);
#endif
    }
}

#if UNITY_EDITOR
public static class FoundationReviewProbeLauncher
{
    // -executeMethod FoundationReviewProbeLauncher.Play -foundationReviewProbe -foundationProbeOut <absolute-file>
    // Do not pass -quit: the runtime coroutine quits after writing evidence.
    public static void Play()
    {
        EditorSceneManager.OpenScene("Assets/LastBell/Run/RunArena.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
