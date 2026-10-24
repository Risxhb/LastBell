using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Bellkeeper Ultimate's strat bot (Part 5 of ultimate/PROMPT.md): plays GUIDE.md through ordinary PlayerInput, with
    // no immunity and no direct state changes. Each tick it gathers
    //   - every live hazard that would hit it (visible from its spawn tick: be outside it on its resolve tick), and
    //   - every mechanic's Demands() for it (the guide's assignments and memories, visible from their Since tick),
    // and walks to a spot that satisfies the earliest group of rules and stays close to one that satisfies the next.
    // Skill: Perfect reacts on the first tick it could know; Late reacts ReactExtra ticks later (+0.25 s); Miss skips the
    // first memory assignment (BotDemand.Memory) of each segment. Attacks: the class rotation when the boss is in range.
    public sealed class UltimateBot : IInputSource
    {
        public enum Skill { Perfect, Late, Miss }
        public Skill Mode = Skill.Perfect;
        public float Speed = PlayerController.WalkSpeed;   // planning and walking speed, m/s (4.2 = the escape-proof speed)
        public bool Attacks = true, UseDash = true;
        public int ReactExtra = 15;                         // Late: 0.25 s
        public float UptimePull = 0.3f;                     // score per metre out of reach while a rule holds it (uptime vs travel)
        public float HoldGap = -1;                          // gap to the boss when idle (-1 = the class's range)
        public int Slot, Of = 1;                            // this bot's angle round the boss when idle
        public string DebugLine;
        public int Decisions { get; private set; }          // tests: personal reads acted on (memory demands) + forced repositions
        public int Reads { get; private set; }

        readonly List<BotDemand> demands = new List<BotDemand>();
        readonly HashSet<string> missedKeys = new HashSet<string>();     // Miss: "segment|mechanic|launch" skipped
        readonly HashSet<string> seenReads = new HashSet<string>();
        readonly HashSet<int> skippedSegments = new HashSet<int>();
        Vector3 target; bool hasTarget; int replanAt, tick, lastPress = -999, castHold;
        readonly int[] cd = new int[5];

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss as BellkeeperEncounter;
            var pos = Sim.Flat(me.transform.position);
            if (boss == null || !boss.Solid) { inp.Move = ToScreen(Vector3.zero); return inp; }
            var bpos = Sim.Flat(boss.transform.position);
            inp.Aim = bpos + Vector3.up * 0.1f;
            int now = w.Tick, react = Mode == Skill.Late ? ReactExtra : 0;

            // ---- gather the rules ----
            demands.Clear();
            foreach (var h in w.Hazards.Live)
            {
                if (!h.HitOnResolve && h.BurnPerSecond <= 0) continue;   // first-body lanes etc.: their mechanic's Demands say what to do
                if (h.BurnPerSecond > 0) continue;                       // standing burns: a cost in scoring, not a rule
                if (h.Hidden) continue;                                  // an unavoidable raidwide: nothing to dodge (potions handle it)
                int spawn = now - h.Age + 1, at = now + (h.ResolveAt - h.Age);
                var hz = h;
                demands.Add(new BotDemand { Tick = at, Since = spawn, Ok = q => !hz.Contains(q), Hint = pos, Why = h.Source });
            }
            if (boss.Mechanics != null)
                foreach (var m in boss.Mechanics) m.Demands(me, demands);

            // ---- filter: known by now, not past, inside the horizon; Miss skips one memory assignment per segment ----
            int seg = (int)boss.Segment;
            for (int i = demands.Count - 1; i >= 0; i--)
            {
                var d = demands[i];
                if (d.Tick < now || d.Since + react > now || d.Tick > now + 6 * Sim.Hz) { demands.RemoveAt(i); continue; }
                if (d.Memory)
                {
                    string key = $"{seg}|{d.From?.Key}|{d.From?.LastCastTick}";
                    if (Mode == Skill.Miss)
                    {
                        if (skippedSegments.Add(seg)) missedKeys.Add(key);   // the first memory assignment of each segment
                        if (missedKeys.Contains(key)) { demands.RemoveAt(i); continue; }
                    }
                    if (seenReads.Add(key)) { Reads++; Decisions++; }
                }
            }
            demands.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            // ---- potions: a MinHp rule, or low ----
            int needHp = 0; foreach (var d in demands) needHp = Mathf.Max(needHp, d.MinHp);
            if (me.Hp < PlayerController.MaxHp && me.Potions > 0 && me.Stunned == 0 && (me.Hp < needHp || me.Hp < 55 || (FreezeSoon(boss, me, now) && me.Hp < 90))) inp.Potion = tick % 4 == 0;   // potions are locked while Stilled: top up under the hourglass

            // ---- plan a spot (every 4 ticks, or at once when the current spot breaks a rule) ----
            var rules = new List<BotDemand>(); foreach (var d in demands) if (d.Ok != null) rules.Add(d);
            if (rules.Count == 0) { hasTarget = false; }
            else if (!hasTarget || now >= replanAt || !Satisfies(target, rules, rules[0].Tick + 6))
            {
                var old = target; bool had = hasTarget;
                hasTarget = Plan(me, boss, pos, rules, now, out target);
                replanAt = now + 4;
                if (hasTarget && (!had || Sim.Flat(target - old).magnitude > 2f)) Decisions++;
            }

            Vector3 move = Vector3.zero;
            if (hasTarget)
            {
                var to = Sim.Flat(target - pos); float dist = to.magnitude;
                float left = rules.Count > 0 ? (rules[0].Tick - now) / (float)Sim.Hz : 9;
                if (dist > 0.02f)
                {
                    move = to / dist * Mathf.Clamp01(dist / (Speed * Sim.Dt)) * Mathf.Clamp01(Speed / PlayerController.WalkSpeed);
                    if (UseDash && !me.Dashing && me.Stamina >= PlayerController.DashCost && dist > 1.2f && dist / Speed > left - 0.25f) { inp.Dash = true; move = to / dist; }
                    // a standing burn across the path (the P5 fissure): dash over it when one dash clears it (GUIDE.md)
                    else if (UseDash && !me.Dashing && me.Stamina >= PlayerController.DashCost && dist > 1.2f && DashesOverBurn(w, pos, to / dist)) { inp.Dash = true; move = to / dist; }
                }
                castHold = 0;
            }
            else
            {
                // idle: hold a spot round the boss at the class's range, on this bot's own angle
                float gap = HoldGap >= 0 ? HoldGap : Range(me);
                var want = bpos + Sim.Dir(360f * Slot / Mathf.Max(1, Of) + 20f) * (boss.BodyRadius + gap);
                if (want.magnitude > CombatWorld.PlayerBound - 0.8f) want = want.normalized * (CombatWorld.PlayerBound - 0.8f);
                var v = Sim.Flat(want - pos);
                if (v.magnitude > 0.35f && castHold == 0) move = v.normalized * Mathf.Clamp01(Speed / PlayerController.WalkSpeed);
                if (castHold > 0) castHold--;
            }
            float bossGap = Sim.Flat(bpos - pos).magnitude - boss.BodyRadius;
            // no swings while walking to a spot or just before a rule: lunges and casts would carry it off its spot
            // (a memory spot, e.g. a Stopped Hour hiding spot that also aims frozen clocks, is held still for its last 1.5 s)
            bool positioning = hasTarget && (Sim.Flat(target - pos).magnitude > 0.5f || (rules.Count > 0 && rules[0].Tick - now < (rules[0].Memory ? 90 : 30)));
            for (int i = 0; i < cd.Length; i++) if (cd[i] > 0) cd[i]--;
            bool stilled = boss.Ult != null && boss.Ult.IsStilled(me);
            // Chimesinger: Mending Chorus (burst heal round the caster) when it would top up a hurt ally or two (potions run out in P5)
            if (Attacks && me.ClassName == "Chimesinger" && !stilled && cd[4] == 0 && tick - lastPress >= 6)
            {
                int hurt = 0; foreach (var p in w.Players) if (!p.Down && p.Hp < 65 && Sim.Flat(p.transform.position - pos).magnitude < 5.5f) hurt++;
                if (hurt >= 2 || (hurt == 1 && me.Hp < 65)) { inp.P4 = true; cd[4] = 900; lastPress = tick; }
            }
            if (Attacks && !positioning && bossGap <= Range(me) + 1.5f && (boss.Ult == null || !boss.Ult.IsStilled(me))) Attack(me, ref inp, rules.Count == 0 || rules[0].Tick - now > 90);
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss, hasTarget ? target : (Vector3?)null);
            inp.Move = ToScreen(move);
            DebugLine = $"rules {rules.Count} first {(rules.Count > 0 ? rules[0].Why + " in " + (rules[0].Tick - now) : "-")} target {(hasTarget ? $"({target.x:0.0},{target.z:0.0})" : "-")} at ({pos.x:0.0},{pos.z:0.0})";
            return inp;
        }

        static bool Satisfies(Vector3 q, List<BotDemand> rules, int untilTick)
        {
            foreach (var r in rules) { if (r.Tick > untilTick) break; if (!r.Ok(q)) return false; }
            return true;
        }

        // Candidate spots: rings round the arena centre and the boss, every rule's hint, and here.
        readonly List<Vector3> cands = new List<Vector3>(), next = new List<Vector3>();
        readonly List<(int tick, List<Vector3> pts)> later = new List<(int, List<Vector3>)>();
        bool Plan(PlayerController me, BellkeeperEncounter boss, Vector3 pos, List<BotDemand> rules, int now, out Vector3 best)
        {
            cands.Clear(); cands.Add(pos);
            foreach (var r in rules) { cands.Add(Sim.Flat(r.Hint)); for (int k = 0; k < 8; k++) cands.Add(Sim.Flat(r.Hint) + Sim.Dir(45 * k) * 0.6f); }
            var bp = Sim.Flat(boss.transform.position);
            for (float rad = 0.4f; rad <= 12.8f; rad += 0.4f)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(rad * 6), 8, 72);
                for (int k = 0; k < n; k++) cands.Add(Sim.Dir(360f * k / n) * rad);
            }
            for (float rad = boss.BodyRadius + 0.8f; rad <= 8f; rad += 0.5f)
                for (int k = 0; k < 36; k++) cands.Add(bp + Sim.Dir(10f * k) * rad);

            // group 1: every rule due within 0.1 s of the first; group 2: the next rule after that
            int t1 = rules[0].Tick, g1End = 0;
            while (g1End < rules.Count && rules[g1End].Tick <= t1 + 6) g1End++;
            int t2 = g1End < rules.Count ? rules[g1End].Tick : -1, g2End = g1End;
            while (g2End < rules.Count && rules[g2End].Tick <= t2 + 6) g2End++;
            float secs1 = Mathf.Max(0.05f, (t1 - now) / (float)Sim.Hz), reach1 = Speed * secs1 + (UseDash && me.Stamina >= PlayerController.DashCost ? PlayerController.DashDistance : 0);

            next.Clear();
            if (t2 >= 0) foreach (var c in cands) if (Valid(c, boss) && AllOk(c, rules, g1End, g2End)) next.Add(c);
            // further groups (up to three more): a spot that leaves one of them out of reach in time is penalised, so a
            // later hide or clock spot pulls the bot early enough (a two-group horizon arrived late to Stopped Hour hides)
            later.Clear();
            for (int gs = g2End, k = 0; gs < rules.Count && k < 3; k++)
            {
                int tg = rules[gs].Tick, ge = gs;
                while (ge < rules.Count && rules[ge].Tick <= tg + 6) ge++;
                var pts = new List<Vector3>();
                for (int i = 0; i < cands.Count; i += 3) if (Valid(cands[i], boss) && AllOk(cands[i], rules, gs, ge)) pts.Add(cands[i]);
                if (pts.Count > 40) { int stride = pts.Count / 40 + 1; var few = new List<Vector3>(); for (int i = 0; i < pts.Count; i += stride) few.Add(pts[i]); pts = few; }
                if (pts.Count > 0) later.Add((tg, pts));
                gs = ge;
            }
            float bestScore = float.MaxValue; best = pos; bool found = false;
            foreach (var c in cands)
            {
                if (!Valid(c, boss) || !AllOk(c, rules, 0, g1End)) continue;
                float d = PathLen(pos, c, bp, boss.BodyRadius);
                float score = d + (d > reach1 ? 100 + d : 0);                 // unreachable: only if nothing else works
                if (t2 >= 0)
                {
                    float nd = 30; foreach (var q in next) nd = Mathf.Min(nd, PathLen(c, q, bp, boss.BodyRadius));
                    float secs2 = (t2 - t1) / (float)Sim.Hz;
                    // the second hop's slack: a dash's gain if one is left, else a walking margin (no dash, no slack)
                    float slack = UseDash && me.Stamina >= PlayerController.DashCost ? 1.5f : -0.5f;
                    score += 0.6f * nd + (nd > Speed * secs2 + slack ? 20 : 0);
                }
                float dashGain = UseDash && me.Stamina >= PlayerController.DashCost ? PlayerController.DashDistance : 0;
                foreach (var (tg, pts) in later)
                {
                    float nd = 30; foreach (var q in pts) nd = Mathf.Min(nd, PathLen(c, q, bp, boss.BodyRadius));
                    if (nd > Speed * (tg - t1) / (float)Sim.Hz + dashGain) score += 8;   // a soft pull: never worth standing in a live shape
                }
                // later rules still in view: a small pull toward satisfying them too
                // (a visible coral shape due soon costs far more than a memory rule seconds away)
                for (int i = g2End; i < rules.Count; i++) if (!rules[i].Ok(c)) score += rules[i].Memory ? 0.8f : rules[i].Tick - t1 < 45 ? 12f : 3f;
                score += BurnCost(boss, pos, c, UseDash && me.Stamina >= PlayerController.DashCost);
                // uptime: among spots that keep the rules, prefer ones in reach of the boss (a melee player weaves in)
                if (Attacks) score += UptimePull * Mathf.Max(0, Sim.Flat(c - bp).magnitude - boss.BodyRadius - Range(me));
                if (score < bestScore) { bestScore = score; best = c; found = true; }
            }
            return found;
        }
        static bool AllOk(Vector3 q, List<BotDemand> rules, int from, int to) { for (int i = from; i < to; i++) if (!rules[i].Ok(q)) return false; return true; }
        // walking distance from a to b round the boss's body (straight if the line clears it; else tangents + arc), so a
        // spot straight across him is planned as the detour it is
        static float PathLen(Vector3 a, Vector3 b, Vector3 bp, float body)
        {
            float r = body + PlayerController.Radius + 0.3f;
            Vector3 pa = Sim.Flat(a - bp), pb = Sim.Flat(b - bp), ab = pb - pa;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(-Vector3.Dot(pa, ab) / ab.sqrMagnitude);
            if ((pa + ab * t).magnitude >= r) return ab.magnitude;
            float da = Mathf.Max(pa.magnitude, r), db = Mathf.Max(pb.magnitude, r);
            float arc = Vector3.Angle(pa, pb) * Mathf.Deg2Rad - Mathf.Acos(r / da) - Mathf.Acos(r / db);
            return Mathf.Sqrt(da * da - r * r) + Mathf.Sqrt(db * db - r * r) + r * Mathf.Max(0, arc);
        }
        static bool Valid(Vector3 q, BossEncounter boss) =>
            CombatWorld.InPlayerBound(q, -0.25f) && Sim.Flat(q - boss.transform.position).magnitude >= boss.BodyRadius + PlayerController.Radius + 0.05f;
        static float BurnCost(BossEncounter boss, Vector3 from, Vector3 to, bool canDash)
        {
            float cost = 0; var w = (boss as BellkeeperEncounter)?.World; if (w == null) return 0;
            foreach (var h in w.Hazards.Live)
            {
                if (h.BurnPerSecond <= 0 || !h.Locked) continue;
                if (h.Contains(to)) cost += 40;
                for (int i = 1; i < 6; i++) if (h.Contains(Vector3.Lerp(from, to, i / 6f))) { cost += canDash ? 2.5f : 8f; break; }   // a crossing: ~4 HP dashed, ~14 HP walked (GUIDE.md), so a walk is worth a long way round
            }
            return cost;
        }

        // about to step into a locked burn that one dash (3 m) carries it out of
        static bool DashesOverBurn(CombatWorld w, Vector3 pos, Vector3 dir)
        {
            bool In(Vector3 q) { foreach (var h in w.Hazards.Live) if (h.BurnPerSecond > 0 && h.Locked && h.Contains(q)) return true; return false; }
            return !In(pos) && In(pos + dir * 0.6f) && !In(pos + dir * (PlayerController.DashDistance - 0.25f));
        }

        // this player's hourglass is up: it will be Stilled soon
        static bool FreezeSoon(BellkeeperEncounter boss, PlayerController me, int now)
        {
            if (boss.Ult == null) return false;
            var s = boss.Ult.Of(me);
            return s.HourglassFrom >= 0 && now >= s.HourglassFrom && now < s.StillFrom;
        }

        float Range(PlayerController me) => me.ClassName == "Vanguard" ? 0.9f : me.ClassName == "Chimesinger" ? 6.0f : 7.5f;

        void Attack(PlayerController me, ref PlayerInput inp, bool longCastOk)
        {
            if (tick - lastPress < 6) return;
            int s;
            switch (me.ClassName)
            {
                case "Vanguard":
                    if (cd[2] == 0) { s = 2; cd[2] = 600; } else if (cd[1] == 0) { s = 1; cd[1] = 240; } else s = 0;
                    break;
                case "Chimesinger":
                    if (cd[3] == 0) { s = 3; cd[3] = 480; } else if (cd[2] == 0) { s = 2; cd[2] = 300; } else if (cd[1] == 0) { s = 1; cd[1] = 150; } else s = 0;
                    break;
                default:   // Arcanist
                    if (cd[1] == 0) { s = 1; cd[1] = 360; } else if (cd[2] == 0 && longCastOk) { s = 2; cd[2] = 200; castHold = 50; } else if (cd[3] == 0) { s = 3; cd[3] = 420; } else s = 0;   // Arc Lance roots the caster: not with a rule close
                    break;
            }
            lastPress = tick;
            switch (s) { case 0: inp.P0 = inp.H0 = true; break; case 1: inp.P1 = true; break; case 2: inp.P2 = true; break; default: inp.P3 = true; break; }
        }

        static Vector3 AroundBoss(Vector3 pos, Vector3 move, BossEncounter b, Vector3? target = null)
        {
            var bp = Sim.Flat(b.transform.position); var to = Sim.Flat(pos - bp);
            float clear = b.BodyRadius + PlayerController.Radius + 0.3f;
            if (to.magnitude > clear + 1.5f) return move;
            // walking to a spot: only steer round him when the straight walk would cross his body's clearance (otherwise
            // any inward step within 3.5 m was turned into an orbit, and spots hugging the boss were never reached)
            if (target.HasValue)
            {
                var a = Sim.Flat(pos); var ab = Sim.Flat(target.Value) - a; float l2 = ab.sqrMagnitude;
                float t = l2 < 1e-4f ? 0 : Mathf.Clamp01(Vector3.Dot(bp - a, ab) / l2);
                if (Sim.Flat(a + ab * t - bp).magnitude >= clear) return move;
            }
            var ahead = Sim.Flat(pos + move.normalized * 0.8f) - bp;
            if (ahead.magnitude >= clear && Vector3.Dot(move, to) >= -0.2f) return move;
            var tan = Vector3.Cross(Vector3.up, to.normalized); if (Vector3.Dot(tan, move) < 0) tan = -tan;
            return (tan + to.normalized * 0.25f).normalized * move.magnitude;
        }
        static Vector2 ToScreen(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
    }

    // A party of strat bots for the Ultimate (Part 5): the run's own player plus scripted extras, all driven by UltimateBot.
    public static class UltimateParty
    {
        public static UltimateBot Drive(PlayerController p, int slot, int of, UltimateBot.Skill mode = UltimateBot.Skill.Perfect)
        {
            var b = new UltimateBot { Mode = mode, Slot = slot, Of = of };
            p.Input = b; return b;
        }
        public static List<UltimateBot> Fill(CombatWorld w, int size, UltimateBot.Skill mode = UltimateBot.Skill.Perfect, string[] classes = null)
        {
            var bots = new List<UltimateBot>();
            for (int i = w.Players.Count; i < size; i++)
            {
                var cls = classes != null ? classes[i % classes.Length] : RunDirector.Classes[i % RunDirector.Classes.Length];
                var p = ScriptedParty.Add(w, Sim.Dir(360f * i / size) * 6f, cls);
            }
            for (int i = 0; i < w.Players.Count; i++) bots.Add(Drive(w.Players[i], i, w.Players.Count, mode));
            return bots;
        }
    }
}
