using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion ability set v2 checks (abilities-v2/ABILITIES.md), run after the v1 checks in AurelionChecks.
    //   -lastbellAurelionOnly                  Aurelion only (v1, then v2)
    //   -lastbellAurelionV2Only                with -lastbellAurelionOnly: skip the v1 section
    //   -lastbellAbilityTest aurelion.<id>     only that ability's checks (or aurelion.phase_plan)
    // The harness holds Aurelion's schedule (TestHold) and runs ability bodies on his script, one tick per step, exactly
    // like a scheduled attack. Party members are real PlayerControllers (PlayerController.Spawn) driven by scripted
    // input, so hits, stamina, dash and knockback all go through the normal code. Each ability's checks live in their
    // own file, RunAutoTest.Aurelion.V2.<AbilityName>.cs, called once from AurelionV2Checks below.
    public sealed partial class RunAutoTest
    {
        static bool AurelionV2Only => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionV2Only", StringComparison.OrdinalIgnoreCase));
        static string AurelionAbilityFilter => AurelionArgs.Arg("-lastbellAbilityTest");
        static bool V2Wants(string id) => AurelionAbilityFilter == null || AurelionAbilityFilter.Equals(id, StringComparison.OrdinalIgnoreCase);

        IEnumerator AurelionV2Checks()
        {
            Note("---------- Aurelion ability set v2 (proposal) ----------");
            AurelionArgs.SetOverride = "v2"; AurelionArgs.SoloOverride = null;
            if (V2Wants("aurelion.meridian_lances")) yield return V2Section("Meridian Lances", V2MeridianLances());
            if (V2Wants("aurelion.starfall_mirror")) yield return V2Section("Starfall Mirror", V2StarfallMirror());
            if (V2Wants("aurelion.noon_shadow")) yield return V2Section("Noon Shadow", V2NoonShadow());
            if (V2Wants("aurelion.orrery_closing")) yield return V2Section("Orrery Closing", V2OrreryClosing());
            if (V2Wants("aurelion.hour_hands")) yield return V2Section("Hour Hands", V2HourHands());
            if (V2Wants("aurelion.phase_plan")) yield return V2Section("phase plan", V2PhasePlan());
            yield return V2Teardown();
            AurelionArgs.SetOverride = null; AurelionArgs.SoloOverride = null;
        }

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
                var p = PlayerController.Spawn(V2W, "Vanguard", new Vector3(0, 0, 5), 180);
                var s = new V2Input(); p.Input = s; v2Inputs[p] = s; p.AbilitiesEnabled = false; V2Party.Add(p);
            }
            for (int i = 0; i < V2Party.Count; i++)
            {
                var p = V2Party[i];
                p.DebugRevive();
                var pos = at != null && i < at.Length ? at[i] : new Vector3(0, 0, 5) + Sim.Dir(90 + 360f * i / Mathf.Max(1, players)) * (players > 1 ? 1.5f : 0);
                p.PrepareForEncounter(new Vector3(pos.x, 0, pos.z), 180);
                p.DebugSetHp(PlayerController.MaxHp);
                p.AbilitiesEnabled = false;
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
                if (d.State == RunState.Combat && d.Boss is AurelionEncounter b && !b.Dead && V2Main && !V2Main.Down) { V2Boss = b; V2Hook(); yield break; }
                if (d.State == RunState.Combat || d.State == RunState.Downed || d.State == RunState.Cleared) yield return Until(() => d.State == RunState.Results, 15, "Aurelion v2: results after a wipe/defeat");
                if (d.State == RunState.Results) { V2Party.Clear(); v2Inputs.Clear(); d.InputOverride = v2MainInput; d.RequestRetry(); }
                if (d.State == RunState.Menu)
                {
                    V2Party.Clear(); v2Inputs.Clear();
                    int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
                    d.InputOverride = v2MainInput;
                    d.StartPractice("Vanguard", RunDirector.AurelionPractice);
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

        // ================================================================ phase plan and the switches (owned by the Abilities session)
        IEnumerator V2PhasePlan()
        {
            // the five abilities are registered and bound
            yield return V2Setup(1);
            if (V2Boss == null) yield break;
            V2Check("five abilities registered", V2Boss.Abilities.Count == 5 && AurelionArgs.Ids.All(id => V2Boss.Abilities.Any(a => a.Id == id)),
                string.Join(", ", V2Boss.Abilities.Select(a => a.Id)));

            // Misalignment pairing, Noon Shadow + Starfall Mirror (Starfall 0.7 s into Noon): both resolve on the same tick,
            // and two edge runners on the lit side (13 m out) clear both by running straight across at 4.2 m/s after a
            // 0.25 s reaction, with one dash each (ABILITIES.md §6 "Pairing: Noon + Starfall (edge runner)")
            yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(0, 0, 13f), new Vector3(3.5f, 0, 11f) }, 2);
            if (V2Boss == null) yield break;
            {
                V2Begin(V2Boss.NoonWithStarfall());
                yield return V2Ticks(ReactTicks);
                foreach (var p in V2Party) { V2In(p).Walk(Vector3.back, ProtoWalk); V2In(p).Dash(); }
                yield return V2WaitIdle(15);
                foreach (var p in V2Party) V2In(p).Stop();
                var noon = V2Spawned("Noon Shadow").FirstOrDefault(); var star = V2Spawned("Starfall Mirror").FirstOrDefault();
                bool together = noon != null && star != null && noon.Tick + noon.H.ResolveAt == star.Tick + star.H.ResolveAt;
                V2Check("Noon + Starfall: Starfall starts 0.7 s into Noon and both resolve on the same tick", together && star.Tick - noon.Tick == Sim.Ticks(AurelionEncounter.StarfallIntoNoon),
                    noon == null || star == null ? "missing spawn" : $"noon spawn {noon.Tick} resolve +{noon.H.ResolveAt}, star spawn {star.Tick} resolve +{star.H.ResolveAt}");
                V2Check("Noon + Starfall: edge runners at 4.2 m/s with one dash clear both", V2Party.All(p => !V2WasHit(p)),
                    string.Join(", ", V2Hits.Select(h => $"{h.P.name} by {h.Source}")) + $"; stamina {string.Join("/", V2Party.Select(p => p.Stamina.ToString("0")))}");
            }

            // the full loop, phase by phase: the order of blocks, the transitions, the punish and the overlap cap
            foreach (var level in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, level);
                if (V2Boss == null) yield break;
                var a = V2Boss; int log0 = a.BlockLog.Count, tr0 = a.Transitions;
                var mon = new V2PlanMonitor(); var watch = StartCoroutine(V2PlanWatch(a, mon));
                a.TestHold = false;
                yield return V2Survive(80, () => a.BlockLog.Skip(log0).Count(b => b.block == "punish") >= 1);
                V2Check($"{level}: Perfect Order runs Lances > Starfall > Noon > Orrery > punish",
                    V2Blocks(a, log0, 1).SequenceEqual(new[] { "Meridian Lances", "Starfall Mirror", "Noon Shadow", "Orrery Closing", "punish" }), string.Join(" > ", V2Blocks(a, log0, 1)));
                a.DebugSetHpFraction(a.NextPhaseFraction());
                yield return V2Survive(80, () => a.Phase == 2 && a.BlockLog.Skip(log0).Count(b => b.phase == 2 && b.block == "punish") >= 1);
                V2Check($"{level}: 65 % queues one transition into Misalignment, still form 1", a.Phase == 2 && a.Transitions == tr0 + 1 && !a.SecondForm && a.PhaseName == "Misalignment", $"phase {a.Phase}, transitions {a.Transitions - tr0}, second form {a.SecondForm}");
                V2Check($"{level}: Misalignment runs Lances > Noon + Starfall > Lances > Orrery + Horizon > punish",
                    V2Blocks(a, log0, 2).SequenceEqual(new[] { "Meridian Lances", "Noon Shadow + Starfall Mirror", "Meridian Lances", "Orrery Closing + Horizon", "punish" }), string.Join(" > ", V2Blocks(a, log0, 2)));
                a.DebugSetHpFraction(a.NextPhaseFraction());
                yield return V2Survive(80, () => a.Phase == 3 && a.BlockLog.Skip(log0).Count(b => b.phase == 3 && b.block == "punish") >= 1);
                V2Check($"{level}: 30 % plays the second form (Exposed Meridian), same HP bar, no heal", a.Phase == 3 && a.SecondForm && a.Transitions == tr0 + 2 && a.PhaseName == "Exposed Meridian" && a.Hp <= a.MaxHp * AurelionEncounter.V2P3At,
                    $"phase {a.Phase}, second form {a.SecondForm}, hp {a.Hp}/{a.MaxHp}");
                V2Check($"{level}: Exposed Meridian runs Cascading Lances > Noon and Afternoon > Grand Orrery > punish",
                    V2Blocks(a, log0, 3).SequenceEqual(new[] { "Cascading Lances", "Noon and Afternoon", "Grand Orrery", "punish" }), string.Join(" > ", V2Blocks(a, log0, 3)));
                StopCoroutine(watch);
                V2Check($"{level}: the punish lasts 3.5 s with no hazard live", mon.Punishes > 0 && mon.HazardsInPunish == 0 && mon.ShortestPunish >= Sim.Ticks(AurelionEncounter.PunishS) - 1,
                    $"{mon.Punishes} punishes, shortest {mon.ShortestPunish} ticks, hazard ticks inside {mon.HazardsInPunish}");
                V2Check($"{level}: transformations warn and deal nothing", mon.HazardsInShift == 0, mon.HazardsInShift + " ticks");
                V2Check($"{level}: never more than two damaging patterns live", mon.MaxPatterns <= 2, $"max {mon.MaxPatterns} ({mon.MaxPatternsAt})");
                V2Check($"{level}: Hour Hands {(level == Difficulty.Hard ? "runs inside the Horizon (Hard only)" : "never runs")}", level == Difficulty.Hard ? mon.HandsRuns >= 2 : mon.HandsRuns == 0, $"Hour Hands runs {mon.HandsRuns}");
                a.TestHold = true;
            }

            // a burst through 65 % and 30 % in one go plays one transition, straight into the second form
            yield return V2Setup(1);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss; int tr0 = a.Transitions;
                a.DebugSetHpFraction(0.9f);
                a.TestHold = false;
                yield return V2Survive(4, null);
                a.DebugSetHpFraction(0.2f);
                yield return V2Survive(40, () => a.Phase == 3 && a.Current != "phase shift");
                V2Check("a burst through 65 % and 30 % plays one transition into the second form", a.Phase == 3 && a.SecondForm && a.Transitions == tr0 + 1, $"phase {a.Phase}, transitions {a.Transitions - tr0}");
                a.TestHold = true;
            }

            // the practice hook: -lastbellAbility <id> (and F6) loops one ability on its own
            foreach (var id in AurelionArgs.Ids)
            {
                yield return V2Setup(1);
                if (V2Boss == null) yield break;
                var a = V2Boss; int log0 = a.BlockLog.Count;
                AurelionArgs.SoloOverride = id;
                a.TestHold = false;
                string want = id == "aurelion.meridian_lances" ? "Meridian Lances" : id == "aurelion.starfall_mirror" ? "Starfall Mirror" : id == "aurelion.noon_shadow" ? "Noon Shadow" : id == "aurelion.orrery_closing" ? "Orrery Closing" : "Hour Hands";
                yield return V2Survive(40, () => a.BlockLog.Skip(log0).Count(b => b.block == want) >= 2);
                var blocks = a.BlockLog.Skip(log0).Select(b => b.block).Where(b => b != "punish").ToList();
                V2Check($"practice hook {id} repeats only that ability", blocks.Count >= 2 && blocks.All(b => b == want), string.Join(", ", blocks));
                AurelionArgs.SoloOverride = null;
                a.TestHold = true;
            }

            // v1 stays reachable with -lastbellAbilitySet v1
            yield return V2Setup(1);
            if (V2Boss == null) yield break;
            AurelionArgs.SetOverride = "v1";
            V2KillBoss();
            yield return Until(() => d.State == RunState.Results, 10, "Aurelion v2: results before the v1 switch");
            d.RequestRetry();
            yield return Until(() => d.State == RunState.Ready, 10, "Aurelion v2: ready for v1");
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 10, "Aurelion v2: v1 combat");
            var v1 = d.Boss as AurelionEncounter;
            var v1Names = new HashSet<string> { "Meridian Cut", "Zenith Pins", "Processional Thrust", "Crown Reversal", "Meridian Alignment" };
            string seen = null; float t = 0;
            while (t < 20 && v1 != null && d.State == RunState.Combat && seen == null)
            {
                if (v1Names.Contains(v1.Current)) seen = v1.Current;
                foreach (var p in d.World.Players) if (!p.Down) p.DebugSetHp(PlayerController.MaxHp);
                t += Time.deltaTime; yield return null;
            }
            V2Check("-lastbellAbilitySet v1 keeps the original five attacks", seen != null && v1.Blocks == 0, $"first v1 attack: {seen ?? "none"}, v2 blocks {v1?.Blocks}");
            AurelionArgs.SetOverride = "v2";
            if (v1 != null && !v1.Dead) v1.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 10, "Aurelion v2: results after the v1 check");
            V2Party.Clear(); v2Inputs.Clear();
        }

        static List<string> V2Blocks(AurelionEncounter a, int from, int phase) => a.BlockLog.Skip(from).Where(b => b.phase == phase).Select(b => b.block).ToList();

        sealed class V2PlanMonitor
        {
            public int MaxPatterns, HazardsInPunish, HazardsInShift, Punishes, ShortestPunish = int.MaxValue, HandsRuns;
            public string MaxPatternsAt = "";
            public readonly HashSet<string> Seen = new HashSet<string>();
        }
        IEnumerator V2PlanWatch(AurelionEncounter a, V2PlanMonitor m)
        {
            int tick = -1, punishStart = -1; string last = null;
            while (a && d.World != null)
            {
                if (d.World.Tick == tick) { yield return null; continue; }
                tick = d.World.Tick;
                var live = d.World.Hazards.Live;
                var patterns = live.Select(V2AbilityOf).Distinct().ToList();
                if (patterns.Count > m.MaxPatterns) { m.MaxPatterns = patterns.Count; m.MaxPatternsAt = string.Join(" + ", patterns); }
                foreach (var h in live) m.Seen.Add(V2AbilityOf(h));
                if (a.Current == "open chest") { if (punishStart < 0) { punishStart = tick; m.Punishes++; } if (live.Count > 0) m.HazardsInPunish++; }
                else if (punishStart >= 0) { m.ShortestPunish = Mathf.Min(m.ShortestPunish, tick - punishStart); punishStart = -1; }
                if (a.Current == "phase shift" && live.Count > 0) m.HazardsInShift++;
                if (a.Current == "Hour Hands" && last != "Hour Hands") m.HandsRuns++;
                last = a.Current;
                yield return null;
            }
        }

        // lets the schedule run: the party stands still 6 m south of him and is topped up every frame (the plan test checks
        // the schedule, not survival; the ability tests check the dodging)
        IEnumerator V2Survive(float seconds, Func<bool> until)
        {
            float t = 0;
            while (t < seconds && d.State == RunState.Combat && (until == null || !Safe(until)))
            {
                foreach (var p in V2Party) if (p && !p.Down) p.DebugSetHp(PlayerController.MaxHp);
                t += Time.deltaTime; yield return null;
            }
            if (until != null && !Safe(until)) V2Check("schedule reached its goal", false, $"timed out after {seconds}s (boss '{V2Boss?.Current}', state {d.State})");
        }
    }
}
