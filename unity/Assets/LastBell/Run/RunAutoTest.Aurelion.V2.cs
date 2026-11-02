using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Aurelion test harness (first built for ability set v2; the v1-v3 checks were retired 1 Nov, Chzy18: only the current
    // fight is tested). The v4 suite (RunAutoTest.Aurelion.V4.cs) uses it; -lastbellAbilityTest aurelion.<id> runs one section.
    // The harness holds Aurelion's schedule (TestHold) and runs ability bodies on his script, one tick per step, exactly
    // like a scheduled attack. Party members are real PlayerControllers (PlayerController.Spawn) driven by scripted
    // input, so hits, stamina, dash and knockback all go through the normal code.
    public sealed partial class RunAutoTest
    {
        static string AurelionAbilityFilter => AurelionArgs.Arg("-lastbellAbilityTest");
        static bool V2Wants(string id) => AurelionAbilityFilter == null || AurelionAbilityFilter.Equals(id, StringComparison.OrdinalIgnoreCase);

        // ================================================================ harness
        string v2Section = "";
        public void V2Check(string name, bool ok, string detail = "") => Check($"Aurelion v2 · {v2Section}: {name}", ok, detail);

        IEnumerator V2Section(string name, IEnumerator body)
        {
            v2Section = name; int before = fail;
            Note($"---- Aurelion v2 · {name} ----");
            while (true)
            {
                object cur;
                try { if (!body.MoveNext()) break; cur = body.Current; }
                catch (Exception ex) { V2Check("checks ran without an exception", false, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); break; }
                yield return cur;
            }
            Note($"Aurelion v2 · {name}: {(fail == before ? "all passed" : (fail - before) + " failed")}");
        }

        // Steers toward a target at a set speed (4.2 m/s = prototype walk, 6 = design walk), walks a constant heading, or
        // holds still. Dash() presses dash once on the next tick, in the steering direction. Never attacks.
        public sealed class V2Input : IInputSource
        {
            public Vector3? Target; public float Speed = 4.2f; public Vector3 Aim; public bool Sprint;
            public Vector3 Heading;   // used when Target is null: constant direction (zero = stand still)
            bool dash, jump;
            public void Dash() => dash = true;
            public void Jump() => jump = true;
            public void Stop() { Target = null; Heading = Vector3.zero; Sprint = false; }
            public void Go(Vector3 to, float speed = 4.2f) { Target = Sim.Flat(to); Speed = speed; }
            public void Walk(Vector3 dir, float speed = 4.2f) { Target = null; Heading = Sim.Flat(dir).normalized * Mathf.Clamp01(speed / PlayerController.WalkSpeed); }
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Sprint = Sprint };
                Vector3 move = Heading;
                if (Target.HasValue)
                {
                    var to = Target.Value - Sim.Flat(who.transform.position);
                    float step = Speed * Sim.Dt;
                    move = to.magnitude <= Mathf.Max(0.05f, step) ? Vector3.zero : to.normalized * Mathf.Clamp01(Speed / PlayerController.WalkSpeed);
                }
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(move, right), Vector3.Dot(move, fwd)), 1);
                if (dash) { r.Dash = true; dash = false; }
                if (jump) { r.Jump = true; jump = false; }
                return r;
            }
        }

        public sealed class V2HitRec { public PlayerController P; public Hazard H; public string Source; public int Damage, Tick; }
        public sealed class V2SpawnRec { public Hazard H; public string Source; public int Tick; }

        AurelionEncounter V2Boss;
        CombatWorld V2W => d.World;
        PlayerController V2Main => d.Player;
        readonly List<PlayerController> V2Party = new List<PlayerController>();   // V2Party[0] is the director's player
        readonly List<V2HitRec> V2Hits = new List<V2HitRec>();
        readonly List<V2SpawnRec> V2Spawns = new List<V2SpawnRec>();
        readonly Dictionary<PlayerController, V2Input> v2Inputs = new Dictionary<PlayerController, V2Input>();
        readonly V2Input v2MainInput = new V2Input();
        CombatWorld v2Hooked;

        public V2Input V2In(PlayerController p) => v2Inputs[p];
        public static void V2Place(PlayerController p, Vector3 pos) { pos.y = 0; p.transform.position = pos; }
        // n spots on a circle round `centre` (default the arena centre), starting at `startYaw` (0 = +z, south/entry)
        public static Vector3[] V2Ring(int n, float radius, Vector3? centre = null, float startYaw = 0) =>
            Enumerable.Range(0, n).Select(i => (centre ?? Vector3.zero) + Sim.Dir(startYaw + 360f * i / n) * radius).ToArray();

        // Fresh combat with a held Aurelion and `players` party members (the director's player first), everyone standing
        // with full HP and stamina at `at` (default: a 1.5 m ring 5 m south of the centre), scripted input idle. The boss
        // is placed at `bossAt` (default (0, 0, -1.5), his spawn) facing +z, in `phase` (1–3, without a transition).
        IEnumerator V2Setup(int players, Difficulty level = Difficulty.Medium, Vector3[] at = null, int phase = 1, Vector3? bossAt = null)
        {
            yield return V2EnsureCombat();
            if (V2Boss == null) yield break;
            V2Boss.TestHold = true;
            yield return Until(() => V2Boss == null || !V2Boss.TestBusy, 30, "previous v2 test body finished");
            V2W.ClearTransient();
            V2W.Difficulty = level;
            V2Boss.DebugSetPhase(phase);
            var bp = bossAt ?? new Vector3(0, 0, -1.5f); bp.y = 0;
            V2Boss.transform.SetPositionAndRotation(bp, Quaternion.identity);
            while (V2Party.Count > players)
            {
                var p = V2Party[V2Party.Count - 1]; V2Party.RemoveAt(V2Party.Count - 1);
                V2W.Players.Remove(p); v2Inputs.Remove(p); Destroy(p.gameObject);
            }
            if (V2Party.Count == 0) { V2Party.Add(V2Main); v2Inputs[V2Main] = v2MainInput; V2Main.Input = v2MainInput; }
            while (V2Party.Count < players)
            {
                var p = PlayerController.Spawn(V2W, "Chimesinger", new Vector3(0, 0, 5), 180);
                var s = new V2Input(); p.Input = s; v2Inputs[p] = s; p.AbilitiesEnabled = true; V2Party.Add(p);
            }
            for (int i = 0; i < V2Party.Count; i++)
            {
                var p = V2Party[i];
                p.DebugRevive();
                var pos = at != null && i < at.Length ? at[i] : new Vector3(0, 0, 5) + Sim.Dir(90 + 360f * i / Mathf.Max(1, players)) * (players > 1 ? 1.5f : 0);
                p.PrepareForEncounter(new Vector3(pos.x, 0, pos.z), 180);
                p.DebugSetHp(PlayerController.MaxHp);
                p.AbilitiesEnabled = true;   // dash and jump need it; V2Input never presses an ability
                V2In(p).Stop(); V2In(p).Aim = bp;
            }
            V2Hits.Clear(); V2Spawns.Clear();
            yield return V2Ticks(2);
        }

        IEnumerator V2EnsureCombat()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // another encounter waiting on Ready (an earlier section): back to the menu first
                if (d.State == RunState.Ready && !(d.Mode == RunMode.Practice && RunDirector.Bosses[d.Encounter].Name.StartsWith("Aurelion"))) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
                // a party member went down but the fight goes on (someone is still standing): stand them back up
                if (d.State == RunState.Combat && d.Boss is AurelionEncounter rb && !rb.Dead && V2Main && V2Main.Down) V2Main.DebugRevive();
                if (d.State == RunState.Combat && d.Boss is AurelionEncounter b && !b.Dead && V2Main && !V2Main.Down) { V2Boss = b; V2Hook(); yield break; }
                if (d.State == RunState.Combat || d.State == RunState.Downed || d.State == RunState.Cleared) yield return Until(() => d.State == RunState.Results, 15, "Aurelion v2: results after a wipe/defeat");
                if (d.State == RunState.Results) { V2Party.Clear(); v2Inputs.Clear(); d.InputOverride = v2MainInput; d.RequestRetry(); }
                if (d.State == RunState.Menu)
                {
                    V2Party.Clear(); v2Inputs.Clear();
                    int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
                    d.InputOverride = v2MainInput;
                    d.StartPractice("Arcanist", RunDirector.AurelionPractice);
                    yield return Until(() => RunMaps.Current == spire && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Aurelion v2: Skyward Spire practice ready");
                    if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; }
                    d.InputOverride = v2MainInput;
                }
                if (d.Player) d.Player.Input = v2MainInput;
                yield return Until(() => d.State == RunState.Ready, 10, "Aurelion v2: ready");
                d.RequestReady();
                if (d.Boss is AurelionEncounter nb) nb.TestHold = true;   // before the script's first step
                yield return Until(() => d.State == RunState.Combat, 10, "Aurelion v2: combat");
                if (d.Boss is AurelionEncounter hb) hb.TestHold = true;
            }
            V2Boss = d.Boss as AurelionEncounter;
            V2Check("practice combat with Aurelion", V2Boss != null && d.State == RunState.Combat, $"state {d.State}");
            if (V2Boss != null) V2Hook();
        }

        void V2Hook()
        {
            if (v2Hooked == V2W) return;
            v2Hooked = V2W;
            V2W.PlayerHit += (p, h, dmg) => V2Hits.Add(new V2HitRec { P = p, H = h, Source = h.Source, Damage = dmg, Tick = V2W.Tick });
            V2W.Hazards.Spawned += h => V2Spawns.Add(new V2SpawnRec { H = h, Source = h.Source, Tick = V2W.Tick });
        }

        IEnumerator V2Teardown()
        {
            if (d.State == RunState.Combat || d.State == RunState.Ready || d.State == RunState.Results || d.State == RunState.Downed) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            V2Party.Clear(); v2Inputs.Clear(); V2Boss = null;
            d.InputOverride = bot; bot.Clear();
        }

        // ---- running ability bodies on the boss's script ----
        // Runs a body (e.g. V2Boss.Noon.Run(false)) as his next "attack" and waits for it to end.
        IEnumerator V2Fire(IEnumerable<int> body, float timeout = 30) { V2Begin(body); yield return V2WaitIdle(timeout); }
        // Starts a body and returns at once, so the test can steer players while it runs.
        void V2Begin(IEnumerable<int> body) => V2Boss.TestRun(body);
        IEnumerator V2WaitIdle(float timeout = 30) { yield return Until(() => V2Boss == null || !V2Boss.TestBusy, timeout, "Aurelion v2: ability finished"); }
        IEnumerator V2Ticks(int n)
        {
            if (V2W == null) yield break;
            var w = V2W; int end = w.Tick + n; float guard = 0;
            while (d.World == w && w.Tick < end && guard < n / 20f + 10) { guard += Time.deltaTime; yield return null; }
        }

        // ---- queries ----
        IEnumerable<V2HitRec> V2HitsOn(PlayerController p, string sourcePrefix = null) => V2Hits.Where(h => h.P == p && (sourcePrefix == null || h.Source.StartsWith(sourcePrefix)));
        bool V2WasHit(PlayerController p, string sourcePrefix = null) => V2HitsOn(p, sourcePrefix).Any();
        IEnumerable<V2SpawnRec> V2Spawned(string sourcePrefix) => V2Spawns.Where(s => s.Source.StartsWith(sourcePrefix));
        // no instance hit the same player twice (standing burns excluded)
        bool V2OneHitPerInstance() => V2Hits.Where(h => h.H.BurnPerSecond <= 0).GroupBy(h => (h.P, h.H.Id)).All(g => g.Count() == 1);
        // the ability a hazard belongs to: its Source starts with the ability's Name (AurelionAbility.Spawn's default)
        string V2AbilityOf(Hazard h) => V2Boss != null ? V2Boss.Abilities.Select(a => a.Name).FirstOrDefault(n => h.Source.StartsWith(n)) ?? h.Source : h.Source;

        // Kills Aurelion now (the same path as a killing blow); the practice ends and the next V2Setup restarts it.
        void V2KillBoss() { V2Boss.DebugDefeat(); }
    }
}
