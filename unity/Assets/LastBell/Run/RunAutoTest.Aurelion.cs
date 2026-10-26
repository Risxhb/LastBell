using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion (Skyward Spire area boss, practice slot 4) acceptance checks. Two parts:
    //  1. Route proofs on the real hazard geometry (Hazard.Contains, player margin, boss body, floor bound), without
    //     dashing or jumping: the worst-placed player must be able to walk clear within the warning left after it locks,
    //     minus a 0.25 s reaction margin, at the 4.2 m/s prototype walk and the 6 m/s design walk. Pins -> Cut (phase 2)
    //     is simulated tick by tick, because two patterns overlap there and need one common route.
    //  2. Live practice fights driven by a dodging bot that walks at 4.2 m/s, reacts 0.25 s after a warning locks and
    //     never dashes, plus the schedule invariants (phase queueing, Reversal trigger, ring order, overlap cap).
    // `-lastbellAurelionOnly` runs only this section.
    public sealed partial class RunAutoTest
    {
        static bool AurelionOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionOnly", StringComparison.OrdinalIgnoreCase));

        const float ReactS = 0.25f, ProtoWalk = 4.2f, DesignWalk = 6f;
        const int ReactTicks = 15;

        IEnumerator AurelionChecks()
        {
            if (AurelionGallery) { yield return AurelionGalleryShots(); yield break; }
            if (AurelionDemo) { yield return AurelionDemoFight(); yield break; }
            if (AurelionV4Live) { yield return AurelionV4LiveFight(); yield break; }
            if (AurelionV4Only) { yield return AurelionV4Checks(); yield return V2Teardown(); yield break; }
            if (AurelionV3Only) { yield return AurelionV3Checks(); yield return V2Teardown(); yield break; }
            if (AurelionV2Only) { yield return AurelionV2Checks(); yield break; }
            AurelionArgs.SetOverride = "v1";   // this section checks the original five attacks; ability set v2 follows (RunAutoTest.Aurelion.V2.cs)
            Note("---------- Aurelion, the Meridian Regent (practice, ability set v1) ----------");
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }   // the earlier practice section ends on Ready
            // his practice is fought in his own arena: from another map the button loads Skyward Spire first
            int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
            Check("Aurelion practice accepted from the menu", d.StartPractice("Vanguard", RunDirector.AurelionPractice));
            yield return Until(() => RunMaps.Current == spire && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Skyward Spire loaded for Aurelion");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // the new scene built the player before the bot was attached
            Check("Aurelion practice loads the Skyward Spire arena", RunMaps.Current == spire && HazardSystem.ArenaCount > 0, RunMaps.CurrentName);
            Check("Aurelion practice: Ready names the boss", d.State == RunState.Ready && d.Mode == RunMode.Practice && RunDirector.Bosses[d.Encounter].Name.StartsWith("Aurelion"));
            AurelionRouteProofs();   // on this arena's floor bound
            yield return null;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Aurelion combat");
            var a = d.Boss as AurelionEncounter;
            Check("Aurelion practice starts the Aurelion encounter", a != null && d.Mode == RunMode.Practice && a.PhaseName == "Perfect Order");
            Check("Aurelion uses its rigged model", a != null && a.Presenter != null && a.Presenter.HasModel);
            if (a == null) { d.ReturnToMenu(); yield break; }

            var taken = new Dictionary<string, int>();
            d.World.PlayerHit += (p, h, dmg) =>
            {
                taken.TryGetValue(h.Source, out int t); taken[h.Source] = t + dmg;
                var v = Sim.Flat(p.transform.position - d.Boss.transform.position);
                Note($"hit by {h.Source} for {dmg}: locked {(h.Age - h.LockAt) / 60f:0.00} s before resolving, player {v.magnitude:0.0} m at {Vector3.SignedAngle(d.Boss.transform.forward, v, Vector3.up):0}° from his facing, boss doing '{d.Boss.Current}'");
                { int i = 0; foreach (var line in trace) if (i++ % 8 == 0) Note("   " + line); }
            };
            int phaseEvents = 0; a.PhaseChanged += (b, n) => phaseEvents++;
            var mon = new AurelionMonitor(); var monitor = StartCoroutine(Monitor(a, mon));
            StartCoroutine(AlignmentShots());

            // ---- phase 1: melee posture on the forward flank, then ranged posture (edge kiting draws the Thrust)
            yield return Shot("aurelion_phase1");
            yield return AurelionDodge(26f, ProtoWalk, 70f, 3.0f, taken);
            yield return AurelionDodge(14f, ProtoWalk, 20f, 9.5f, taken);
            Check("Aurelion P1: taught Cut, Pins and Alignment", a.CutsStarted > 0 && a.PinVolleys > 0 && a.AlignmentsStarted > 0, $"cuts {a.CutsStarted}, pins {a.PinVolleys}, alignments {a.AlignmentsStarted}");
            Check("Aurelion P1: kiting at range draws Processional Thrust", a.ThrustsStarted > 0, "thrusts " + a.ThrustsStarted);
            Check("Aurelion P1: no Crown Reversal before Misalignment", a.ReversalsStarted == 0);
            Check("Aurelion P1: Pins never lead into Cut", a.PinsCutCombos == 0);

            // ---- phase 2
            a.DebugSetHpFraction(a.NextPhaseFraction());
            yield return AurelionDodge(12f, ProtoWalk, 70f, 3.0f, taken, () => a.PhaseName == "Misalignment");
            Check("Aurelion P2 queued and entered (one transition)", a.Phase == 2 && phaseEvents == 1, $"phase {a.Phase}, events {phaseEvents}");
            yield return Shot("aurelion_phase2");
            yield return AurelionDodge(24f, ProtoWalk, 70f, 3.0f, taken, () => a.PinsCutCombos > 0);
            Check("Aurelion P2: Pins lead into Cut", a.PinsCutCombos > 0, "combos " + a.PinsCutCombos);
            Check("Aurelion P2: whenever both are live, the pins resolve before the cut", mon.PinsAfterCut == 0 && mon.BothLive > 0, $"order violations {mon.PinsAfterCut}, ticks both live {mon.BothLive}");

            // rear camping: stand behind him; the Reversal must come only after more than 3 s there
            int rev0 = a.ReversalsStarted;
            yield return AurelionDodge(22f, ProtoWalk, 180f, 2.6f, taken, () => a.ReversalsStarted > rev0);
            Check("Aurelion P2: rear camping triggers Crown Reversal", a.ReversalsStarted > rev0, $"reversals {a.ReversalsStarted - rev0}");
            Check("Aurelion P2: Reversal only after > 3 s behind", mon.EarlyReversal == 0, $"{mon.EarlyReversal} early (min behind {mon.MinBehindAtReversal / 60f:0.00} s)");

            // ---- phase 3
            a.DebugSetHpFraction(a.NextPhaseFraction());
            yield return AurelionDodge(12f, ProtoWalk, 70f, 3.0f, taken, () => a.PhaseName == "Exposed Meridian");
            Check("Aurelion P3 entered", a.Phase == 3 && phaseEvents == 2, $"phase {a.Phase}, events {phaseEvents}");
            yield return Shot("aurelion_phase3");
            int al0 = a.AlignmentsStarted;
            yield return AurelionDodge(34f, ProtoWalk, 70f, 3.0f, taken, () => a.AlignThenThrust > 0);
            Check("Aurelion P3: Alignment, then after its full 3 s recovery a Thrust", a.AlignThenThrust > 0 && a.AlignThenThrustGap >= AurelionEncounter.AlignRec, $"{a.AlignThenThrust} follow-ups, gap {a.AlignThenThrustGap} ticks");

            StopCoroutine(monitor);
            Check("Aurelion: ring II only warns after ring I resolved", !a.SecondRingSeenBeforeFirstResolved && mon.RingsTogether == 0);
            Check("Aurelion: never more than two damaging patterns live", mon.MaxPatterns <= 2 && a.MaxLiveHazards <= 2, $"max patterns {mon.MaxPatterns}, max hazards {a.MaxLiveHazards}");
            Check("Aurelion: phase transformations deal and warn nothing", mon.HazardsDuringShift == 0, mon.HazardsDuringShift + " ticks");
            Check("Aurelion: hits use the short 0.15 s stun", mon.WrongStun == 0);
            var wantClips = new[] { "Idle", "Walk", "MeridianCut", "ZenithPins", "ProcessionalThrust", "CrownReversal", "MeridianAlignment", "OpenChest", "PhaseShift" };
            Check("Aurelion plays every fight clip", wantClips.All(mon.Clips.Contains), "missing: " + string.Join(", ", wantClips.Where(c => !mon.Clips.Contains(c))));
            int total = taken.Values.Sum();
            Note("dodge bot (4.2 m/s walk, 0.25 s reaction, dashes only when walking can't make it) damage taken: " + (total == 0 ? "none" : string.Join(", ", taken.Select(kv => $"{kv.Key} {kv.Value}"))));
            Note("dodge bot dashes by attack: " + string.Join(", ", botDashes.Select(kv => $"{kv.Key} {kv.Value}")));
            Check("Aurelion: every pattern avoided at 4.2 m/s without immunity (dodge bot takes no damage)", total == 0, total + " damage");
            Check("Aurelion: the dash checks needed dashes (Cut and Pins)", botDashes.ContainsKey("Meridian Cut") && botDashes.ContainsKey("Zenith Pins"), string.Join(", ", botDashes.Keys));
            Check("Aurelion: the dodge bot is still standing", d.State == RunState.Combat && !d.Player.Down);

            // a withdrawn hazard (e.g. a pin whose recipient went down) never resolves
            var probe = d.World.Hazards.Spawn(new Hazard { Source = "Zenith Pins", Shape = HazardShape.Circle, Radius = 1.3f, LockAt = 0, ResolveAt = 20, Damage = 12, Origin = d.Player.transform.position });
            d.World.Hazards.Cancel(probe);
            yield return Wait(0.6f);
            Check("withdrawn hazard leaves without resolving or hitting", !d.World.Hazards.Live.Contains(probe) && probe.Hit.Count == 0);

            // burst through both thresholds from Perfect Order: one transition, straight to Exposed Meridian
            d.SetPaused(true); d.RequestRetry(); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Aurelion retry combat");
            a = d.Boss as AurelionEncounter; phaseEvents = 0; a.PhaseChanged += (b, n) => phaseEvents++;
            a.DebugSetHpFraction(0.2f);
            yield return Until(() => a.Phase == 3, 10, "Aurelion burst transition");
            yield return Wait(2f);
            Check("Aurelion: a burst through 65 % and 30 % plays one transition", phaseEvents == 1 && a.PhaseName == "Exposed Meridian", $"events {phaseEvents}, {a.PhaseName}");

            // Processional Thrust hits as he travels: standing still you are hit after the warning ends, when his front
            // arrives; stepping sideways once he is already moving still avoids it
            d.SetPaused(true); d.RequestRetry(); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Aurelion thrust combat");
            a = d.Boss as AurelionEncounter;
            int thrustHitAge = -1;
            d.World.PlayerHit += (p, h, dmg) => { if (h.Source == "Processional Thrust" && thrustHitAge < 0) thrustHitAge = h.Age; };
            yield return ThrustStand(a, () => thrustHitAge >= 0, false);
            Check("Thrust: standing in the lane, the blade hits during his travel (not at the warning's end)", thrustHitAge > AurelionEncounter.ThrustWarn && thrustHitAge <= AurelionEncounter.ThrustWarn + AurelionEncounter.ThrustTravel, $"hit at lane age {thrustHitAge} (warning ends {AurelionEncounter.ThrustWarn}, travel ends {AurelionEncounter.ThrustWarn + AurelionEncounter.ThrustTravel})");
            int hits0 = a.ThrustTravelHits, thrusts0 = a.ThrustsStarted;
            yield return Wait(2.5f);
            a.DebugReadyThrust();
            yield return ThrustStand(a, () => a.ThrustsStarted > thrusts0 && !d.World.Hazards.Live.Any(h => h.Source == "Processional Thrust"), true);
            Check("Thrust: a sidestep after he starts moving still avoids it", a.ThrustsStarted > thrusts0 && a.ThrustTravelHits == hits0, $"thrusts {a.ThrustsStarted - thrusts0}, hits {a.ThrustTravelHits - hits0}");

            // sustained damage for HP sizing (packet: HP = measured sustained DPS x 150 s)
            d.SetPaused(true); d.RequestRetry(); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Aurelion DPS combat");
            int dealt0 = d.World.DamageDealt; float t0 = Time.time;
            yield return Fight(20f, VanguardRotation, 1.0f);
            float dps = (d.World.DamageDealt - dealt0) / Mathf.Max(1, Time.time - t0);
            Note($"Vanguard sustained DPS on Aurelion (moving, dodging): {dps:0.0} -> 150 s solo target = {dps * 150:0} HP (current MaxHp {d.Boss.MaxHp})");

            yield return KillDuringWindup("Aurelion");
            yield return Until(() => d.State == RunState.Results, 6, "Aurelion practice results");
            Check("Aurelion practice clear is a practice result", d.Mode == RunMode.Practice && d.Victory);
            d.ReturnToMenu();
            yield return null;
            AurelionArgs.SetOverride = null;
            yield return AurelionV2Checks();
        }

        // evidence: the orrery rings late in each Alignment warning (first Alignment of the fight only)
        IEnumerator AlignmentShots()
        {
            foreach (var (src, age) in new[] { ("Meridian Alignment", 100), ("Meridian Alignment II", 85) })
            {
                float t = 0;
                while (t < 60 && d.World != null && !d.World.Hazards.Live.Any(h => h.Source == src && h.Age >= age)) { t += Time.deltaTime; yield return null; }
                if (d.World == null || t >= 60) yield break;
                yield return Shot(src == "Meridian Alignment" ? "aurelion_alignment_ring1" : "aurelion_alignment_ring2");
            }
        }

        // ---------------------------------------------------------------- live invariants
        sealed class AurelionMonitor
        {
            public int MaxPatterns, PinsAfterCut, BothLive, RingsTogether, HazardsDuringShift, WrongStun;
            public int EarlyReversal, MinBehindAtReversal = int.MaxValue;
            public readonly HashSet<string> Clips = new HashSet<string>();
        }
        IEnumerator Monitor(AurelionEncounter a, AurelionMonitor m)
        {
            var seen = new HashSet<Hazard>(); int tick = -1;
            while (a && d.World != null)
            {
                if (d.World.Tick == tick) { yield return null; continue; }
                tick = d.World.Tick;
                var live = d.World.Hazards.Live;
                m.MaxPatterns = Mathf.Max(m.MaxPatterns, live.Select(h => h.Source.Replace(" II", "")).Distinct().Count());
                var pin = live.FirstOrDefault(h => h.Source == "Zenith Pins"); var cut = live.FirstOrDefault(h => h.Source == "Meridian Cut");
                if (pin != null && cut != null) { m.BothLive++; if (pin.ResolveAt - pin.Age >= cut.ResolveAt - cut.Age) m.PinsAfterCut++; }
                if (live.Any(h => h.Source == "Meridian Alignment") && live.Any(h => h.Source == "Meridian Alignment II")) m.RingsTogether++;
                if (a.Current == "phase shift" && live.Count > 0) m.HazardsDuringShift++;
                if (a.Presenter != null && !string.IsNullOrEmpty(a.Presenter.CurrentClip)) m.Clips.Add(a.Presenter.CurrentClip);
                foreach (var h in live)
                {
                    if (!seen.Add(h)) continue;
                    if (h.Stun != AurelionEncounter.HitStun) m.WrongStun++;
                    if (h.Source == "Crown Reversal")
                    {
                        // the boss records how long the camper had been behind when it chose the Reversal
                        m.MinBehindAtReversal = Mathf.Min(m.MinBehindAtReversal, a.LastReversalBehindTicks);
                        if (a.LastReversalBehindTicks <= AurelionEncounter.RevCamp) m.EarlyReversal++;
                    }
                }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- dodging bot
        // Holds a posture (angle from the boss's facing, distance from its centre). When it stands in a hazard that
        // locked at least 0.25 s ago it heads for the nearest spot outside every live hazard: walking, plus one dash
        // only when walking cannot get there before that hazard resolves. Never sprints, never jumps.
        // Once `until` holds it keeps dodging for `tail` seconds, so the attack that satisfied it is still avoided.
        readonly Dictionary<string, int> botDashes = new Dictionary<string, int>();
        IEnumerator AurelionDodge(float seconds, float speed, float angle, float dist, Dictionary<string, int> taken, Func<bool> until = null, float tail = 3.5f)
        {
            float t = 0, scale = speed / PlayerController.WalkSpeed, stopAt = seconds;
            while (t < stopAt && d.State == RunState.Combat)
            {
                if (until != null && stopAt == seconds && Safe(until)) stopAt = t + tail;
                var p = d.Player; var b = d.Boss;
                if (p && b)
                {
                    var pos = p.transform.position; var live = d.World.Hazards.Live;
                    bot.Aim = b.transform.position; bot.Sprint = false;
                    var threat = live.Where(h => h.Age >= Mathf.Max(h.LockAt, 0) + ReactTicks && h.Contains(pos)).OrderBy(h => h.ResolveAt - h.Age).FirstOrDefault();
                    Vector3 move;
                    if (threat != null)
                    {
                        move = NearestClear(pos, b, live, out float need);
                        float left = (threat.ResolveAt - threat.Age) / (float)Sim.Hz;
                        if (move != Vector3.zero && need / speed > left - 0.05f && !p.Dashing && p.Stamina >= PlayerController.DashCost)
                        {
                            bot.dash = true;
                            botDashes.TryGetValue(threat.Source, out int n); botDashes[threat.Source] = n + 1;
                        }
                    }
                    else
                    {
                        var want = b.transform.position + Sim.Dir(b.transform.eulerAngles.y + angle) * dist;
                        var flat = new Vector2(want.x, want.z); if (flat.magnitude > 10.6f) { flat = flat.normalized * 10.6f; want = new Vector3(flat.x, 0, flat.y); }
                        var v = Sim.Flat(want - pos);
                        move = v.magnitude < 0.3f ? Vector3.zero : v.normalized;
                        var next = pos + move * 0.35f;
                        if (move != Vector3.zero && live.Any(h => h.Contains(next)) && !live.Any(h => h.Contains(pos))) move = Vector3.zero;
                    }
                    bot.MoveWorld = move * scale;
                    trace.Enqueue($"{d.World.Tick}: at {Sim.Flat(pos - b.transform.position).magnitude:0.00} m {(threat != null ? "ESCAPE " + threat.Source : "posture")} move ({move.x:0.0},{move.z:0.0}) stamina {p.Stamina:0} boss '{b.Current}'"); while (trace.Count > 160) trace.Dequeue();
                }
                t += Time.deltaTime; yield return null;
            }
            bot.Clear();
        }
        readonly Queue<string> trace = new Queue<string>();
        static Vector3 NearestClear(Vector3 pos, BossEncounter b, IReadOnlyList<Hazard> live, out float need)
        {
            for (float r = 0.2f; r <= 10f; r += 0.2f)
                for (int k = 0; k < 36; k++)
                {
                    var dir = Sim.Dir(k * 10f); var q = pos + dir * r;
                    if (!OkSpot(q, b.transform.position, b.BodyRadius)) continue;
                    if (live.Any(h => h.Contains(q))) continue;
                    // do not path through the boss body
                    var c = Sim.Flat(b.transform.position - pos); float along = Mathf.Clamp(Vector3.Dot(c, dir), 0, r);
                    if ((c - dir * along).magnitude < b.BodyRadius + PlayerController.Radius - 0.05f) continue;   // tolerance: standing flush against the body
                    need = r; return dir;
                }
            need = 99; return Vector3.zero;
        }
        static bool OkSpot(Vector3 q, Vector3 boss, float bodyR) =>
            new Vector2(q.x, q.z).magnitude <= CombatWorld.PlayerBound - 0.1f && Sim.Flat(q - boss).magnitude >= bodyR + PlayerController.Radius + 0.05f;

        // Thrust probe: stand ~8.8 m from him on the arena-centre side (draws the Thrust, out of Alignment range), then
        // either stand still in the lane or step sideways only once he has started moving.
        IEnumerator ThrustStand(AurelionEncounter a, Func<bool> done, bool sidestep)
        {
            float t = 0; Vector3 side = Vector3.zero;
            while (t < 18f && d.State == RunState.Combat && !Safe(done))
            {
                var p = d.Player; var bpos = Sim.Flat(a.transform.position); var pos = p.transform.position;
                bot.Aim = bpos; bot.Sprint = false;
                var lane = d.World.Hazards.Live.FirstOrDefault(h => h.Source == "Processional Thrust");
                Vector3 move = Vector3.zero;
                if (lane == null)
                {
                    side = Vector3.zero;
                    // the nearest spot 8.8 m from him that is on the floor and not behind his body
                    Vector3 best = pos; float bestD = float.MaxValue;
                    for (int k = 0; k < 36; k++)
                    {
                        var s = bpos + Sim.Dir(k * 10f) * 8.8f;
                        if (new Vector2(s.x, s.z).magnitude > 10.5f) continue;
                        var dir = Sim.Flat(s - pos); var c = bpos - Sim.Flat(pos);
                        float along = Mathf.Clamp(Vector3.Dot(c, dir.normalized), 0, dir.magnitude);
                        if ((c - dir.normalized * along).magnitude < a.BodyRadius + PlayerController.Radius + 0.3f) continue;
                        if (dir.magnitude < bestD) { bestD = dir.magnitude; best = s; }
                    }
                    var v = Sim.Flat(best - pos);
                    if (v.magnitude > 0.3f) move = v.normalized;
                    else if (bestD == float.MaxValue) move = Sim.Flat(pos - bpos).normalized;   // boxed in: back off first
                }
                else if (sidestep && lane.Age >= AurelionEncounter.ThrustWarn)
                {
                    if (side == Vector3.zero) side = Sim.Dir(lane.Yaw + 90);
                    move = side;
                }
                bot.MoveWorld = move;
                if (Mathf.FloorToInt(t / 2f) != Mathf.FloorToInt((t + Time.deltaTime) / 2f))
                    Note($"   thrust probe t {t:0} s: player {Sim.Flat(pos - bpos).magnitude:0.0} m from him at ({pos.x:0.0},{pos.z:0.0}), hp {p.Hp}, boss '{a.Current}', thrusts {a.ThrustsStarted}, lane {(lane != null ? lane.Age.ToString() : "-")}");
                t += Time.deltaTime; yield return null;
            }
            bot.Clear();
        }

        // ---------------------------------------------------------------- route proofs
        void AurelionRouteProofs()
        {
            const float body = 1.1f;   // AurelionEncounter.BodyRadius
            var o = Vector3.zero;
            var cases = new (string name, Hazard h, float window)[]
            {
                ("Processional Thrust", new Hazard { Shape = HazardShape.Lane, Origin = o, Width = AurelionEncounter.ThrustWidth, Length = AurelionEncounter.ThrustMax + body }, AurelionEncounter.ThrustWarn / 60f),
                ("Crown Reversal", new Hazard { Shape = HazardShape.Cone, Origin = o, Yaw = 180, Radius = AurelionEncounter.RevReach, HalfAngle = AurelionEncounter.RevHalf }, AurelionEncounter.RevWarn / 60f),
                ("Meridian Alignment I", new Hazard { Shape = HazardShape.Ring, Origin = o, Inner = AurelionEncounter.AlignOuterIn, Radius = AurelionEncounter.AlignOuterOut }, AurelionEncounter.AlignWarn1 / 60f),
                ("Meridian Alignment II", new Hazard { Shape = HazardShape.Ring, Origin = o, Inner = AurelionEncounter.AlignInnerIn, Radius = AurelionEncounter.AlignInnerOut }, AurelionEncounter.AlignWarn2 / 60f),
            };
            foreach (var (name, h, window) in cases)
            {
                float worst = WorstExit(q => h.Contains(q), body, out var at);
                RouteCheck(name, worst, window, $"worst start {Sim.Flat(at).magnitude:0.0} m from centre");
            }

            // Ring II's safe pocket hugging his body is real: there is standing room between his body and the ring
            var ring2 = cases[3].h; int pocket = 0;
            for (int k = 0; k < 72; k++) { var q = Sim.Dir(k * 5f) * (body + PlayerController.Radius + 0.1f); if (!ring2.Contains(q)) pocket++; }
            Check("Alignment II: the pocket hugging his body is safe all the way round", pocket == 72, $"{pocket}/72 directions, pocket {body + PlayerController.Radius:0.00}–{AurelionEncounter.AlignInnerIn - Hazard.Margin:0.00} m from his centre");

            // dash checks: from the worst spot neither walking (6 m/s) nor sprinting (9 m/s) clears after the lock;
            // walking plus one dash does, at both walk speeds
            var cut = new Hazard { Shape = HazardShape.Cone, Origin = o, Radius = AurelionEncounter.CutReach, HalfAngle = AurelionEncounter.CutHalf };
            float cutWorst = WorstExit(q => cut.Contains(q), body, out var cutAt);
            DashCheck("Meridian Cut", cutWorst, (AurelionEncounter.CutWarn - AurelionEncounter.CutTrack) / 60f, $"worst start {Sim.Flat(cutAt).magnitude:0.0} m from centre");
            // Pins: the circle sits on the player when it locks; the floor bound can only shorten the way out
            float pinWorst = 0;
            for (float x = -11.5f; x <= 11.5f; x += 0.5f)
                for (float z = -11.5f; z <= 11.5f; z += 0.5f)
                {
                    var p = new Vector3(x, 0, z); if (!OkSpot(p, o, body)) continue;
                    var pin = new Hazard { Shape = HazardShape.Circle, Origin = PinCentre(p), Radius = AurelionEncounter.PinRadius };
                    pinWorst = Mathf.Max(pinWorst, ExitFrom(p, q => pin.Contains(q), body));
                }
            DashCheck("Zenith Pins", pinWorst, (AurelionEncounter.PinWarn - AurelionEncounter.PinTrack) / 60f, "circle on the player at lock");

            // Misalignment Pins -> Cut: one dash for the pins, one for the cut, from every start in front of him
            foreach (float speed in new[] { ProtoWalk, DesignWalk })
            {
                int starts = 0, ok = 0; string firstFail = null;
                for (float dist = body + 0.5f; dist <= 10.5f; dist += 0.5f)
                    foreach (float off in new[] { 0f, -35f, 35f })
                    {
                        starts++;
                        var s = Sim.Dir(off) * dist;
                        if (PinsCutRoute(s, speed, body)) ok++; else firstFail ??= $"{dist:0.0} m at {off:0}°";
                    }
                Check($"route proof {speed:0.0} m/s: Pins -> Cut has a route (walk + one dash each) from every start", ok == starts, $"{ok}/{starts}" + (firstFail != null ? ", first fail " + firstFail : ""));
            }
        }
        static Vector3 PinCentre(Vector3 p)
        {
            var v = new Vector2(p.x, p.z); float max = CombatWorld.PlayerBound - AurelionEncounter.PinRadius * 0.5f;
            if (v.magnitude > max) v = v.normalized * max;
            return new Vector3(v.x, 0, v.y);
        }
        void RouteCheck(string name, float exit, float window, string detail)
        {
            foreach (float speed in new[] { ProtoWalk, DesignWalk })
            {
                float need = exit / speed + ReactS;
                Check($"route proof {speed:0.0} m/s: {name} escapable on foot", need <= window, $"walk {exit:0.00} m = {need:0.00} s incl. reaction, window {window:0.00} s, spare {window - need:0.00} s ({detail})");
            }
        }
        // time to cover `exit` metres with one dash (3 m over 0.2 s) and walking after it, from the reaction
        static float DashTime(float exit, float walk) =>
            ReactS + (exit <= PlayerController.DashDistance ? exit / PlayerController.DashDistance * PlayerController.DashTicks / Sim.Hz
                                                            : PlayerController.DashTicks / (float)Sim.Hz + (exit - PlayerController.DashDistance) / walk);
        void DashCheck(string name, float exit, float window, string detail)
        {
            float walkNeed = exit / DesignWalk + ReactS, sprintNeed = exit / PlayerController.SprintSpeed + ReactS;
            Check($"dash check: {name} cannot be walked or sprinted out of from the worst spot", walkNeed > window && sprintNeed > window, $"exit {exit:0.00} m: walk 6 m/s {walkNeed:0.00} s, sprint 9 m/s {sprintNeed:0.00} s, window {window:0.00} s ({detail})");
            foreach (float speed in new[] { ProtoWalk, DesignWalk })
            {
                float need = DashTime(exit, speed);
                Check($"dash check {speed:0.0} m/s: {name} escapable with one dash (no immunity)", need <= window, $"{need:0.00} s incl. reaction, window {window:0.00} s, spare {window - need:0.00} s");
            }
        }
        static float WorstExit(Func<Vector3, bool> inside, float body, out Vector3 worstAt)
        {
            float worst = 0; worstAt = Vector3.zero;
            for (float x = -11.5f; x <= 11.5f; x += 0.25f)
                for (float z = -11.5f; z <= 11.5f; z += 0.25f)
                {
                    var p = new Vector3(x, 0, z);
                    if (!OkSpot(p, Vector3.zero, body) || !inside(p)) continue;
                    float e = ExitFrom(p, inside, body);
                    if (e > worst) { worst = e; worstAt = p; }
                }
            return worst;
        }
        static float ExitFrom(Vector3 p, Func<Vector3, bool> inside, float body)
        {
            if (!inside(p)) return 0;
            for (float r = 0.05f; r <= 12f; r += 0.05f)
                for (int k = 0; k < 72; k++)
                {
                    var dir = Sim.Dir(k * 5f); var q = p + dir * r;
                    if (!OkSpot(q, Vector3.zero, body) || inside(q)) continue;
                    var c = -Sim.Flat(p); float along = Mathf.Clamp(Vector3.Dot(c, dir), 0, r);
                    if ((c - dir * along).magnitude < body + PlayerController.Radius) continue;   // not through the body
                    return r;
                }
            return 99f;
        }
        // Tick simulation from the pins' lock (= the cut's first warning tick). The cut tracks the player at 240°/s for
        // CutTrack ticks, then locks. The player reacts 0.25 s after each lock with a dash (3 m / 0.2 s) along one
        // heading and keeps walking that way; floor bound and boss body hold it. Pins resolve at +27, the cut at +96.
        // A route = one heading pair (pin dash, cut dash) that clears both.
        static bool PinsCutRoute(Vector3 start, float speed, float body)
        {
            var pin = new Hazard { Shape = HazardShape.Circle, Origin = PinCentre(start), Radius = AurelionEncounter.PinRadius };
            int pinEnd = AurelionEncounter.PinWarn - AurelionEncounter.PinTrack, cutLock = AurelionEncounter.CutTrack;
            for (int ka = 0; ka < 24; ka++)
                for (int kb = 0; kb < 24; kb++)
                {
                    var pos = start; var dir = Sim.Dir(ka * 15f); int dashLeft = 0;
                    var cut = new Hazard { Shape = HazardShape.Cone, Origin = Vector3.zero, Radius = AurelionEncounter.CutReach, HalfAngle = AurelionEncounter.CutHalf, Yaw = Sim.YawTo(Vector3.zero, start, 0) };
                    bool hit = false;
                    for (int t = 1; t <= AurelionEncounter.CutWarn && !hit; t++)
                    {
                        if (t <= cutLock) cut.Yaw = Mathf.MoveTowardsAngle(cut.Yaw, Sim.YawTo(Vector3.zero, pos, cut.Yaw), 240f * Sim.Dt);
                        if (t == ReactTicks) dashLeft = PlayerController.DashTicks;
                        if (t == cutLock + ReactTicks) { dir = Sim.Dir(kb * 15f); dashLeft = PlayerController.DashTicks; }
                        if (t >= ReactTicks)
                        {
                            float step = dashLeft > 0 ? PlayerController.DashDistance / PlayerController.DashTicks : speed * Sim.Dt;
                            if (dashLeft > 0) dashLeft--;
                            pos += dir * step;
                            var f = new Vector2(pos.x, pos.z); if (f.magnitude > CombatWorld.PlayerBound) { f = f.normalized * CombatWorld.PlayerBound; pos = new Vector3(f.x, 0, f.y); }
                            float m = body + PlayerController.Radius; if (Sim.Flat(pos).magnitude < m) pos = Sim.Flat(pos).normalized * m;
                        }
                        if (t == pinEnd && pin.Contains(pos)) hit = true;
                        if (t == AurelionEncounter.CutWarn && cut.Contains(pos)) hit = true;
                    }
                    if (!hit) return true;
                }
            return false;
        }
    }
}
