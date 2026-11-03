using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // A bot ally (RunParty): fights the boss with its class kit through ordinary PlayerInput, like a human would.
    // - Positions: melee hugs the boss; casters keep their range. Each ally takes its own angle round the boss so the
    //   party spreads out (spread / mirror / clock mechanics need that).
    // - Dodging: when it stands in a hazard that locked at least its reaction time ago (0.25-0.4 s, varied per bot),
    //   it heads for the nearest spot outside every live hazard, dashing or sprinting only when walking is too slow.
    //   No cheats: it sees exactly the telegraphs a player sees, and has no immunity.
    // - Rotation: a simple per-class priority list, OFF by default (AllyBot.Attacks): Chzy18 wants bots that do the
    //   mechanics without fighting, so they never shorten the fight.
    // Test modes (Aurelion v3, HARDER-PROMPT §7; the run menu always uses Passive):
    // - Reflex: reflex-perfect and memory-blind. It reacts on the tick any hostile shape appears (amber included, not at
    //   lock), dashing and sprinting, enters any visible soak, drinks potions, and ignores numerals, marks and in/out cues.
    // - Perfect: the assignment-follower. Reflex dodging plus perfect knowledge of Aurelion's memory mechanics: it walks
    //   to the next Ecliptic Order anchor as soon as the previous strike lands and keeps its Zenith Mark's side.
    public enum BotMode { Passive, Reflex, Perfect }

    public sealed class AllyBot : IInputSource
    {
        readonly int slot, of;            // this ally's index and the ally count (for its angle round the boss)
        readonly int reactTicks;
        public readonly BotMode Mode;
        // tests: what it decided last tick; only formatted when read, so a tick stores plain values and makes no strings
        public string DebugLine => dMe == null ? null : Describe();
        public float? HoldRange;          // tests: keep this gap to the boss instead of the class's range (8.5 = a ranged player)
        // bots are mechanics dummies by default: they move, dodge and spread, but never attack or heal. Launch switch
        // -lastbellAllyAttack turns their class rotation on (the boss then scales his HP for them, BossEncounter.Spawn).
        public static bool Attacks = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellAllyAttack", System.StringComparison.OrdinalIgnoreCase));
        public static BotMode AllyMode = BotMode.Perfect;   // menu allies do every mechanic, memory ones included (Chzy18: "do the mechanics"); still no attacks
        int tick, lastPress = -999, castHold;
        int pendingSlot = -1, pendingUntil;
        readonly int[] skipUntil = new int[6];

        public AllyBot(int index, int count, BotMode mode = BotMode.Passive)
        {
            slot = index; of = Mathf.Max(1, count); Mode = mode;
            reactTicks = mode == BotMode.Passive ? 15 + (index * 7) % 10 : 0;   // 0.25-0.4 s; test bots react on the first tick
        }

        float Range(PlayerController me) => HoldRange ?? (me.ClassName == "Tollbreaker" ? 3.5f : me.ClassName == "Chimesinger" ? 6.5f : 8.5f);

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss; readWorld = w;
            Vector3 pos = me.transform.position, want;
            if (boss == null || !boss.Solid) { inp.Move = ToScreen(Vector3.zero); return inp; }
            // the target: the boss, or while boss parts stand (Crucivar's castings, which must die before the Pour Flood) the
            // first of them by name: the castings carry their numerals, and on Hard one sunk out of order sets every turret
            // firing (a party went down to it, 11-03); hits land on whichever of them is nearest, so aim and reach follow it
            var bpos = Sim.Flat(boss.transform.position); float brad = boss.BodyRadius;
            BossPart first = null;
            foreach (var part in w.Parts)
                if (part.Alive && (first == null || string.CompareOrdinal(part.Name, first.Name) < 0)) first = part;
            if (first != null) { bpos = Sim.Flat(first.Pos); brad = first.BodyRadius; }
            tPos = bpos; tRad = brad;
            inp.Aim = bpos;
            BotSight.Update(w, boss);
            if (Scripted(me, boss, pos, ref inp)) return inp;

            // 1) dodge: in a hazard that locked at least our reaction time ago (test bots: from its first tick) -> nearest
            // clear spot. A Perfect bot also heads for its memory job (next anchor, mark side) as soon as it is due.
            var live = w.Hazards.Live;
            bad = Bad(me, boss);
            Hazard threat = null; int left = int.MaxValue;
            for (int i = 0; i < live.Count; i++)   // indexed: a foreach over the IReadOnlyList allocates its enumerator
            {
                var h = live[i];
                int lands = Lands(h, boss);
                if (h.Age >= (Mode == BotMode.Passive ? Mathf.Max(h.LockAt, 0) + reactTicks : 0) && Hits(h, me, pos) && lands < left) { threat = h; left = lands; }
            }
            // an orb heading here, or a strike still rolling where its warning was
            if (threat == null && (BotSight.OrbHits(pos, OrbAhead, OrbPad) || BotSight.GhostHits(me, pos))) { threat = Unseen; left = 20; }
            int duty = Duty(me, boss);   // ticks until its job resolves, or -1
            if (threat != null && !bad(pos)) threat = null;   // a shape that lands after its job: not in the way yet
            // dodge whatever lands first: shapes landing more than 0.5 s after it are not in the way yet (Perfect / Reflex only)
            if (threat != null && Mode != BotMode.Passive) bad = Bad(me, boss, false, w.Tick + left);
            // attacking bots keep swinging until the escape (walk, plus a root still running) leaves 0.75 s to spare, and plan
            // round only the shapes landing within 1.25 s; they look again every tick
            if (Attacks && Mode != BotMode.Passive)
            {
                if (threat != null)
                {
                    NearestClear(pos, boss, bad, out float esc);
                    if (esc < 90 && left > (esc / PlayerController.WalkSpeed + 0.75f + (me.Kit.MoveScale < 0.5f ? 0.45f : 0f)) * Sim.Hz) threat = null;
                }
                if (threat == null) bad = Bad(me, boss, false, w.Tick + 45);
            }
            // attacking bots keep swinging until the walk to their job (tower, ring, mark, rim) leaves 1.25 s to spare, then go
            // (not with a Sunspot: its blast locks on the carrier 1.5 s before it lands, so it goes to the rim at once)
            if (threat == null && duty >= 0 && Attacks && bad(pos) && (boss as AurelionEncounter)?.Spots?.NextFor(me, w.Tick) == null)
            {
                NearestClear(pos, boss, bad, out float to);
                if (to < 90 && (to / PlayerController.WalkSpeed + 1.25f) * Sim.Hz < duty) { bad = Bad(me, boss, jobs: false); duty = -1; }
            }
            if (threat == null && duty >= 0 && bad(pos)) left = duty;
            Vector3 move;
            if (threat != null || (duty >= 0 && bad(pos)))
            {
                move = NearestClear(pos, boss, bad, out float need);
                if (move == Vector3.zero && need > 90) { bad = Bad(me, boss, true, threat != null ? w.Tick + left : int.MaxValue); move = NearestClear(pos, boss, bad, out need, false); }   // no clean spot: take the burn
                float t = left / (float)Sim.Hz;
                // attacking: dodge into a clear spot still in reach of him when it can be reached in time (walking, a sprint at most)
                if (Attacks && move != Vector3.zero && AttackSpot(me, boss, out var keep))
                {
                    float d = Sim.Flat(keep - pos).magnitude;
                    if (d / PlayerController.SprintSpeed < t - 0.25f && d > 0.05f) { move = Sim.Flat(keep - pos).normalized; need = d; }
                }
                // rooted in an attack (walking does nothing until it ends): count up to 0.45 s of it lost, so it dashes out sooner
                if (me.Kit.MoveScale < 0.5f) t -= 0.45f;
                if (move != Vector3.zero)
                {
                    if (need / PlayerController.WalkSpeed > t - 0.1f)
                    {
                        if (need > 2.2f && me.Stamina > 20) inp.Sprint = true;
                        // dash only when its 3 m landing is clear too (it overshoots a near spot into the next shape)
                        if (!me.Dashing && me.Stamina >= PlayerController.DashCost && need / PlayerController.SprintSpeed > t - 0.15f && !bad(pos + move * 3f)) inp.Dash = true;
                    }
                }
                // casters: the basic casts on the move (0.55-0.6 speed), so keep it going while repositioning (following a
                // turning well, a far job) when nothing lands yet or the slower walk still has time to spare
                if (Attacks && !inp.Dash && !me.Kit.Acting && tick - lastPress >= 6 && me.Kit.Cooldown01(0) <= 0.001f
                    && Sim.Flat(bpos - pos).magnitude - brad <= Range(me) + 1.85f
                    && (threat == null || move == Vector3.zero || need / (PlayerController.WalkSpeed * 0.55f) < t - 0.3f))
                { inp.P0 = inp.H0 = true; lastPress = tick; }
                castHold = 0;
            }
            else
            {
                // 2) hold a position round the boss at this class's range, on this ally's own angle
                float ang = 360f * slot / of + 30f;
                float dist = brad + Range(me);
                want = bpos + Sim.Dir(ang) * dist;
                // Total Eclipse: hold the edge of the nearest light well closest to him (melee still reaches from there)
                if (boss is AurelionEncounter ae && ae.Eclipse != null && ae.Eclipse.Active && ae.Eclipse.Wells.Count > 0)
                {
                    float best = float.MaxValue;
                    foreach (var c in ae.Eclipse.Wells)
                    {
                        var edge = c + Sim.Flat(bpos - c).normalized * (ae.Eclipse.WellRadius - 0.5f);
                        float d = Sim.Flat(edge - pos).sqrMagnitude; if (d < best) { best = d; want = edge; }
                    }
                }
                // attacking: the nearest spot in reach of him that every job and telegraph allows (inner disc edge, well edge, ...)
                if (Attacks && AttackSpot(me, boss, out var spot)) { want = spot; spotNote = 1; dSpot = spot; } else spotNote = 2;
                var flat = new Vector2(want.x, want.z); float lim = CombatWorld.PlayerBound - 0.8f;
                if (flat.magnitude > lim) { flat = flat.normalized * lim; want = new Vector3(flat.x, 0, flat.y); }
                var v = Sim.Flat(want - pos);
                move = v.magnitude < (Attacks ? 0.15f : 0.4f) || castHold > 0 ? Vector3.zero : v.normalized;
                // never walk into a live telegraph
                if (move != Vector3.zero && bad(pos + move * 0.4f) && !bad(pos)) { move = Bend(pos, move); if (bad(pos + move * 0.4f)) move = Vector3.zero; }   // round it if a turned step is clear
                if (castHold > 0) castHold--;
                // 3) attack when in range (off: Chzy18 2026-10-22, "the bots are doing too much damage": they only do the
                // mechanics). Set Attacks = true to bring the rotation back.
                float gap = Sim.Flat(bpos - pos).magnitude - brad;
                if (Attacks && gap <= Range(me) + 1.85f) Attack(me, ref inp);
            }
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;   // test bots: before a Hard kill hit (65) could down them
            // walk round his body, not into it: a straight line across the centre would pin the bot against him
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            dMe = me; dDuty = duty; dThreat = threat?.Source; dBad = bad(pos); dMove = move; dPos = pos; dNote = spotNote; spotNote = 0;
            inp.Move = ToScreen(move);
            return inp;
        }

        // Bosses whose mechanics are not floor shapes, played from what the screen shows (the Dial Warden: the lit hours and
        // their key prompts, the safe circle while the hand sweeps, the Mainspring gaps, the Scales' mirror circle). True when
        // it set this tick's input.
        bool Scripted(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            if (!(boss is AurelionEncounter) && Quake(me, boss, pos, ref inp)) return true;
            if (!(boss is AurelionEncounter) && Brand(me, boss, pos, ref inp)) return true;   // Aurelion keeps its own Sunspot job
            if (!(boss is AurelionEncounter) && Spokes(me, boss, pos, ref inp)) return true;
            if (boss is ChimeboundOracleEncounter or) return Carillon(me, or, pos, ref inp);
            if (boss is PendulumSentinelEncounter ps) return Level(me, ps, pos, ref inp);
            if (!(boss is DialWardenEncounter dw)) return false;
            if (!dw.QteSources.ContainsKey(me)) dw.QteSources[me] = new BotQte(dw, me);   // a test's scripted source stays
            Vector3? goal = null; bool still = true;
            if (dw.LapActive && !dw.Breaking)
            {
                var pad = PadFor(dw, me);
                // its lit hour, or inside the safe circle round him (clear of his body) while the hand sweeps
                goal = pad != null ? pad.Pos : (Vector3?)SafeRing(pos);
            }
            else if (Mainspring(dw, me, pos, out var cell, out still)) goal = cell;
            else if (dw.ScalesActive && dw.Throw > 0 && dw.ThrowLog.Count > 0) goal = dw.ThrowLog[dw.ThrowLog.Count - 1].mirror;
            if (goal == null) return false;
            bad = Bad(me, boss);
            var v = Sim.Flat(goal.Value - pos);
            var move = v.magnitude < 0.12f ? Vector3.zero : v.normalized;
            if (v.magnitude > 3f && me.Stamina > 30) inp.Sprint = true;
            // swing only while the route holds still (a planted throw would leave it behind)
            float gap = Sim.Flat(boss.transform.position - pos).magnitude - boss.BodyRadius;
            if (Attacks && still && move == Vector3.zero && gap <= Range(me) + 1.85f) Attack(me, ref inp);
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            inp.Move = ToScreen(move);
            return true;
        }

        // Carillon Chain: the standing player nearest the lit circle walks into it and holds there; a lane or orb in the
        // way is dodged first (the normal reading), and nobody else changes what they do
        bool Carillon(PlayerController me, ChimeboundOracleEncounter or, Vector3 pos, ref PlayerInput inp)
        {
            if (!or.ChainActive || or.ChainIndex < 0 || or.ChainIndex >= or.ChainPositions.Count) return false;
            var c = Sim.Flat(or.ChainPositions[or.ChainIndex]);
            PlayerController near = null; float nd = float.MaxValue;
            foreach (var p in me.World.Players) { if (!p || p.Down) continue; float d = Sim.Flat(p.transform.position - c).sqrMagnitude; if (d < nd) { nd = d; near = p; } }
            if (near != me) return false;
            bad = Bad(me, or);
            if (bad(pos)) return false;
            var v = c - Sim.Flat(pos);
            var move = v.magnitude < ChimeboundOracleEncounter.SoakRadius - 0.5f ? Vector3.zero : v.normalized;
            if (move != Vector3.zero && bad(pos + move * 0.6f)) return false;
            if (v.magnitude > 3f && me.Stamina > 30) inp.Sprint = true;
            float gap = Sim.Flat(or.transform.position - pos).magnitude - or.BodyRadius;
            if (Attacks && move == Vector3.zero && gap <= Range(me) + 1.85f) Attack(me, ref inp);
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            if (move != Vector3.zero) move = AroundBoss(pos, move, or);
            inp.Move = ToScreen(move);
            return true;
        }

        // Plumb Line: everyone outside a level mark is hit, and a mark holds two. Players in turn take the nearest mark with
        // room (the marks drift, so the bot follows its mark's centre); nothing else is live while the pendulum stands still
        bool Level(PlayerController me, PendulumSentinelEncounter ps, Vector3 pos, ref PlayerInput inp)
        {
            var marks = ps.LiveMarks;
            if (!ps.PlumbActive || marks == null || marks.Count == 0) return false;
            var room = new int[marks.Count]; int mine = -1;
            foreach (var p in me.World.Players)
            {
                if (!p || p.Down) continue;
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < marks.Count; i++)
                {
                    if (room[i] >= PendulumSentinelEncounter.Capacity) continue;
                    float d = Sim.Flat(p.transform.position - marks[i]).sqrMagnitude; if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) continue;
                room[best]++;
                if (p == me) { mine = best; break; }
            }
            if (mine < 0) return false;
            var v = Sim.Flat(marks[mine] - pos);
            var move = v.magnitude < 0.15f ? Vector3.zero : v.normalized;
            if (v.magnitude > 3f && me.Stamina > 30) inp.Sprint = true;
            float gap = Sim.Flat(ps.transform.position - pos).magnitude - ps.BodyRadius;
            if (Attacks && move == Vector3.zero && gap <= Range(me) + 1.85f) Attack(me, ref inp);
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            if (move != Vector3.zero) move = AroundBoss(pos, move, ps);
            inp.Move = ToScreen(move);
            return true;
        }

        // a mark that follows this player and spares them (Crucivar's Ember Brand) leaves a burning pool where it lands: walk
        // it out to a ring 12.5 m from the centre, clear of other pools and other players, before it stops following; a threat on the way
        // is dodged first (the normal reading)
        // bands round one centre that land in turn (Crucivar's Anvil Quake and Hard Quake Echo): the plain dodge leaves the
        // band landing next for the one after it. Plan the distance from the centre for every landing still shown (each a
        // hand clear of its band edge, walking pace between them) and walk the first leg; a brand carrier ends far out
        static readonly List<Hazard> quakeBands = new List<Hazard>();
        const float QuakePad = 0.5f, QuakeStep = 0.25f, QuakePace = 6.5f;
        bool Quake(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            var live = me.World.Hazards.Live; quakeBands.Clear();
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h.Kind == HazardKind.Strike && (h.Shape == HazardShape.Ring || h.Shape == HazardShape.Circle) && h.Track == null && h.Damage > 0 && !h.Cancelled
                    && h.Age < h.ResolveAt && h.Radius >= 3f && (quakeBands.Count == 0 || Sim.Flat(h.Origin - quakeBands[0].Origin).magnitude < 0.1f)) quakeBands.Add(h);
            }
            if (quakeBands.Count < 2) return false;
            quakeBands.Sort((x, y) => (x.ResolveAt - x.Age).CompareTo(y.ResolveAt - y.Age));
            var hub = Sim.Flat(quakeBands[0].Origin); var off = Sim.Flat(pos - hub);
            float r0 = off.magnitude, yaw = r0 > 0.1f ? Sim.YawTo(hub, pos, 0) : 0f;
            float rMin = boss.BodyRadius + PlayerController.Radius + 0.15f, rMax = CombatWorld.PlayerBound - 0.4f - hub.magnitude;
            int n = Mathf.FloorToInt((rMax - rMin) / QuakeStep) + 1;
            bool brand = false;
            for (int i = 0; i < live.Count && !brand; i++) { var h = live[i]; if (h.Track != null && h.Age < h.LockAt && h.Shape == HazardShape.Circle && Spares(h, me)) brand = true; }
            float pref = brand ? BrandRing : 6.5f;
            var dir = Sim.Dir(yaw);
            bool Clear(Hazard h, float r) => !h.Contains(hub + dir * r) && !h.Contains(hub + dir * (r + QuakePad)) && !h.Contains(hub + dir * Mathf.Max(0, r - QuakePad));
            // cost[j]: the shortest walk that stands at rMin + j * step for every landing so far; from[i][j] the step before
            var cost = new float[n]; var next = new float[n]; var from = new int[quakeBands.Count, n];
            int prevAt = 0;
            for (int i = 0; i < quakeBands.Count; i++)
            {
                var h = quakeBands[i]; int at = h.ResolveAt - h.Age;
                float reach = QuakePace * ((at - prevAt) / (float)Sim.Hz - (i == 0 ? 0.15f : 0f));
                for (int j = 0; j < n; j++)
                {
                    float r = rMin + j * QuakeStep; next[j] = float.MaxValue; from[i, j] = -1;
                    if (!Clear(h, r)) continue;
                    if (i == 0) { float d = Mathf.Abs(r - r0); if (d <= reach || at == 0 && d < 0.01f) next[j] = d; continue; }
                    int lo = Mathf.Max(0, j - Mathf.FloorToInt(reach / QuakeStep)), hi = Mathf.Min(n - 1, j + Mathf.FloorToInt(reach / QuakeStep));
                    for (int k = lo; k <= hi; k++)
                        if (cost[k] < float.MaxValue && cost[k] + Mathf.Abs(r - rMin - k * QuakeStep) < next[j]) { next[j] = cost[k] + Mathf.Abs(r - rMin - k * QuakeStep); from[i, j] = k; }
                }
                var swap = cost; cost = next; next = swap; prevAt = at;
            }
            int best = -1; float bestCost = float.MaxValue;
            for (int j = 0; j < n; j++) { if (cost[j] == float.MaxValue) continue; float c = cost[j] + 0.3f * Mathf.Abs(rMin + j * QuakeStep - pref); if (c < bestCost) { bestCost = c; best = j; } }
            if (best < 0) return false;   // no plan in reach: the plain dodge
            for (int i = quakeBands.Count - 1; i > 0; i--) best = from[i, best];
            float rGoal = rMin + best * QuakeStep;
            float t0 = (quakeBands[0].ResolveAt - quakeBands[0].Age) / (float)Sim.Hz;
            // the bearing: our own, or the nearest one clear of burning ground (and, with brands out, of the other players)
            Vector3 goal = hub + dir * rGoal;
            for (int k = 0; k < 13; k++)
            {
                var q = hub + Sim.Dir(yaw + (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 10f) * rGoal;
                // the walk there too: a radial walk inside a Pour Channel (a lane out from the same centre) burns all the way
                bool ok = k == 0 || Sim.Flat(q - pos).magnitude <= QuakePace * (t0 - 0.15f);
                for (int i = 0; i < live.Count && ok; i++)
                {
                    var h = live[i]; if (h.BurnPerSecond <= 0) continue;
                    if (h.Contains(q) || h.Contains(Vector3.Lerp(pos, q, 0.35f)) || h.Contains(Vector3.Lerp(pos, q, 0.7f))) ok = false;
                }
                if (ok && brand) foreach (var p in me.World.Players) if (ok && p && p != me && !p.Down && Sim.Flat(p.transform.position - q).magnitude < BrandRadiusPad) ok = false;
                if (ok) { goal = q; break; }
            }
            var v = Sim.Flat(goal - pos);
            var move = v.magnitude < 0.2f ? Vector3.zero : v.normalized;
            bad = Bad(me, boss, false, me.World.Tick + 1);   // round burning ground; the bands are planned above
            if (move != Vector3.zero && bad(pos + move * 0.6f)) move = Bend(pos, move);
            if (v.magnitude / PlayerController.WalkSpeed > t0 - 0.3f && me.Stamina > 10) inp.Sprint = true;
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            inp.Move = ToScreen(move);
            // in place with time to spare: keep throwing
            if (Attacks && v.magnitude < 0.5f && t0 > 0.5f && Sim.Flat(boss.transform.position - pos).magnitude - boss.BodyRadius <= Range(me) + 1.85f) Attack(me, ref inp);
            return true;
        }
        const float BrandRadiusPad = 4f;   // a brand (3 m) and a hand: brand carriers keep this far from other players

        bool Brand(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            Hazard brand = null; var live = me.World.Hazards.Live;
            for (int i = 0; i < live.Count && brand == null; i++)
            {
                var h = live[i];
                if (h.Track != null && h.Age < h.LockAt && h.Shape == HazardShape.Circle && Spares(h, me)) brand = h;
            }
            if (brand == null) return false;
            bad = Bad(me, boss);
            if (bad(pos)) return false;
            // Backdraft's pull (2 m inward just before the brand freezes) would leave the pool mid-arena: sprint straight out
            // against it (9 m/s against 5)
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h.AttackId != "crucivar.backdraft" || h.Age < Sim.Ticks(ForgeRite.PullFrom - 0.3f) || h.Age > Sim.Ticks(ForgeRite.PullTo)) continue;
                var outward = Sim.Flat(pos - h.Origin);
                if (outward.magnitude < 0.1f || outward.magnitude > CombatWorld.PlayerBound - 0.8f) break;
                var o = outward.normalized; if (bad(pos + o * 0.6f)) o = Bend(pos, o);
                inp.Move = ToScreen(o); if (me.Stamina > 5) inp.Sprint = true;
                return true;
            }
            float rim = Mathf.Min(BrandRing, CombatWorld.PlayerBound - 1.2f), mine = Sim.YawTo(Vector3.zero, pos, 0);   // not the wall: back in to the jet gaps within the 1 s freeze
            Vector3 goal = pos; float bestCost = float.MaxValue;
            for (int k = 0; k < 24; k++)
            {
                float yaw = mine + (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 15f;
                var q = Sim.Dir(yaw) * rim;
                if (!CombatWorld.InPlayerBound(q, -0.3f)) q = Sim.Dir(yaw) * (rim - 2f);
                bool ok = true;
                for (int i = 0; i < live.Count && ok; i++) { var h = live[i]; if ((h.BurnPerSecond > 0 || Jet(h)) && (h.Contains(q) || (Jet(h) && h.Contains((q + pos) / 2f)))) ok = false; }
                foreach (var p in me.World.Players) if (ok && p && p != me && !p.Down && Sim.Flat(p.transform.position - q).magnitude < brand.Radius + 1f) ok = false;
                if (!ok) continue;
                float c = Sim.Flat(q - pos).magnitude;
                if (c < bestCost) { bestCost = c; goal = q; }
            }
            var v = Sim.Flat(goal - pos);
            var move = v.magnitude < 0.3f ? Vector3.zero : v.normalized;
            if (move != Vector3.zero && bad(pos + move * 0.6f)) move = Bend(pos, move);
            if (v.magnitude > 2f && me.Stamina > 30) inp.Sprint = true;
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            inp.Move = ToScreen(move);
            return true;
        }

        // jets turning round one hub (Crucivar's Furnace Spin) reach the wall, where they outrun a sprint: ride the middle of
        // the nearest gap about 6.5 m out, turning with them (shown jets first, the turn read from their yaw)
        Hazard spokeRef; float spokeYaw, spokeDir;
        static readonly float[] SpokeRadii = { 6.5f, 5.6f, 7.4f, 4.8f, 8.3f };   // near the hub a jet covers too wide an angle, far out the gap outruns a walk
        static readonly float[] SpokeShifts = { 0f, 8f, -8f, 14f, -14f };   // degrees off the gap's middle
        const float BrandRing = 12.5f;   // where a brand is walked to: its pool (2 m) stays off the jet ring and leaves the inside of the quake's outer band
        bool Spokes(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            var live = me.World.Hazards.Live; var jets = new List<Hazard>();
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (Jet(h)
                    && (jets.Count == 0 || Sim.Flat(h.Origin - jets[0].Origin).magnitude < 0.1f)) jets.Add(h);
            }
            if (jets.Count < 3) { spokeRef = null; return false; }
            var j0 = jets[0];
            if (spokeRef != j0) { spokeRef = j0; spokeYaw = j0.Yaw; spokeDir = 0; }
            else { float dy = Mathf.DeltaAngle(spokeYaw, j0.Yaw); if (Mathf.Abs(dy) > 0.01f) spokeDir = Mathf.Sign(dy); spokeYaw = j0.Yaw; }
            var hub = Sim.Flat(j0.Origin);
            var yaws = new List<float>(); foreach (var h in jets) yaws.Add(Mathf.Repeat(h.Yaw, 360f));
            yaws.Sort();
            // a spot in a gap clear of burning pools and channels; once the jets turn, only the gap this player is in (the
            // others are across a live jet), and a burning spot there beats leaving it
            bool turning = j0.Age >= j0.ResolveAt;
            float mine = Sim.YawTo(hub, pos, 0);
            Vector3 goal = Vector3.zero; float bestCost = float.MaxValue;
            for (int pass = 0; pass < 2 && bestCost == float.MaxValue; pass++)
                for (int k = 0; k < yaws.Count; k++)
                {
                    float a = yaws[k], b = k + 1 < yaws.Count ? yaws[k + 1] : yaws[0] + 360f, mid = (a + b) / 2f + spokeDir * 6f;
                    if (turning && Mathf.Repeat(mine - a, 360f) > b - a) continue;
                    foreach (float r in SpokeRadii)
                    foreach (float da in SpokeShifts)
                    {
                        // off the gap's middle (round a pool there) only while the shifted spot keeps a hand clear of both jets
                        float at = mid + da;
                        var q = hub + Sim.Dir(at) * r;
                        bool ok = CombatWorld.InPlayerBound(q, -0.3f);
                        for (int i = 0; i < jets.Count && ok && da != 0; i++) if (jets[i].Contains(q) || jets[i].Contains(hub + Sim.Dir(at + Mathf.Sign(da) * 5f) * r)) ok = false;
                        for (int i = 0; i < live.Count && ok && pass == 0; i++) { var h = live[i]; if ((h.BurnPerSecond > 0 || (h.Track != null && h.Includes != null && h.Age >= h.LockAt)) && (h.Contains(q) || h.Contains(hub + Sim.Dir(at + spokeDir * 12f) * r))) ok = false; }
                        if (!ok) continue;
                        float c = Sim.Flat(q - pos).magnitude + Mathf.Abs(r - 6.5f) + Mathf.Abs(da) * 0.05f;
                        if (c < bestCost) { bestCost = c; goal = q; }
                    }
                }
            if (bestCost == float.MaxValue) return false;
            var v = Sim.Flat(goal - pos);
            var move = v.magnitude < 0.3f ? Vector3.zero : v.normalized;
            bad = Bad(me, boss);
            if (move != Vector3.zero && bad(pos + move * 0.6f)) move = Bend(pos, move);   // round a pool rather than back to the plain reading, which the turning jets beat
            if (v.magnitude > 2f && me.Stamina > 30) inp.Sprint = true;
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            inp.Move = ToScreen(move);
            return true;
        }

        // ticks until a shape hurts: a strike when it lands, burning ground once it has heated (a pool that lasts until the
        // phase ends burns now, not then), and a strike already live (a flying bullet, a turning jet) now: a negative count
        // made the oldest bullet the threat and left every younger one out of the dodge (a bot stepped into the next one in
        // the same Lane Stream, 11-03). Aurelion keeps the landing time its tested routes were built on
        static int Lands(Hazard h, BossEncounter boss) => boss is AurelionEncounter ? h.ResolveAt - h.Age
            : h.BurnPerSecond > 0 ? Mathf.Max(0, h.LockAt - h.Age) : Mathf.Max(0, h.ResolveAt - h.Age);

        static bool Jet(Hazard h) => h.Shape == HazardShape.Lane && h.Track != null && h.ActiveTicks > 0 && h.Damage > 0 && !h.Cancelled && h.Age < h.ResolveAt + h.ActiveTicks;

        // the first of a few turned steps that is not bad, else the step as it was
        Vector3 Bend(Vector3 pos, Vector3 move)
        {
            foreach (float turn in new[] { 35f, -35f, 70f, -70f })
            {
                var alt = Quaternion.Euler(0, turn, 0) * move;
                if (!bad(pos + alt * 0.6f)) return alt;
            }
            return move;
        }

        static Vector3 SafeRing(Vector3 pos)
        {
            var d = Sim.Flat(pos); float r = (DialWardenRoutes.BodyClear + DialWardenRoutes.SafeRadius - Hazard.Margin) / 2f;
            return (d.magnitude > 0.05f ? d.normalized : Vector3.forward) * r;
        }

        // the lit hour this bot takes: unmanned hours in the hand's order, each to the nearest free standing player
        static DialWardenEncounter.Lit PadFor(DialWardenEncounter dw, PlayerController me)
        {
            foreach (var l in dw.LitList) if (l.Player == me && !l.Resolved) return l;
            var used = new HashSet<PlayerController>();
            foreach (var l in dw.LitList) if (l.Player != null && !l.Resolved) used.Add(l.Player);
            var open = new List<DialWardenEncounter.Lit>();
            foreach (var l in dw.LitList) if (!l.Opened && !l.Resolved) open.Add(l);
            open.Sort((a, b) => a.Step.CompareTo(b.Step));
            foreach (var l in open)
            {
                PlayerController best = null; float bd = float.MaxValue;
                foreach (var p in me.World.Players)
                {
                    if (!p || p.Down || used.Contains(p)) continue;
                    float d = Sim.Flat(p.transform.position - l.Pos).sqrMagnitude; if (d < bd) { bd = d; best = p; }
                }
                if (best == null) return null;
                if (best == me) return l;
                used.Add(best);
            }
            return null;
        }

        // Mainspring: one walk through every wave on the floor (the boss's own route solver, run on the gaps it shows),
        // planned again whenever a wave is launched or a Constellation path is set to fire. still: the route stays on this cell for the next few steps.
        static CombatWorld planWorld; static int planWaves = -1, planFires = -1, planT0; static DialWardenRoutes.Grid plan;
        readonly Dictionary<int, List<Vector3>> routes = new Dictionary<int, List<Vector3>>(); int routeFor = -1;
        bool Mainspring(DialWardenEncounter dw, PlayerController me, Vector3 pos, out Vector3 cell, out bool still)
        {
            cell = pos; still = true;
            int now = dw.Tick;
            if (planWorld != me.World || planWaves != dw.WaveLog.Count || planFires != dw.FireStart)
            {
                planWorld = me.World; planWaves = dw.WaveLog.Count; planFires = dw.FireStart; planT0 = now; plan = null;
                var tl = new DialWardenRoutes.Timeline();
                foreach (var (_, w, origin) in dw.WaveLog)
                    if (origin + w.End >= now)
                        tl.Waves.Add(new DialWardenRoutes.Wave { Start = w.Start + origin - now, OutGaps = w.OutGaps, InGaps = w.InGaps, GapHalf = w.GapHalf, VOut = w.VOut, VIn = w.VIn, ImplosionDelay = w.ImplosionDelay });
                // a Constellation path still to fire, remembered from its lines (on Hard the beams flash only as they strike)
                if (dw.FireStart >= 0 && dw.Gold != null)
                    foreach (var f in DialWardenRoutes.Fires(dw.Gold, dw.Silver, dw.BeatTicks))
                        if (dw.FireStart + f.Tick >= now) tl.Fires.Add(new DialWardenRoutes.Fire { Tick = dw.FireStart + f.Tick - now, A = f.A, B = f.B, Silver = f.Silver });
                if (tl.Waves.Count > 0 || tl.Fires.Count > 0) plan = DialWardenRoutes.Solve(tl);
            }
            if (plan == null || now - planT0 >= plan.Steps * DialWardenRoutes.StepTicks) return false;
            if (routeFor != planT0) { routes.Clear(); routeFor = planT0; }
            if (!routes.TryGetValue(planT0, out var path)) { path = DialWardenRoutes.Plan(plan, pos); routes[planT0] = path; }
            if (path == null || path.Count == 0) return false;   // nothing survivable (Midnight): fall back to dodging
            int s = (now - planT0) / DialWardenRoutes.StepTicks;
            int at = Mathf.Min(s + 1, path.Count - 1);
            cell = path[at];
            for (int k = at; k <= Mathf.Min(at + 3, path.Count - 1); k++) if (Sim.Flat(path[k] - path[at]).sqrMagnitude > 0.01f) still = false;
            return true;
        }

        // the parry prompt, keyed like a player reading it: a direction every 0.1 s, then E on the hand's arrival
        sealed class BotQte : IQteSource
        {
            readonly DialWardenEncounter boss; readonly PlayerController me; int lastPress = -999;
            public BotQte(DialWardenEncounter b, PlayerController p) { boss = b; me = p; }
            public void Begin() { lastPress = -999; }
            public void Drain(List<QteKey> into)
            {
                var q = boss ? boss.PromptFor(me, out _) : null;
                if (q == null || !q.Open) return;
                int now = boss.Tick;
                if (!q.OnParry) { if (now - lastPress >= 6 && now - q.OpenTick >= 8) { into.Add(q.Sequence[q.Index]); lastPress = now; } return; }
                if (now >= q.ArriveTick) into.Add(QteKey.Parry);
            }
        }

        PlayerController dMe; int dDuty, dNote; string dThreat; bool dBad; Vector3 dMove, dPos;
        string Describe()
        {
            var s = $"duty {dDuty} threat {dThreat ?? "-"} bad {dBad} move ({dMove.x:0.0},{dMove.z:0.0}) at ({dPos.x:0.0},{dPos.z:0.0})"
                + (dNote == 1 ? $" spot ({dSpot.x:0.0},{dSpot.z:0.0})" : dNote == 2 ? " no spot" : "");
            var dtw = (dMe.World.Boss as AurelionEncounter)?.Towers?.NextFor(dMe, dMe.World.Tick);
            if (dtw != null && bad != null) { var c = dtw.At + (dPos - dtw.At).normalized * 1.0f; s += $" | tower spot ok {!bad(c)} covered by [{string.Join("/", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(dMe.World.Hazards.Live, h => h.Contains(c)), h => h.Source + (h.BurnPerSecond > 0 ? "(burn)" : "")))}]"; }
            return s;
        }

        void Attack(PlayerController me, ref PlayerInput inp)
        {
            // Arcanist: keep Disintegrate held on him until its channel ends, unless something is about to land here or it's
            // standing where it mustn't (a burning pool, off its job): the beam slows it to 0.2, so let go and walk
            if (me.Kit is ArcanistKit ak && ak.BeamActive) { if (Clear(me, 30) && (bad == null || !bad(me.transform.position))) inp.H1 = true; return; }
            if (tick - lastPress < 6) return;
            // the kit's own cooldowns (the reference policy; a private counter only ticked while attacking)
            System.Func<int, bool> ready = slot => me.Kit.Cooldown01(slot) <= 0.001f && tick >= skipUntil[slot];
            // a pressed ability's cooldown starts on release: wait for it (up to 1.5 s) instead of pressing again and restarting the
            // cast; if it never started (the cast didn't take: no target, a missing condition), skip that ability for 8 s
            // (casters press only when idle, so an action starting counts too: charge abilities show no cooldown while charges remain)
            if (pendingSlot > 0 && me.Kit.Cooldown01(pendingSlot) <= 0.001f && !me.Kit.Acting)
            {
                if (tick < pendingUntil) return;
                skipUntil[pendingSlot] = tick + 480;
            }
            pendingSlot = -1;
            int s = -1;
            switch (me.ClassName)
            {
                // Tollbreaker (kit spec rotation): frenzy in a quiet moment, split him, grind under him, recall through him, throw
                case "Tollbreaker":
                    if (me.Kit.Acting || !(me.Kit is TollbreakerKit tb)) return;
                    if (ready(4) && tb.AxesStuck == 0 && Clear(me, 60)) s = 4;                    // Bell Frenzy
                    else if (ready(2) && tb.AxesInHand > 0 && !tb.SplitActive && Clear(me, 40)) s = 2;   // Splitting Throw (planted 0.4 s)
                    else if (ready(3) && tb.AxesStuck > 0 && (!tb.SplitActive || tb.AxesInHand == 0)) s = 3;   // Call of Iron
                    else if (ready(1) && tb.AxesInHand > 0) s = 1;                                // Grinding Toll
                    else if (tb.AxesInHand > 0) { inp.P0 = true; lastPress = tick; return; }       // Twin Hurl: a tap (holding would lodge it)
                    break;
                // casters: decide only between actions; the priorities follow Codex's reference rotation (solo study 25 Oct),
                // which reaches the kits' dummy throughput (Chimesinger ~60, Arcanist ~100 DPS)
                case "Chimesinger":
                    if (me.Kit.Acting || !(me.Kit is ChimesingerKit c)) return;
                    bool hurt = false;
                    foreach (var p in me.World.Players) if (!p.Down && p.Hp < 70 && Sim.Flat(p.transform.position - me.transform.position).magnitude < 6f) hurt = true;
                    if (hurt && ready(4)) s = 4;                                                                     // Mending Chorus
                    else if (c.Bell && ready(3) && ((c.Harmony == 5 && c.Echoes == 2 && c.Strength > 0) || c.BellLeft < 30)) s = 3;   // Crescendo: full, or the bell is ending
                    else if (!c.Bell && c.Charges > 0) s = 1;                                                        // Bellstrike raises the bell
                    else if (c.Bell && c.Echoes < 2 && ready(2)) s = 2;                                              // Harmonic Refrain stores echoes
                    else if (c.Bell && c.Charges > 0 && (c.Strength < 2 || c.BellLeft < 90)) s = 1;                  // Bellstrike strengthens it
                    else if (ready(0)) s = 0;                                                                        // Resonant Note
                    break;
                default:   // Arcanist (Risx's glyph kit, 27 Oct): Disintegrate builds glyphs, Comet and Sigil spend them, Surge spends all
                    if (me.Kit.Acting || !(me.Kit is ArcanistKit a)) return;
                    bool fed = a.Glyphs > 0 || a.Surging;
                    if (ready(4) && !a.Surging && a.Glyphs >= 2 && (ready(3) || ready(2))) s = 4;               // Arcane Surge: runes free and twice as fast
                    else if (ready(3) && fed) s = 3;                                                            // Arcane Comet (lands 3 s later)
                    else if (ready(2) && fed) s = 2;                                                            // Rune Sigil
                    else if (ready(1) && a.Glyphs < ArcanistKit.MaxGlyphs && Clear(me, 150)) s = 1;               // Disintegrate: held below until its 3 steps end
                    else if (ready(0)) s = 0;                                                                   // Magic Missile tap
                    break;
            }
            if (s < 0) return;
            if (s > 0) { pendingSlot = s; pendingUntil = tick + 90; }
            lastPress = tick;
            if (s == 0 && me.Kit is ArcanistKit) { inp.P0 = true; return; }   // a tap, not a held volley
            switch (s) { case 0: inp.P0 = inp.H0 = true; break; case 1: inp.P1 = inp.H1 = true; break; case 2: inp.P2 = true; break; case 3: inp.P3 = true; break; default: inp.P4 = true; break; }
        }

        System.Func<Vector3, bool> bad;
        int spotNote; Vector3 dSpot;   // DebugLine: 1 a spot in reach (dSpot), 2 none
        // nothing visible lands on this bot, or where its lunge carries it (0.6 m toward him), within the next ticks (pools
        // excluded: they are already avoided)
        static bool Clear(PlayerController me, int ticks)
        {
            var pos = me.transform.position; var b = me.World.Boss;
            var lunge = b != null ? pos + Sim.Flat(b.transform.position - pos).normalized * 0.6f : pos;
            var live = me.World.Hazards.Live;
            for (int i = 0; i < live.Count; i++) { var h = live[i]; if (h.BurnPerSecond <= 0 && h.ResolveAt - h.Age <= ticks && (Hits(h, me, pos) || Hits(h, me, lunge))) return false; }
            return true;
        }
        static bool Hits(Hazard h, PlayerController me, Vector3 q) => !h.Soak && (h.Contains(q) || SideOf(h, q)) && (h.Affects == null || h.Affects(me)) && !Spares(h, me) && (!BotSight.IsPath(h) || BotSight.PathSoon(h, q));
        // a shape that leaves out the player standing in it (an Ember Brand never hits its own carrier) is no danger to them
        static bool Spares(Hazard h, PlayerController me) => h.Includes != null && (h.Track == null || h.Age < h.LockAt) && h.Contains(me.transform.position) && !h.Includes(me);   // a frozen brand is left: its pool forms there
        // a lane that strikes after its warning (a Toll Lane) can hit a little wider than it is drawn: keep a hand's width clear
        static bool SideOf(Hazard h, Vector3 q)
        {
            if (h.Shape != HazardShape.Lane || h.HitOnResolve) return false;
            var side = Sim.Dir(h.Yaw + 90f) * LanePad;
            return h.Contains(q + side) || h.Contains(q - side);
        }
        const float LanePad = 0.2f;
        static readonly Hazard Unseen = new Hazard { Source = "orb / rolling strike" };   // the threat when it is not a live hazard
        const float OrbAhead = 0.5f, OrbPad = 0.25f;

        // where this bot must not stand now: inside any shape that would hit it; a Perfect bot also avoids anywhere outside
        // the next Ecliptic anchor's disc and the wrong side of its Zenith Mark (with a 0.35-0.4 m buffer); v4 jobs: its
        // Meridian Tower, a light well during Total Eclipse, its Severance side (and clear of the line's next 2 s of sweep)
        // jobs false: the timed jobs (ring, mark, tower, Sunspot) are far enough off to ignore this tick; standing ones still count
        // Bad sets this state and returns the one cached delegate (BadAt), so a probe allocates nothing (perf agent, 28 Oct: 8 bots
        // made up to ~3 800 probes a tick each, 5-10 MB of garbage a frame); the job lookups run once per bot per tick
        System.Func<Vector3, bool> badAt;
        PlayerController bMe; AurelionEncounter bA; IReadOnlyList<Hazard> bLive;
        bool bFull, bAllowBurn, bAnyJob, bWells, bOthers; int bTick, bK, bSide, bIgnoreAfter; float bDisc; Vector3 bBp, bAnchor;
        ZenithMarks.Marked bMine; MeridianTowers.Tower bTower; Sunspots.Spot bSpot;
        CombatWorld jWorld; int jobsTick = -1; int jK, jSide; bool jWells, jOthers; ZenithMarks.Marked jMine; MeridianTowers.Tower jTower; Sunspots.Spot jSpot;

        System.Func<Vector3, bool> Bad(PlayerController me, BossEncounter boss, bool allowBurn = false, int soonest = int.MaxValue, bool jobs = true)
        {
            if (badAt == null) badAt = BadAt;
            var live = me.World.Hazards.Live;
            // visible jobs (a tower, a light well, a Severance side) for every bot, the allies included; the memory jobs (the
            // Ecliptic order, a Zenith Mark) only for Perfect
            var a = boss as AurelionEncounter; bool memory = Mode == BotMode.Perfect;
            int tick = me.World.Tick;
            if (jobsTick != tick || bMe != me || jWorld != me.World)
            {
                jobsTick = tick; bMe = me; jWorld = me.World;
                jK = memory && a != null && a.Ecliptic != null ? a.Ecliptic.NextStrike(tick) : -1;
                jMine = memory && a != null && a.Marks != null ? a.Marks.Current(me, tick) : null;
                jTower = a != null && a.Towers != null ? a.Towers.NextFor(me, tick) : null;
                jWells = a != null && a.Eclipse != null && a.Eclipse.Active;
                jSide = a != null && a.Sever != null && a.Sever.Active ? (a.Sever.Splitting ? a.Sever.Planned(me) : a.Sever.Assigned(me)) : 0;   // the call while splitting, then the locked side
                jSpot = a != null && a.Spots != null ? a.Spots.NextFor(me, tick) : null;   // a Sunspot on me: take it to the rim
                // co-op: an extra in someone else's tower is hit (MeridianTowers.InOtherTower, without its list per probe)
                int standing = 0; foreach (var p in me.World.Players) if (p && !p.Down) standing++;
                jOthers = a != null && a.Towers != null && a.Towers.Towers.Count >= 2 && standing >= 2;
            }
            int k = jobs ? jK : -1;
            var mine = jobs ? jMine : null;
            var tower = jobs ? jTower : null;
            bool wells = jWells;
            int side = jSide;
            var spot = jobs ? jSpot : null;
            // timed jobs one at a time: only the one that lands first counts now (a tower then a mark, a ring then a mark), or
            // the two zones can leave no ground that satisfies both and the bot freezes
            int due = int.MaxValue;
            if (k >= 0) due = Mathf.Min(due, a.Ecliptic.StrikeAt(k));
            if (mine != null) due = Mathf.Min(due, mine.ResolveTick);
            if (tower != null) due = Mathf.Min(due, tower.ResolveTick);
            if (spot != null) due = Mathf.Min(due, spot.DetonateTick);
            if (k >= 0 && a.Ecliptic.StrikeAt(k) > due) k = -1;
            if (mine != null && mine.ResolveTick > due) mine = null;
            if (tower != null && tower.ResolveTick > due) tower = null;
            if (spot != null && spot.DetonateTick > due) spot = null;
            bLive = live; bA = a; bTick = tick; bAllowBurn = allowBurn;
            bK = k; bMine = mine; bTower = tower; bSpot = spot; bWells = wells; bSide = side;
            bAnyJob = tower != null || k >= 0 || wells || mine != null || spot != null;
            bFull = !(k < 0 && mine == null && tower == null && spot == null && !wells && side == 0 && soonest == int.MaxValue);
            bOthers = bFull && jOthers;
            bBp = Sim.Flat(boss.transform.position);
            bAnchor = k >= 0 ? a.Ecliptic.Anchors[k] : Vector3.zero;
            bDisc = k >= 0 ? a.Ecliptic.SafeRadius - Hazard.Margin - 0.2f : 0;
            // while a timed job is due, a shape that lands after it is not in the way yet: hold the job, dodge it afterwards
            int first = bFull ? Mathf.Min(due, soonest) : int.MaxValue;   // the job or the threat that lands first
            bIgnoreAfter = first == int.MaxValue ? int.MaxValue : first + 30;
            return badAt;
        }

        bool BadAt(Vector3 q)
        {
            // allowBurn (no clean spot left): with a place to be (a tower, a ring, a well, a mark), a burning pool is a cost
            for (int i = 0; i < bLive.Count; i++)
            {
                var h = bLive[i];
                if (h.BurnPerSecond <= 0 && bTick + (h.ResolveAt - h.Age) > bIgnoreAfter) continue;
                if (Hits(h, bMe, q) && !(bAllowBurn && h.BurnPerSecond > 0 && bAnyJob)) return true;
            }
            if (BotSight.Orbs.Count > 0 && BotSight.OrbHits(q, OrbAhead, OrbPad)) return true;
            if (BotSight.GhostHits(bMe, q)) return true;
            if (!bFull) return false;
            var a = bA;
            if (bK >= 0 && Sim.Flat(q - bAnchor).magnitude > bDisc) return true;
            if (bMine != null)
            {
                float d = Sim.Flat(q - bBp).magnitude;
                if (bMine.Mark == ZenithMarks.Mark.Shade ? d > ZenithMarks.ShadeR - Hazard.Margin - 0.4f : d < ZenithMarks.SunR + Hazard.Margin + 0.4f) return true;
            }
            if (bTower != null && Sim.Flat(q - bTower.At).magnitude > bTower.H.Radius - 0.4f) return true;
            if (bOthers)
            {
                var ts = a.Towers.Towers;
                for (int i = 0; i < ts.Count; i++)
                {
                    var t = ts[i];
                    if (t.ResolveTick >= bTick && !t.Owners.Contains(bMe) && Sim.Flat(q - t.At).magnitude <= MeridianTowers.Radius + Hazard.Margin + 0.3f) return true;
                }
            }
            if (bWells && (!a.Eclipse.InWell(q) || !a.Eclipse.InWellAfter(q, 0.6f))) return true;   // lit now and still lit in 0.6 s (the wells turn)
            if (bSpot != null && Sim.Flat(q - bBp).magnitude < Sunspots.RimDrop) return true;
            if (bSpot != null)   // carriers keep apart
            {
                var ss = a.Spots.Spots;
                for (int i = 0; i < ss.Count; i++)
                {
                    var o = ss[i];
                    if (o.Who != bMe && o.Who && !o.Who.Down && !o.Done && Sim.Flat(o.Who.transform.position - q).magnitude < 4.5f) return true;
                }
            }
            if (bSide != 0 && (a.Sever.SideOfAt(q, a.Sever.LineYaw) != bSide || a.Sever.SideOfAt(q, a.Sever.YawIn(0.6f)) != bSide || a.Sever.SideOfAt(q, a.Sever.YawIn(1.2f)) != bSide || a.Sever.DistToLine(q) < 1.0f)) return true;
            return false;
        }

        // ticks until its job (Perfect: the next Ecliptic strike, its Zenith Mark; everyone: its tower) resolves; standing jobs (a
        // light well, a Severance side) count as due now; -1 = none
        int Duty(PlayerController me, BossEncounter boss)
        {
            if (!(boss is AurelionEncounter a)) return -1;
            bool memory = Mode == BotMode.Perfect;
            int t = me.World.Tick, best = -1;
            if (memory && a.Ecliptic != null) { int k = a.Ecliptic.NextStrike(t); if (k >= 0) best = a.Ecliptic.StrikeAt(k) - t; }
            var mk = memory && a.Marks != null ? a.Marks.Current(me, t) : null;
            if (mk != null) { int m = mk.ResolveTick - t; if (best < 0 || m < best) best = m; }
            var sp = a.Spots != null ? a.Spots.NextFor(me, t) : null;
            if (sp != null) { int m = sp.DetonateTick - t; if (best < 0 || m < best) best = m; }
            var tw = a.Towers != null ? a.Towers.NextFor(me, t) : null;
            if (tw != null) { int m = tw.ResolveTick - t; if (best < 0 || m < best) best = m; }
            if ((a.Eclipse != null && a.Eclipse.Active) || (a.Sever != null && a.Sever.Active)) best = best < 0 ? 20 : Mathf.Min(best, 20);
            return best;
        }

        // the spot in attack reach of him (gap Range-1 .. Range+1.6: it stops within 0.15 m, inside the swing gate) nearest this bot that the current constraints allow
        Vector3 tPos; float tRad;   // this tick's target (the boss or its first live part)
        bool AttackSpot(PlayerController me, BossEncounter boss, out Vector3 spot)
        {
            var pos = me.transform.position; var bp = tPos;
            float lo = tRad + PlayerController.Radius + Mathf.Max(0.2f, Range(me) - 1f), hi = tRad + Range(me) + 1.6f;
            spot = pos; float best = float.MaxValue;
            var ecl = (boss as AurelionEncounter)?.Eclipse; bool turning = ecl != null && ecl.Active;   // a well spot must stay lit a while
            for (float r = lo; r <= hi; r += 0.3f)
                for (int k = 0; k < 36; k++)
                {
                    var q = bp + Sim.Dir(k * 10f) * r;
                    if (!CombatWorld.InPlayerBound(q, -0.2f) || bad(q) || ThroughBurn(pos, q, boss) || (turning && !ecl.InWellAfter(q, 1.5f))) continue;
                    float d = Sim.Flat(q - pos).sqrMagnitude; if (d < best) { best = d; spot = q; }
                }
            return best < float.MaxValue;
        }

        static CombatWorld readWorld;   // the world the bot read this tick (ThroughBurn has only the boss)
        static bool ThroughBurn(Vector3 from, Vector3 to, BossEncounter b)
        {
            var w = (b as AurelionEncounter)?.World ?? readWorld; if (w == null) return false;   // every boss since 11-03 (was Aurelion only: a Crucivar bot froze at a Brand Pool between it and its casting)
            var live = w.Hazards.Live;
            for (int j = 0; j < live.Count; j++)
            {
                var h = live[j];
                if (h.BurnPerSecond <= 0 || h.Contains(from)) continue;   // already standing in it: leaving is fine
                for (int i = 1; i < 6; i++) if (h.Contains(Vector3.Lerp(from, to, i / 6f))) return true;
            }
            return false;
        }

        static Vector3 AroundBoss(Vector3 pos, Vector3 move, BossEncounter b)
        {
            var bp = Sim.Flat(b.transform.position); var to = Sim.Flat(pos - bp);
            float clear = b.BodyRadius + PlayerController.Radius + 0.35f;
            if (to.magnitude > clear + 1.5f) return move;
            var ahead = Sim.Flat(pos + move.normalized * 0.8f) - bp;
            if (ahead.magnitude >= clear) return move;   // stepping in toward him is fine (inside a Collapse disc, say)
            // heading into him: go round on the side the move already leans to (tangent), drifting outward a little
            var tan = Vector3.Cross(Vector3.up, to.normalized); if (Vector3.Dot(tan, move) < 0) tan = -tan;
            return (tan + to.normalized * 0.25f).normalized * move.magnitude;
        }

        static Vector3[] Dirs48;
        static Vector3 NearestClear(Vector3 pos, BossEncounter b, System.Func<Vector3, bool> bad, out float need, bool avoidBurn = true)
        {
            var bp = b.transform.position;
            if (Dirs48 == null) { Dirs48 = new Vector3[48]; for (int k = 0; k < 48; k++) Dirs48[k] = Sim.Dir(k * 7.5f); }
            // nearest ring first, stopping at the first clear point (every ring keeps all 48 directions: at 15° beyond 8 m a Hard
            // Ecliptic disc fell between the rays and every solo Hard bot died at 6 s)
            for (float r = 0.2f; r <= 28f; r += r < 8f ? 0.2f : 0.5f)   // the whole arena: a tower can be 20 m away
                for (int k = 0; k < 48; k++)
                {
                    var dir = Dirs48[k]; var q = pos + dir * r;
                    if (!CombatWorld.InPlayerBound(q, -0.2f)) continue;
                    if (Sim.Flat(q - bp).magnitude < b.BodyRadius + PlayerController.Radius + 0.1f) continue;
                    if (bad(q) || (avoidBurn && ThroughBurn(pos, q, b))) continue;   // go round burning pools, not through them
                    need = r; return dir;
                }
            need = 99; return Vector3.zero;
        }

        static Vector2 ToScreen(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
    }
}
