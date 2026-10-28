using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Watch mode: four bots play Crucivar in the Brass Foundry while you look on (practice only, never in a run):
    //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest
    //             -crucivarWatch [-lastbellAbilitySet v4] [-lastbellBossHp 0.4]
    // The boss runs its real schedule. Each bot is a real PlayerController on scripted input: it scores spots around
    // itself against every live hazard (where the jets will have turned to, Backdraft's pull, its own brand vs the
    // others, the pools), walks to the best one, dashes when a hit is about to land, and hits the boss in between.
    // Nobody goes down: a bot that would drop is healed and counted ("would have gone down"). The camera shows the
    // whole arena; Tab switches to following the first bot. When the boss dies the practice restarts.
    [DefaultExecutionOrder(1000)]   // after RunDirector.LateUpdate, so the overview camera wins
    public sealed class CrucivarWatch : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-crucivarWatch", StringComparison.OrdinalIgnoreCase));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var d = FindAnyObjectByType<RunDirector>();
            if (d && !d.GetComponent<CrucivarWatch>()) d.gameObject.AddComponent<CrucivarWatch>();
        }

        static readonly string[] Classes = { "Vanguard", "Vanguard", "Vanguard", "Vanguard" };
        static readonly string[] Names = { "Bot 1", "Bot 2", "Bot 3", "Bot 4" };

        sealed class Bot : IInputSource
        {
            public string Name; public PlayerController P; public Vector3 Goal; public bool Dash, Attack; public Vector3 Aim, Target; public float TargetRadius;
            public float NextPlan; public int Hits, Damage, Downs;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim };
                var to = Sim.Flat(Goal - who.transform.position);
                Vector3 move = to.magnitude < 0.15f ? Vector3.zero : to.normalized;
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(move, right), Vector3.Dot(move, fwd)), 1);
                r.Sprint = to.magnitude > 2.5f;
                if (Dash) { r.Dash = true; Dash = false; }
                if (Attack) { r.H0 = true; r.P0 = Time.frameCount % 20 == 0; }
                return r;
            }
        }

        RunDirector d;
        readonly List<Bot> bots = new List<Bot>();
        readonly Dictionary<int, float> lastYaw = new Dictionary<int, float>(), turnRate = new Dictionary<int, float>();
        CombatWorld hooked;
        bool follow; int fights; float fightStart;

        void Start()
        {
            d = GetComponent<RunDirector>();
            Application.runInBackground = true;
            StartCoroutine(Loop());
        }

        IEnumerator Loop()
        {
            yield return new WaitForSeconds(0.5f);
            while (true)
            {
                bots.Clear();
                var lead = new Bot { Name = Names[0] };
                d.InputOverride = lead;
                if (d.State == RunState.Results) d.RequestRetry();
                if (d.State == RunState.Menu) d.StartPractice(Classes[0], RunDirector.CrucivarPractice);
                while (d.State != RunState.Ready) yield return null;
                d.RequestReady();
                while (d.State != RunState.Combat) yield return null;
                var w = d.World;
                lead.P = d.Player; bots.Add(lead);
                for (int i = 1; i < 4; i++)
                {
                    var p = PlayerController.Spawn(w, Classes[i], new Vector3(-4.5f + 3f * i, 0, 6f), 180);
                    var b = new Bot { Name = Names[i], P = p };
                    p.Input = b; p.AbilitiesEnabled = true; bots.Add(b);
                }
                foreach (var b in bots) b.Goal = b.P.transform.position;
                if (hooked != w)
                {
                    hooked = w;
                    w.PlayerHit += (p, h, dmg) => { var b = bots.FirstOrDefault(x => x.P == p); if (b != null) { b.Hits++; b.Damage += dmg; } };
                }
                fights++; fightStart = Time.time;
                int logged = 0;
                while (d.State == RunState.Combat)
                {
                    foreach (var b in bots)
                    {
                        b.P.AbilitiesEnabled = true;
                        if (b.P.Hp < 40) { b.Downs++; b.P.DebugSetHp(PlayerController.MaxHp); }   // keep the show going, count it
                    }
                    // pace the fight so every phase shows at least two whole loops: 65 % after loop 2, 30 % after loop 4,
                    // the kill after loop 6 (the bots' damage alone would end it in half a minute)
                    if (d.Boss is CrucivarEncounter cb && cb.Rite != null && !cb.Dead)
                    {
                        int ph = cb.PhaseIndex;
                        // -crucivarWatchFrom p2: start just under 65 %, so Phase 2 comes after the first Forge Rite loop
                        if (fromP2 && ph == 1 && cb.Hp > cb.MaxHp * 0.645f) cb.DebugSetHpFraction(0.64f);
                        float floor = cb.PhaseLoops < 2 ? (ph == 1 ? (fromP2 ? 0f : 0.66f) : ph == 2 ? 0.31f : 0.02f) : 0f;
                        if (cb.Hp < cb.MaxHp * floor) cb.DebugSetHpFraction(floor);
                        int key = ph * 100 + cb.PhaseLoops;
                        if (key != logged)
                        {
                            logged = key;
                            Debug.Log($"[watch] fight {fights} loop {logged} · boss {cb.Hp}/{cb.MaxHp} · " + string.Join(" · ", bots.Select(b => $"{b.Name} {b.Hits} hits/{b.Damage} dmg/{b.Downs} downs")));
                        }
                    }
                    yield return null;
                }
                Debug.Log($"[watch] fight {fights} over after {Time.time - fightStart:0}s · " + string.Join(" · ", bots.Select(b => $"{b.Name} {b.Hits} hits/{b.Damage} dmg/{b.Downs} downs")));
                foreach (var b in bots.Skip(1)) if (b.P) { w.Players.Remove(b.P); Destroy(b.P.gameObject); }
                while (d.State != RunState.Results) yield return null;
                yield return new WaitForSeconds(1f);
            }
        }

        // ---------------- the bots' brains ----------------
        static bool fromP2 => CrucivarArgs.Arg("-crucivarWatchFrom") == "p2";

        void Update()
        {
            if (d == null || d.State != RunState.Combat || d.World == null || d.Boss == null) return;
            var w = d.World;
            foreach (var h in w.Hazards.Live)
            {
                if (lastYaw.TryGetValue(h.Id, out var y) && Time.deltaTime > 0) turnRate[h.Id] = Mathf.DeltaAngle(y, h.Yaw) / Time.deltaTime;
                lastYaw[h.Id] = h.Yaw;
            }
            if (UnityEngine.InputSystem.Keyboard.current?.tabKey.wasPressedThisFrame == true) follow = !follow;
            var boss = Sim.Flat(d.Boss.transform.position);
            var parts = w.Parts.Where(q => q.Alive).OrderBy(q => q.Name).ToList();
            foreach (var b in bots)
            {
                if (!b.P || b.P.Down) continue;
                var me = Sim.Flat(b.P.transform.position);
                // Foundry Uprising: each bot takes a casting by party order (extra bots double up on the first ones)
                var part = parts.Count > 0 ? parts[bots.IndexOf(b) % parts.Count] : null;
                b.Target = part != null ? Sim.Flat(part.Pos) : boss; b.TargetRadius = part != null ? part.BodyRadius : d.Boss.BodyRadius;
                b.Aim = b.Target;
                if (Time.time >= b.NextPlan)
                {
                    b.NextPlan = Time.time + 0.12f;   // a human-ish reaction cadence
                    b.Goal = Plan(b, me, boss, w);
                }
                b.Attack = Danger(b, me, boss, w, 0) < 1f && (me - b.Target).magnitude < b.TargetRadius + 3.2f;
                // dash out when a hit lands within 0.3 s and the goal is clear
                if (Danger(b, me, boss, w, 0.3f, imminentOnly: true) >= 50f && Danger(b, b.Goal, boss, w, 0) < 50f && (b.Goal - me).magnitude > 0.8f) b.Dash = true;
            }
        }

        Vector3 Plan(Bot b, Vector3 me, Vector3 boss, CombatWorld w)
        {
            Vector3 best = me; float bestCost = float.MaxValue;
            // carrying my own brand: drop it out by the wall like a player would, so the pools (which last the whole
            // phase) don't pave over the melee ring and stall the fight
            bool carrying = w.Hazards.Live.Any(h => h.Source == "Ember Brand" && h.Age < h.ResolveAt && (Sim.Flat(h.Origin) - me).magnitude < 0.25f);
            for (int ring = 0; ring <= 4; ring++)
                for (int k = 0; k < (ring == 0 ? 1 : 20); k++)
                {
                    var c = ring == 0 ? me : me + Quaternion.Euler(0, 18f * k, 0) * Vector3.forward * (1.3f * ring);
                    if (c.magnitude > CrucivarAbility.Rim - 0.8f) continue;
                    float travel = (c - me).magnitude;
                    float cost = Danger(b, c, boss, w, travel / 6f) + travel * 1.2f;
                    float r = (c - boss).magnitude;
                    if (carrying) cost += Mathf.Max(0f, CrucivarAbility.Rim - 3f - r) * 4f;
                    else
                    {
                        float rt = (c - b.Target).magnitude;   // stay in melee of the target (the boss or my casting), out of its body
                        cost += rt < b.TargetRadius + 1f ? 40f : Mathf.Abs(rt - (b.TargetRadius + 2.2f)) * 1.5f;
                        if (r < d.Boss.BodyRadius + 1f) cost += 40f;
                    }
                    foreach (var o in bots) if (o != b && o.P && !o.P.Down) { float s = (Sim.Flat(o.P.transform.position) - c).magnitude; if (s < 3.4f) cost += (3.4f - s) * 8f; }
                    if (cost < bestCost) { bestCost = cost; best = c; }
                }
            return best;
        }

        // how bad standing at c would be, looking `ahead` seconds forward (rotating jets advanced by their turn rate)
        float Danger(Bot b, Vector3 c, Vector3 boss, CombatWorld w, float ahead, bool imminentOnly = false)
        {
            float cost = 0;
            var uprising = (d.Boss as CrucivarEncounter)?.Uprising;
            foreach (var h in w.Hazards.Live)
            {
                float toLand = (h.ResolveAt - h.Age) * Sim.Dt;
                float live = h.ActiveTicks > 1 ? (h.ResolveAt + h.ActiveTicks - h.Age) * Sim.Dt : 0;
                if (h.BurnPerSecond > 0) { if (!imminentOnly && Inside(h, c, 0.35f)) cost += 70f; continue; }
                if (uprising != null && uprising.Velocity.TryGetValue(h, out var vel))
                {   // a marching bullet: test where it will be over the next moments
                    for (float t = 0; t <= Mathf.Max(ahead, 0.15f) + 0.35f; t += 0.1f)
                        if ((Sim.Flat(h.Origin + vel * t) - c).magnitude < h.Radius + 0.75f) { cost += 100f; break; }
                    continue;
                }
                if (h.Damage <= 0 || (toLand < -0.05f && live <= 0)) continue;
                if (imminentOnly && toLand > ahead && live <= 0) continue;
                if (h.Source == "Ember Brand")
                {
                    bool mine = (Sim.Flat(h.Origin) - Sim.Flat(b.P.transform.position)).magnitude < 0.25f;
                    if (mine)
                    {   // carry my brand away from everyone else
                        if (imminentOnly) continue;
                        foreach (var o in bots) if (o != b && o.P && !o.P.Down && (Sim.Flat(o.P.transform.position) - c).magnitude < h.Radius + 0.9f) cost += 60f;
                        continue;
                    }
                }
                var test = c;
                if (h.Source == "Backdraft" && toLand > 0) test = c + Sim.Flat(boss - c).normalized * Mathf.Min(2.3f, (boss - c).magnitude);   // the pull
                float yaw0 = h.Yaw;
                if (turnRate.TryGetValue(h.Id, out var rate) && live > 0)
                {
                    bool hit = false;
                    for (float t = 0; t <= Mathf.Max(ahead, 0) + 0.6f && !hit; t += 0.2f) { h.Yaw = yaw0 + rate * t; hit = Inside(h, test, 0.45f); }
                    h.Yaw = yaw0;
                    if (hit) cost += 100f;
                    continue;
                }
                if (Inside(h, test, 0.45f)) cost += toLand < 1.2f || live > 0 ? 100f : 30f + 70f * Mathf.Clamp01(1f - (toLand - 1.2f) / 2f);   // sooner = worse, so they stand where the later hit lands
            }
            return cost;
        }

        // Contains with a safety margin: test a few points around c
        static bool Inside(Hazard h, Vector3 c, float pad)
        {
            if (h.Contains(c)) return true;
            for (int i = 0; i < 6; i++) if (h.Contains(c + Quaternion.Euler(0, 60f * i, 0) * Vector3.forward * pad)) return true;
            return false;
        }

        // ---------------- the overview camera and the scoreboard ----------------
        void LateUpdate()
        {
            if (follow || d == null || d.State != RunState.Combat) return;
            var cam = Camera.main; if (!cam) return;
            var target = new Vector3(0, 23f, 15.5f);
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1 - Mathf.Exp(-8f * Time.deltaTime));
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, 1.5f) - target);
            cam.fieldOfView = 50;
        }

        GUIStyle style;
        void OnGUI()
        {
            if (d == null || bots.Count == 0) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = new Color(1f, 0.95f, 0.85f) } };
            var boss = d.Boss as CrucivarEncounter;
            var lines = new List<string>
            {
                $"WATCH MODE · set {CrucivarArgs.Set} · fight {fights} · {Time.time - fightStart:0}s · Tab: follow/overview",
                boss ? $"Crucivar {boss.Hp}/{boss.MaxHp} · {boss.Current}" + (boss.Rite != null ? $" · loop {boss.Rite.Loops}" : "") : "",
            };
            foreach (var b in bots) lines.Add($"{b.Name}: {b.Hits} hits, {b.Damage} damage, would have gone down {b.Downs}x");
            GUI.Box(new Rect(10, 10, 560, 26 + 22 * lines.Count), GUIContent.none);
            for (int i = 0; i < lines.Count; i++) GUI.Label(new Rect(20, 16 + 22 * i, 540, 22), lines[i], style);
        }
    }
}
