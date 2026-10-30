using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Orrery Closing (aurelion.orrery_closing) checks, owned by the Orrery Closing session; called once from AurelionV2Checks
    // (RunAutoTest.Aurelion.V2.cs). Brief: abilities-v2/04-orrery_closing.md, "Escape-check plan" and the eight autotest checks.
    // Harness: V2Setup(n, level, positions, phase, bossAt), V2In(p).Go/Walk/Dash/Stop, V2Fire / V2Begin + V2WaitIdle on
    // V2Boss.<Ability>.Run(...), V2Hits / V2Spawns, V2OneHitPerInstance(), V2KillBoss(), V2Check(name, ok, detail).
    public sealed partial class RunAutoTest
    {
        const string OcRim = "Orrery Closing rim", OcCrown = "Orrery Closing crown";

        IEnumerator V2OrreryClosing()
        {
            yield return OcTimingAndStandingStill();
            yield return OcEscape();
            yield return OcHorizon(Difficulty.Medium);
            yield return OcHorizon(Difficulty.Hard);
            yield return OcHorizon(Difficulty.Easy);
            foreach (int n in new[] { 1, 2, 4, 8 }) yield return OcParty(n);
            yield return OcDeathMidWindup();
            yield return OcDeathMidHorizon();
            V2Check("one hit per instance across every Orrery Closing check", ocOneHit, ocOneHitWhere);
        }

        bool ocOneHit = true; string ocOneHitWhere = "";
        void OcNoteOneHit(string where) { if (!V2OneHitPerInstance()) { ocOneHit = false; ocOneHitWhere += where + " "; } }
        // runs a body and stamps the world tick it finished on
        IEnumerable<int> OcThen(IEnumerable<int> body, System.Action after) { foreach (var x in body) yield return x; after(); }

        // waits on the world's ticks (wall-time guarded), so a defeat that stops the world can't hang the test
        IEnumerator OcUntilTick(int tick, float timeout = 15)
        {
            var w = V2W; float t = 0;
            while (d.World == w && w.Tick < tick && t < timeout) { t += Time.deltaTime; yield return null; }
        }
        IEnumerator OcWait(float seconds) { float t = 0; while (t < seconds) { t += Time.deltaTime; yield return null; } }

        // checks 1, 3, 4 and 5 (no Horizon, Phase 1): spawn, lock, resolve tick; everyone standing still in a shape is hit once
        IEnumerator OcTimingAndStandingStill()
        {
            var at = new[]
            {
                new Vector3(12, 0, 0),      // 12 m: rim
                new Vector3(0, 0, 13.6f),   // the arena edge: rim
                new Vector3(-9, 0, 0),      // 9 m from the centre: rim
                new Vector3(0, 0, -0.6f),   // against his body: crown
                new Vector3(5, 0, 3),       // the safe floor between them
            };
            yield return V2Setup(at.Length, Difficulty.Medium, at, 1);
            if (V2Boss == null) yield break;
            var c = V2Boss.Closing; var bossAt = Sim.Flat(V2Boss.transform.position);
            int t0 = V2W.Tick, ended = -1;
            V2Begin(OcThen(c.Run(0), () => ended = V2W.Tick));
            yield return Until(() => V2Spawned("Orrery Closing").Count() >= 2, 5, "Orrery Closing spawned");
            var rimS = V2Spawned(OcRim).FirstOrDefault(); var crownS = V2Spawned(OcCrown).FirstOrDefault();
            if (rimS == null || crownS == null) { V2Check("spawns a rim and a crown", false, string.Join(", ", V2Spawns.Select(s => s.Source))); yield break; }
            var rim = rimS.H; var crown = crownS.H;
            int warn = Sim.Ticks(OrreryClosing.WarnS);
            V2Check("the rim and the crown spawn on the same tick", rimS.Tick == crownS.Tick && rimS.Tick > t0, $"rim {rimS.Tick}, crown {crownS.Tick}");
            V2Check("rim: Ground ring on the arena centre, 8.5 m out to past the bound", rim.Shape == HazardShape.Ring && rim.Kind == HazardKind.Ground && Sim.Flat(rim.Origin).magnitude < 0.01f && Mathf.Approximately(rim.Inner, 8.5f) && rim.Radius > CombatWorld.PlayerBound,
                $"{rim.Shape} {rim.Kind} at {rim.Origin}, {rim.Inner}-{rim.Radius} m");
            V2Check("crown: Ground circle of 3.6 m on the boss", crown.Shape == HazardShape.Circle && crown.Kind == HazardKind.Ground && Sim.Flat(crown.Origin - bossAt).magnitude < 0.01f && Mathf.Approximately(crown.Radius, 3.6f),
                $"{crown.Shape} {crown.Kind} at {crown.Origin} (boss {bossAt}), r {crown.Radius}");
            V2Check("both locked on the warning's first tick, resolving 2.3 s (138 ticks) later", rim.LockAt == 0 && crown.LockAt == 0 && rim.ResolveAt == warn && crown.ResolveAt == warn && warn == 138,
                $"lock {rim.LockAt}/{crown.LockAt}, resolve {rim.ResolveAt}/{crown.ResolveAt}");
            V2Check("40 damage each, no knockback, 0.25 s stun, no Horizon in Phase 1", rim.Damage == 40 && crown.Damage == 40 && rim.Knockback == 0 && crown.Knockback == 0 && rim.Stun == 15 && crown.Stun == 15 && rim.ActiveTicks <= 1,
                $"dmg {rim.Damage}/{crown.Damage}, kb {rim.Knockback}/{crown.Knockback}, stun {rim.Stun}/{crown.Stun}, active {rim.ActiveTicks}");
            // he moves during the warning: the crown stays where it was locked
            yield return V2Ticks(30);
            V2Boss.transform.position = bossAt + new Vector3(3, 0, 0);
            yield return V2Ticks(2);
            V2Check("the crown stays where it locked when he moves", Sim.Flat(crown.Origin - bossAt).magnitude < 0.01f, $"crown {crown.Origin}");
            int resolveTick = rimS.Tick + warn - 1;   // spawned at Age 0 in the boss step, aged once in that tick's hazard step
            yield return V2WaitIdle(10);
            yield return V2Ticks(10);
            var P = V2Party;
            V2Check("standing still at 12 m: hit by the rim on the resolve tick", V2HitsOn(P[0]).Count() == 1 && V2HitsOn(P[0], OcRim).Any(h => h.Tick == resolveTick), OcHitsText(P[0]));
            V2Check("standing still at the arena edge: hit by the rim", V2HitsOn(P[1]).Count() == 1 && V2WasHit(P[1], OcRim), OcHitsText(P[1]));
            V2Check("standing still at 9 m: hit by the rim", V2HitsOn(P[2]).Count() == 1 && V2WasHit(P[2], OcRim), OcHitsText(P[2]));
            V2Check("standing still against his body: hit by the crown, on the same tick", V2HitsOn(P[3]).Count() == 1 && V2HitsOn(P[3], OcCrown).Any(h => h.Tick == resolveTick), OcHitsText(P[3]));
            V2Check("standing still between them: not hit", !V2WasHit(P[4]), OcHitsText(P[4]));
            V2Check("Run returns right after the resolve, with nothing left live", ended >= resolveTick && ended <= resolveTick + 1 && V2W.Hazards.Count == 0 && !c.HorizonLive,
                $"resolve {resolveTick}, Run ended on {ended}, live {V2W.Hazards.Count}, Horizon {c.HorizonLive}");
            OcNoteOneHit("standing-still");
        }

        string OcHitsText(PlayerController p) { var hs = V2HitsOn(p).ToList(); return hs.Count == 0 ? "no hits" : string.Join(", ", hs.Select(h => $"{h.Source} @{h.Tick} ({h.Damage})")); }

        // check 2: the worst case. A player 13.5 m out behind a boss standing 6 m out reacts after 0.25 s and comes round
        // his crown onto the safe floor (inside 8.2 m, outside 3.9 m of him): 7.96 m. Hard (2.0 s window, v2.1 §2): a sprint
        // (9 m/s) or a 4.2 m/s walk with one dash.
        IEnumerator OcEscape()
        {
            var start = new Vector3(0, 0, -13.5f); var boss = new Vector3(0, 0, -6); var safe = new Vector3(4.3f, 0, -6.8f);
            foreach (var (level, speed, dash, sprint, required) in new[]
            {
                (Difficulty.Medium, 4.2f, true, false, true), (Difficulty.Medium, 6f, false, false, true), (Difficulty.Medium, 4.2f, false, false, false),
                (Difficulty.Hard, 6f, false, true, true), (Difficulty.Hard, 4.2f, true, false, true),
            })
            {
                yield return V2Setup(1, level, new[] { start }, 1, boss);
                if (V2Boss == null) yield break;
                var p = V2Party[0]; var inp = V2In(p);
                float stamina0 = p.Stamina;
                V2Begin(V2Boss.Closing.Run(0));
                yield return V2Ticks(Sim.Ticks(0.25f));
                inp.Go(safe, speed); inp.Sprint = sprint;   // sprint: full stick at 6 m/s input runs at SprintSpeed (9 m/s)
                if (dash) { yield return V2Ticks(1); inp.Dash(); }
                int arrived = -1;
                var w = V2W; float t = 0;
                while (V2Boss.TestBusy && t < 10) { if (arrived < 0 && Sim.Flat(p.transform.position - safe).magnitude < 0.1f) arrived = w.Tick; t += Time.deltaTime; yield return null; }
                yield return V2Ticks(3);
                var crown = V2Spawned(OcCrown).FirstOrDefault()?.H;
                int resolveTick = crown != null ? V2Spawned(OcCrown).First().Tick + crown.ResolveAt - 1 : -1;
                bool hit = V2WasHit(p, "Orrery Closing");
                string detail = $"route {Sim.Flat(safe - start).magnitude:0.00} m, arrived tick {arrived}, resolve {resolveTick}, margin {(arrived > 0 ? (resolveTick - arrived) * Sim.Dt : -1):0.00} s, stamina {stamina0:0}->{p.Stamina:0}; {OcHitsText(p)}";
                string move = sprint ? "sprint (9 m/s)" : $"{speed} m/s walk{(dash ? " + one dash" : " only")}";
                string name = $"{level}: 13.5 m behind a boss 6 m out: {move}, reacting after 0.25 s, is not hit";
                if (required) V2Check(name, !hit && arrived > 0 && (!dash && !sprint || p.Stamina < stamina0), detail);
                else Note($"(info, the brief needs a dash at 4.2 m/s) {name}: {(hit ? "hit" : "not hit")}; {detail}");
                inp.Stop();
            }
        }

        // check 5 and 8 on the Horizon: 4 s (Hard 6 s) after the resolve; stepping out is one hit, again is none, after it is safe
        IEnumerator OcHorizon(Difficulty level)
        {
            var at = new[] { new Vector3(0, 0, 7.5f), new Vector3(12, 0, 0), new Vector3(5, 0, 0) };
            yield return V2Setup(at.Length, level, at, 2);
            if (V2Boss == null) yield break;
            var c = V2Boss.Closing; var P = V2Party;
            int horizon = V2Boss.HorizonFor();
            int want = level == Difficulty.Hard ? 360 : 240;
            V2Check($"{level}: the Horizon is {want / 60f:0} s", horizon == want, $"{horizon} ticks");
            V2Begin(c.Run(horizon));
            yield return Until(() => V2Spawned(OcRim).Any(), 5, "Orrery Closing rim spawned");
            var rs = V2Spawned(OcRim).FirstOrDefault(); if (rs == null) yield break;
            // Medium 2.3 s, Easy x1.25 = 2.9 s, Hard x0.7 = 1.61 s held at the 2.0 s floor (v2.1 §2)
            int warn = level == Difficulty.Easy ? Sim.Ticks(OrreryClosing.WarnS * 1.25f) : level == Difficulty.Hard ? Sim.Ticks(OrreryClosing.HardFloorS) : Sim.Ticks(OrreryClosing.WarnS);
            V2Check($"{level}: warning {(level == Difficulty.Easy ? "2.9 s (x1.25)" : level == Difficulty.Hard ? "2.0 s (the floor)" : "2.3 s")}", rs.H.ResolveAt == warn && rs.H.LockAt == 0, $"resolve at {rs.H.ResolveAt}, want {warn}");
            int resolveTick = rs.Tick + rs.H.ResolveAt - 1, spentTick = -1;
            V2Check($"{level}: Duration() is the resolve offset from Run's first tick", c.Duration() == resolveTick - rs.Tick && c.Duration() == warn - 1, $"Duration {c.Duration()}, resolve offset {resolveTick - rs.Tick}");
            rs.H.OnSpent += h => spentTick = V2W.Tick;
            yield return V2WaitIdle(10);
            V2Check($"{level}: the Horizon is live once Run returns", c.HorizonLive, $"tick {V2W.Tick}, resolve {resolveTick}");
            // the 12 m player was hit by the rim at the resolve; the Horizon is the same instance, so never again
            // the 7.5 m player steps out, back in, out again: hit once
            yield return OcUntilTick(resolveTick + 20);
            V2In(P[0]).Go(new Vector3(0, 0, 9.5f), 6f);
            yield return Until(() => V2WasHit(P[0], OcRim), 3, $"{level}: stepping out during the Horizon hits");
            int outHit = V2HitsOn(P[0], OcRim).Select(h => h.Tick).DefaultIfEmpty(-1).First();
            yield return V2Ticks(Sim.Ticks(0.4f));
            V2In(P[0]).Go(new Vector3(0, 0, 7f), 6f);
            yield return V2Ticks(Sim.Ticks(0.6f));
            V2In(P[0]).Go(new Vector3(0, 0, 9.5f), 6f);
            yield return V2Ticks(Sim.Ticks(0.6f));
            bool stillLive = c.HorizonLive;
            V2Check($"{level}: stepping out past 8.5 m during the Horizon is one hit; stepping out again is none",
                V2HitsOn(P[0], OcRim).Count() == 1 && outHit > resolveTick && stillLive, $"{OcHitsText(P[0])}; resolve {resolveTick}, Horizon still live {stillLive}");
            V2In(P[0]).Go(new Vector3(0, 0, 7f), 6f);
            // the Horizon's length: the rim's last active tick is resolve + horizon, then it is gone
            yield return Until(() => spentTick >= 0, 10, $"{level}: the Horizon expired");
            yield return V2Ticks(1);
            int span = spentTick - resolveTick;
            V2Check($"{level}: the Horizon hits for {horizon} ticks after the resolve, then expires", span == horizon && !c.HorizonLive && V2W.Hazards.Count == 0,
                $"spent {span} ticks after the resolve, Horizon {c.HorizonLive}, live {V2W.Hazards.Count}");
            // after it: walking out is safe
            V2In(P[2]).Go(new Vector3(12, 0, 0), 6f);
            yield return V2Ticks(Sim.Ticks(1.5f));
            V2Check($"{level}: after the Horizon, walking out to 12 m is safe", !V2WasHit(P[2]), OcHitsText(P[2]));
            V2Check($"{level}: standing outside through the whole Horizon is one hit", V2HitsOn(P[1]).Count() == 1 && V2HitsOn(P[1], OcRim).First().Tick == resolveTick, OcHitsText(P[1]));
            foreach (var p in P) V2In(p).Stop();
            OcNoteOneHit(level + " Horizon");
        }

        // check 6: the same two shapes for every N. The party stands on a 6 m ring (one on top of a boss standing 6 m out);
        // whoever is inside his crown walks 4.4 m clear of him at 4.2 m/s after 0.25 s. Nobody is hit. Then everyone on a
        // 12 m ring stands still and is hit once by the rim.
        IEnumerator OcParty(int n)
        {
            var boss = new Vector3(0, 0, -6);
            var at = V2Ring(n, 6, null, 180);
            yield return V2Setup(n, Difficulty.Medium, at, 1, boss);
            if (V2Boss == null) yield break;
            var P = V2Party;
            V2Begin(V2Boss.Closing.Run(0));
            yield return V2Ticks(Sim.Ticks(0.25f));
            int movers = 0;
            foreach (var p in P)
            {
                var pos = Sim.Flat(p.transform.position); var away = pos - boss;
                if (away.magnitude > OrreryClosing.CrownRadius + Hazard.Margin + 0.2f) continue;
                // the nearest spot 4.4 m from him that is still inside the rim's safe floor (the body push can leave a
                // player on his far side, where straight out would cross the rim)
                var to = Enumerable.Range(0, 24).Select(k => boss + Sim.Dir(k * 15f) * 4.4f).Where(c => c.magnitude <= 7.8f)
                    .OrderBy(c => (c - pos).sqrMagnitude).First();
                V2In(p).Go(to, 4.2f); movers++;
            }
            yield return V2WaitIdle(10);
            yield return V2Ticks(3);
            int spawns = V2Spawned("Orrery Closing").Count();
            V2Check($"N = {n}: one rim and one crown ({movers} in his crown walk out at 4.2 m/s); nobody is hit",
                spawns == 2 && P.All(p => !V2WasHit(p)), $"spawns {spawns}; " + string.Join("; ", P.Select((p, i) => $"P{i} {OcHitsText(p)}")));
            if (n == 1) V2Check("solo rule: the same shapes and timing as any party (not a co-op mechanic)", spawns == 2);

            var far = V2Ring(n, 12, null, 30);
            for (int i = 0; i < n; i++) { V2In(P[i]).Stop(); V2Place(P[i], far[i]); }
            V2Hits.Clear(); V2Spawns.Clear();
            yield return V2Fire(V2Boss.Closing.Run(0));
            yield return V2Ticks(3);
            V2Check($"N = {n}: everyone standing still at 12 m is hit once by the rim", P.All(p => V2HitsOn(p).Count() == 1 && V2WasHit(p, OcRim)),
                string.Join("; ", P.Select((p, i) => $"P{i} {OcHitsText(p)}")));
            OcNoteOneHit("N=" + n);
        }

        // check 7: boss death mid-windup clears both instances in the killing tick; nobody is hit afterwards
        IEnumerator OcDeathMidWindup()
        {
            var at = new[] { new Vector3(12, 0, 0), new Vector3(0, 0, -0.6f) };
            yield return V2Setup(2, Difficulty.Medium, at, 2);
            if (V2Boss == null) yield break;
            var c = V2Boss.Closing; var P = V2Party.ToList();
            V2Begin(c.Run(V2Boss.HorizonFor()));
            yield return V2Ticks(60);
            int live = V2W.Hazards.Count;
            V2KillBoss();
            int after = V2W.Hazards.Count; bool horizon = c.HorizonLive;
            yield return OcWait(4f);
            V2Check("boss death mid-windup cancels the rim and the crown in the killing tick", live == 2 && after == 0 && !horizon && P.All(p => !V2WasHit(p)),
                $"live {live} -> {after}, Horizon {horizon}; {string.Join("; ", P.Select(OcHitsText))}");
        }

        // brief: boss death mid-Horizon cancels it; stepping out afterwards is safe
        IEnumerator OcDeathMidHorizon()
        {
            var at = new[] { new Vector3(0, 0, 5) };
            yield return V2Setup(1, Difficulty.Medium, at, 2);
            if (V2Boss == null) yield break;
            var c = V2Boss.Closing; var p = V2Party[0];
            V2Begin(c.Run(V2Boss.HorizonFor()));
            yield return V2WaitIdle(10);
            yield return V2Ticks(30);
            bool before = c.HorizonLive;
            V2KillBoss();
            bool after = c.HorizonLive; int live = V2W.Hazards.Count;
            V2In(p).Go(new Vector3(0, 0, 11), 6f);
            yield return OcWait(2f);
            V2Check("boss death mid-Horizon cancels it: stepping out afterwards is safe", before && !after && live == 0 && !V2WasHit(p), $"Horizon {before} -> {after}, live {live}; {OcHitsText(p)}");
            V2In(p).Stop();
        }
    }
}
