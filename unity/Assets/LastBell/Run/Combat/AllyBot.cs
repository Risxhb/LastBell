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
    // - Sylvara v3 (bullet hell): ReadSylvara and SylvaraSteer below take over, since bullets are not telegraphs.
    // Test modes (Aurelion v3, HARDER-PROMPT §7; the run menu always uses Passive):
    // - Reflex: reflex-perfect and memory-blind. It reacts on the tick any hostile shape appears (amber included, not at
    //   lock), dashing and sprinting, enters any visible soak, drinks potions, and ignores numerals, marks and in/out cues.
    // - Perfect: the assignment-follower. Reflex dodging plus perfect knowledge of Aurelion's memory mechanics: it walks
    //   to the next Ecliptic Order anchor as soon as the previous strike lands and keeps its Zenith Mark's side.
    public enum BotMode { Passive, Reflex, Perfect }

    // A boss that hands bots a job the generic dodge and soak logic can't read (Briar Metronome: walk to your own season
    // graft by the inner path, then cut it). Stand: where to be now (a waypoint is fine); Aim: what to swing at when near.
    public interface IAllyBotJob { bool BotJob(PlayerController me, out Vector3 stand, out Vector3 aim); }

    public sealed class AllyBot : IInputSource
    {
        readonly int slot, of;            // this ally's index and the ally count (for its angle round the boss)
        readonly int reactTicks;
        public readonly BotMode Mode;
        // tests: what it decided last tick; only formatted when read, so a tick stores plain values and makes no strings
        public string DebugLine => sylOn ? SylvaraLine() : dMe == null ? null : Describe();
        public string Brief => dMe == null ? null : $"{dMe.ClassName.Substring(0, 2)} {dJob ?? "-"} gap {Sim.Flat(tPos - dMe.transform.position).magnitude - tRad:0.0} {(tick - lastPress < Sim.Hz ? "swing" : "idle")}{(dJob == "volley" && volEsc != null ? " " + volEsc : "")}";   // the sweep trace: job, gap to the target, pressed in the last second
        public float? HoldRange;          // tests: keep this gap to the boss instead of the class's range (8.5 = a ranged player)
        // bots are mechanics dummies by default: they move, dodge and spread, but never attack or heal. Launch switch
        // -lastbellAllyAttack turns their class rotation on (the boss then scales his HP for them, BossEncounter.Spawn).
        public static bool Attacks = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellAllyAttack", System.StringComparison.OrdinalIgnoreCase));
        // how hard its Sylvara spot pulls in Seed Storm (cost per metre; 0.35 elsewhere). At 0.35 the dodging drifted the
        // Tollbreaker to the wall, 11 m out, in swing range 1 % of the time; 2.5 brought solo Phase 4 runs from 21 % of her
        // HP left to 4-9 %, with a first clear (6 Nov, run 51)
        const float P4Pull = 2.5f;
        // in Seed Storm the Tollbreaker throws from 9 m (a Twin Hurl tap flies 9 m): even pulled home, the seeds keep it
        // 9-10 m out. Phase 4 from its start then cleared 8 of 8 Hard runs, solo and 4 players (run 52; 2 of 8 without)
        const float P4Throw = 9f;
        public static BotMode AllyMode = BotMode.Perfect;   // menu allies do every mechanic, memory ones included (Chzy18: "do the mechanics"); still no attacks
        public static int StartDelay;   // tests: ticks a new bot stands still at the start (-lastbellSweepRepeat varies the fight with it)
        int tick, lastPress = -999, castHold;
        int pendingSlot = -1, pendingUntil;
        readonly int[] skipUntil = new int[6];

        public AllyBot(int index, int count, BotMode mode = BotMode.Passive)
        {
            slot = index; of = Mathf.Max(1, count); Mode = mode;
            reactTicks = mode == BotMode.Passive ? 15 + (index * 7) % 10 : 0;   // 0.25-0.4 s; test bots react on the first tick
        }

        // the nearest downed ally (not dead, within 14 m) nobody else is reviving: walk to a spot beside them that nothing is
        // about to land on and stand still there, so the revive channels (PlayerController: 3 s within 1.5 m)
        const float ReviveStand = 0.9f, ReviveReach = 14f, SylvaraReviveReach = 40f;   // (30 m was tried, 5 Nov: on Sylvara bots crossed her bullets and still never arrived)
        bool ReviveJob(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            if (!ReviveSpot(me, pos, out var spot)) return false;
            if (bad(spot)) return false;   // the ally lies in something about to land: dodge first
            dJob = "revive"; dSpot = spot; dNote = 1;
            var v = Sim.Flat(spot - Sim.Flat(pos));
            if (v.magnitude < 0.3f) { dMove = Vector3.zero; inp.Move = ToScreen(Vector3.zero); return true; }   // beside them: hold still
            var move = v.normalized; if (bad(pos + move * 0.6f)) move = Bend(pos, move);
            if (v.magnitude > 3f && me.Stamina > 40) inp.Sprint = true;
            move = AroundBoss(pos, move, boss); dMove = move; inp.Move = ToScreen(move);
            return true;
        }

        // one bot per downed ally: the nearest, and one already on the way keeps it unless another is 4 m nearer (or two
        // swapped back and forth as they moved, and neither arrived)
        static bool NearestBot(PlayerController me, PlayerController downed, bool going)
        {
            float mine = Sim.Flat(me.transform.position - downed.transform.position).magnitude - (going ? 4f : 0f);
            foreach (var q in me.World.Players)
                if (q != me && q != downed && q && !q.Down && q.Input is AllyBot ab && Sim.Flat(q.transform.position - downed.transform.position).magnitude - (ab.rvFor == downed && ab.dJob == "revive" ? 4f : 0f) < mine) return false;
            return true;
        }
        static bool ReviveSpot(PlayerController me, Vector3 pos, out Vector3 spot) => ReviveSpot(me, pos, out spot, out _);
        static bool ReviveSpot(PlayerController me, Vector3 pos, out Vector3 spot, out PlayerController best, float reach = ReviveReach)
        {
            best = null; float bd = reach; spot = default;
            foreach (var p in me.World.Players)
            {
                if (p == me || !p || !p.Down || p.Dead) continue;
                bool taken = false; foreach (var q in me.World.Players) if (q != me && q && !q.Down && q.Reviving == p) taken = true;   // two are no faster
                float dd = Sim.Flat(p.transform.position - pos).magnitude;
                if (!taken && dd < bd) { bd = dd; best = p; }
            }
            if (best == null) return false;
            var at = Sim.Flat(best.transform.position); var off = Sim.Flat(pos - at);
            spot = at + (off.magnitude > 0.1f ? off.normalized : Vector3.forward) * ReviveStand;
            if (!CombatWorld.InPlayerBound(spot, -0.5f)) spot = at + Sim.Flat(-at).normalized * ReviveStand;   // at the wall: the inner side
            return true;
        }

        float Range(PlayerController me) => HoldRange ?? (me.ClassName == "Tollbreaker" ? 3.5f : me.ClassName == "Chimesinger" ? 6.5f : 8.5f);

        // which boss part to hit: the first in kill order (or by name where the encounter gives none). With three or more
        // bots the back half wears down the next casting meanwhile, so the party does not stand in one turret's fan, and
        // stops while it is within PartReserve of sinking (on Hard one sunk out of order sets every turret firing)
        const float PartReserve = 0.25f, PartReserveMin = 120f;   // axes and recalls land on whichever casting they touch: room for a stray one
        BossPart PartTarget(CombatWorld w)
        {
            BossPart first = null, next = null;
            foreach (var part in w.Parts)
            {
                if (!part.Alive) continue;
                if (first == null || Before(part, first)) { next = first; first = part; }
                else if (next == null || Before(part, next)) next = part;
            }
            if (next != null && of >= 3 && slot >= of / 2 && BotSight.PartOrder(next) >= 0 && LiveParts(w) >= 3
                && BotSight.PartHp(next) > Mathf.Max(PartReserveMin, BotSight.PartMaxHp(next) * PartReserve)) return next;
            return first;
        }
        // with two castings up (every party size since 4 Nov) the whole party breaks the first: the lone bot left on it
        // spent the second uprising dodging bullets 8 m off while the rest wore the next one down (Hard party sweep, 4 Nov)
        static int LiveParts(CombatWorld w) { int n = 0; foreach (var p in w.Parts) if (p.Alive) n++; return n; }
        // a casting held back for its turn standing nearer than the target: a swing or a recall could sink it out of order
        static bool HeldNear(CombatWorld w, Vector3 pos, Vector3 target)
        {
            int low = int.MaxValue;
            foreach (var p in w.Parts) if (p.Alive && BotSight.PartOrder(p) >= 0) low = Mathf.Min(low, BotSight.PartOrder(p));
            float to = Sim.Flat(target - pos).magnitude;
            foreach (var p in w.Parts)
                if (p.Alive && BotSight.PartOrder(p) > low && BotSight.PartHp(p) <= Mathf.Max(PartReserveMin, BotSight.PartMaxHp(p) * PartReserve)
                    && Sim.Flat(p.Pos - pos).magnitude - p.BodyRadius < to) return true;
            return false;
        }
        static bool Before(BossPart a, BossPart b)
        {
            int oa = BotSight.PartOrder(a), ob = BotSight.PartOrder(b);
            return oa >= 0 && ob >= 0 ? oa < ob : string.CompareOrdinal(a.Name, b.Name) < 0;
        }

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss; readWorld = w;
            Vector3 pos = me.transform.position, want;
            if (boss == null || !boss.Solid || tick <= StartDelay) { inp.Move = ToScreen(Vector3.zero); return inp; }
            var bpos = Sim.Flat(boss.transform.position); float brad = boss.BodyRadius;
            tPos = bpos; tRad = brad;
            if (boss is SylvaraEncounter sy) return ReadSylvara(me, sy);   // her own path picks her or a Choir Bud itself
            // the target: the boss, or while boss parts stand (Crucivar's castings, which must die before the Pour Flood) the
            // first of them by name: the castings carry their numerals, and on Hard one sunk out of order sets every turret
            // firing (a party went down to it, 11-03); hits land on whichever of them is nearest, so aim and reach follow it
            var first = PartTarget(w);
            if (first != null) { bpos = Sim.Flat(first.Pos); brad = first.BodyRadius; }
            tPos = bpos; tRad = brad;
            inp.Aim = bpos;
            BotSight.Update(w, boss);
            dJob = null;
            dNote = 0;
            if (Scripted(me, boss, pos, ref inp)) { dMe = me; dPos = pos; return inp; }

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
            // revives: nothing landing here soon and no job of its own: get a downed ally up
            if (threat == null && duty < 0 && ReviveJob(me, boss, pos, ref inp)) { dMe = me; dPos = pos; return inp; }
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
                // a job the boss hands out (IAllyBotJob: Briar Metronome's grafts): go there and cut what it names, even
                // as a non-attacking menu bot (a mechanic, like Sylvara's Choir Buds)
                Vector3 jobStand = default, jobAim = default;
                bool job = boss is IAllyBotJob bj && bj.BotJob(me, out jobStand, out jobAim);
                if (job)
                {
                    want = jobStand; inp.Aim = jobAim;
                    if (Sim.Flat(jobAim - pos).magnitude <= 3.2f)
                    {
                        if (me.Kit is TollbreakerKit) Attack(me, ref inp);
                        else inp.P0 = inp.H0 = tick % 6 == 0 || me.ClassName != "Arcanist";
                    }
                }
                // attacking: the nearest spot in reach of him that every job and telegraph allows (inner disc edge, well edge, ...)
                else if (Attacks && AttackSpot(me, boss, out var spot)) { want = spot; spotNote = 1; dSpot = spot; } else spotNote = 2;
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
                if (Attacks && !job && gap <= Range(me) + 1.85f) Attack(me, ref inp);
            }
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;   // test bots: before a Hard kill hit (65) could down them
            // walk round his body, not into it: a straight line across the centre would pin the bot against him
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            // Aurelion's Devour pulls faster than a walk: any move against it is a sprint
            if (move != Vector3.zero && boss is AurelionEncounter da && da.DevourLeft > 0 && Vector3.Dot(move, Sim.Flat(bpos - pos)) < 0 && me.Stamina > 5) inp.Sprint = true;
            dMe = me; dDuty = duty; dThreat = threat?.Source; dBad = bad(pos); dMove = move; dPos = pos; dNote = spotNote; spotNote = 0;
            inp.Move = ToScreen(move);
            return inp;
        }

        // Bosses whose mechanics are not floor shapes, played from what the screen shows (the Dial Warden: the lit hours and
        // their key prompts, the safe circle while the hand sweeps, the Mainspring gaps, the Scales' mirror circle). True when
        // it set this tick's input.
        bool Scripted(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            // a tower of the next set to fill (the generic soak job) comes first: a dropped Mould Tower is Molten Overflow, a party
            // wipe, and the floor jobs below would hold the bot out of it (PR 14 + PR 19, 4 Nov); the soak path dodges on its own
            if (OwnSoak(me, boss) != null) return false;
            if (!(boss is AurelionEncounter) && Quake(me, boss, pos, ref inp)) { dJob = "quake"; return true; }
            if (!(boss is AurelionEncounter) && Volley(me, boss, pos, ref inp)) { dJob = "volley"; return true; }
            if (!(boss is AurelionEncounter) && Brand(me, boss, pos, ref inp)) { dJob = "brand"; return true; }   // Aurelion keeps its own Sunspot job
            if (!(boss is AurelionEncounter) && Spokes(me, boss, pos, ref inp)) { dJob = "spokes"; return true; }
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
                if ((h.AttackId == "crucivar.anvil_quake" || h.AttackId == "crucivar.quake_echo") && (h.Shape == HazardShape.Ring || h.Shape == HazardShape.Circle) && h.Track == null && h.Damage > 0 && !h.Cancelled
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
            for (int j = 0; j < n; j++) { if (cost[j] == float.MaxValue) continue; float c = cost[j] + (brand ? 1.5f : 0.3f) * Mathf.Abs(rMin + j * QuakeStep - pref); if (c < bestCost) { bestCost = c; best = j; } }
            if (best < 0) return false;   // no plan in reach: the plain dodge
            for (int i = quakeBands.Count - 1; i > 0; i--) best = from[i, best];
            float rGoal = rMin + best * QuakeStep;
            float t0 = (quakeBands[0].ResolveAt - quakeBands[0].Age) / (float)Sim.Hz;
            // the bearing: our own, or the nearest one clear of burning ground, of other strikes still to land (a locked Casting
            // Order pour: a Chimesinger bot stood in Pour IV planning the quake, took it and the channel, and Anvil Quake II
            // downed her, 11-04) and, with brands out, of the other players
            Vector3 goal = hub + dir * rGoal;
            for (int k = 0; k < 13; k++)
            {
                var q = hub + Sim.Dir(yaw + (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 10f) * rGoal;
                // the walk there too: a radial walk inside a Pour Channel (a lane out from the same centre) burns all the way
                bool ok = k == 0 || Sim.Flat(q - pos).magnitude <= QuakePace * (t0 - 0.15f);
                for (int i = 0; i < live.Count && ok; i++)
                {
                    var h = live[i];
                    if (h.BurnPerSecond <= 0)
                    {
                        if (h.Damage > 0 && !h.Cancelled && h.Age < h.ResolveAt && h.ActiveTicks == 0 && !quakeBands.Contains(h) && Hits(h, me, q)) ok = false;
                        continue;
                    }
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
            if (brand && me.Stamina > 5) for (int i = 0; i < live.Count; i++) { var h = live[i]; if (h.AttackId == "crucivar.backdraft" && h.Age >= Sim.Ticks(ForgeRite.PullFrom - 0.3f) && h.Age <= Sim.Ticks(ForgeRite.PullTo)) inp.Sprint = true; }
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            dMove = move; dSpot = goal; dNote = 1;
            inp.Move = ToScreen(move);
            // in place with time to spare: keep throwing
            if (Attacks && v.magnitude < 0.5f && t0 > 0.5f && Sim.Flat(boss.transform.position - pos).magnitude - boss.BodyRadius <= Range(me) + 1.85f) Attack(me, ref inp);
            return true;
        }
        const float BrandRadiusPad = 4f;   // a brand (3 m) and a hand: brand carriers keep this far from other players

        // bullets (Crucivar's turret volleys: small moving circles that stun and knock you along their line): the plain dodge
        // walks to the nearest spot clear now, and the next bullet of the column or ring crosses the walk (three bots went
        // down to that within half a second, 11-03). While bullets can reach it, every few ticks it scores walking or
        // sprinting each way (or standing) against where each bullet will be over the next VolleyLook s, plus the remembered
        // paths still to fire, burning ground, strikes landing and its attack spot, the way the Sylvara bots steer; a dash
        // when every way is hit. A brand carrier steers for the rim.
        Vector3 volMove; bool volSprint, volOn, volReach, volDash; float volCost, volPull; int volTick = -99;
        const float VolleyLook = 0.8f, VolleyStep = 0.05f, VolleyBuffer = 0.15f, VolleyPathCost = 30f, VolleyPathSoon = 1.6f, VolleySwing = 20f, VolleySprintFloor = 55f;   // a swing waits only for a bullet it would meet (50 and up)
        const float VolleyJetRing = 8f;   // m from the jets' hub where they sweep at a walk (40 deg/s, WalkSpeed 6)
        const float VolleyJetHit = 200f;  // a jet step in the bullet dodge (a bullet step is 100)
        bool volJets; Vector3 volHub; string volEsc;   // volEsc: the sweep trace
        const float VolleyThrowReach = 7f;   // a Tollbreaker's axe throws, gap to the target (MaxRange 9 m less a margin)
        static readonly List<Hazard> volNear = new List<Hazard>();
        static Vector3[] Dirs16;
        bool Volley(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            var shots = BotSight.Bullets; volNear.Clear();
            float reach = PlayerController.SprintSpeed * VolleyLook + 1.5f;
            for (int i = 0; i < shots.Count; i++)
            {
                var b = shots[i]; var a = Sim.Flat(b.Origin); var e = a + BotSight.ShotVel(b) * VolleyLook;
                if (SegDist(Sim.Flat(pos), a, e) < reach) volNear.Add(b);
            }
            bad = Bad(me, boss, false, me.World.Tick + 45);
            bool path = BotSight.GhostHits(me, pos);
            if (volNear.Count == 0 && !path) { volOn = false; return false; }
            volJets = false; { var lv = me.World.Hazards.Live; for (int i = 0; i < lv.Count; i++) if (Jet(lv[i])) { TrackSpin(lv[i]); volJets = true; volHub = Sim.Flat(lv[i].Origin); break; } }   // the jets' turn, for VolleyCost
            pos = Sim.Flat(pos);
            // home: the rim for a brand carrier, else the attack spot (or straight at the target)
            Vector3 home = tPos; bool carrier = false;
            var live = me.World.Hazards.Live;
            for (int i = 0; i < live.Count && !carrier; i++) { var h = live[i]; if (h.Track != null && h.Age < h.LockAt && h.Shape == HazardShape.Circle && Spares(h, me)) carrier = true; }
            if (carrier) volJets = false;   // its brand goes to the rim, jets or not
            if (carrier) home = (pos.magnitude > 0.1f ? pos.normalized : Vector3.forward) * Mathf.Min(BrandRing, CombatWorld.PlayerBound - 1.2f);
            else if (Attacks && AttackSpot(me, boss, out var spot)) home = Sim.Flat(spot);
            volReach = Attacks && !carrier; volPull = 0.6f; volEsc = carrier ? "carry" : null;
            // in a fan its bullets are still to cross: the nearest spot clear of every path is the goal (the look is too short
            // to find a Ring Wave's gap metres away: the solo bot ran from the wall of bullets into the corner, 11-03)
            if (path || bad(pos))
            {
                var esc = NearestClear(pos, boss, bad, out float need);
                // not beside a turret: the clear spot there is where its next arms sweep (two bots were pinned so, 11-04)
                if (esc != Vector3.zero && BotSight.TurretDist(pos + esc * need) >= TurretKeep) { home = pos + esc * need; volReach = false; volPull = need > 6f ? 1f : 2f; volEsc = $"esc{need:0}"; }
            }
            if (!volOn || me.World.Tick - volTick >= 3 || me.Stunned > 0)
            {
                volOn = true; volTick = me.World.Tick;
                if (Dirs16 == null) { Dirs16 = new Vector3[16]; for (int k = 0; k < 16; k++) Dirs16[k] = Sim.Dir(k * 22.5f); }
                volDash = false; volSprint = false; volMove = Vector3.zero; volCost = VolleyCost(me, boss, pos, Vector3.zero, 0, home, VolleyLook);
                bool canSprint = me.Stamina > VolleySprintFloor;   // keep enough for a dash when it gets cornered
                for (int s = 0; s < (canSprint ? 2 : 1); s++)
                {
                    float v = s == 0 ? PlayerController.WalkSpeed : PlayerController.SprintSpeed;
                    foreach (var d in Dirs16)
                    {
                        float c = VolleyCost(me, boss, pos, d, v, home, VolleyLook) + (s == 1 ? 4f : 0);
                        if (c < volCost) { volCost = c; volMove = d; volSprint = s == 1; }
                    }
                }
                // every way is hit: a 3 m dash (0.2 s) when its line and landing are clean
                if (volCost >= 50 && !me.Dashing && me.Stamina >= PlayerController.DashCost)
                {
                    float dashSpeed = PlayerController.DashDistance / (PlayerController.DashTicks * Sim.Dt);
                    foreach (var d in Dirs16)
                    {
                        float c = VolleyCost(me, boss, pos, d, dashSpeed, home, PlayerController.DashTicks * Sim.Dt) + 2f;
                        if (c < volCost) { volCost = c; volMove = d; volSprint = false; inp.Dash = volDash = true; }
                    }
                }
            }
            inp.Sprint = volSprint;
            // swing while standing is the plan and nothing comes near it. A Tollbreaker throws from further than it stands (an axe
            // flies 9 m): the solo bot stood 5.5 m off a casting among the bullets, just past its swing reach, with an axe lodged
            // it never recalled, until the Pour Flood (11-04)
            float gap = Sim.Flat(tPos - pos).magnitude - tRad;
            float swing = me.Kit is TollbreakerKit ? VolleyThrowReach : Range(me) + 1.85f;
            if (Attacks && volMove == Vector3.zero && volCost < VolleySwing && gap <= swing) Attack(me, ref inp);
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            var move = volMove;
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            dMove = move; dSpot = home; dNote = 1;
            inp.Move = ToScreen(move);
            return true;
        }

        // moves at v along d for moveFor s, then stands, until VolleyLook
        float VolleyCost(PlayerController me, BossEncounter boss, Vector3 pos, Vector3 d, float v, Vector3 home, float moveFor)
        {
            float cost = 0; var bp = Sim.Flat(boss.transform.position);
            float body = boss.BodyRadius + PlayerController.Radius + 0.1f;
            var live = me.World.Hazards.Live;
            int steps = Mathf.RoundToInt(VolleyLook / VolleyStep);
            Vector3 q = pos; bool stopped = false;
            for (int k = 1; k <= steps; k++)
            {
                float t = k * VolleyStep;
                // a walk into the wall or his body stops there, as the body does (scoring it as out made running into the wall
                // look cheaper than any dodge once hits added up past that score, and a bot by the wall froze, 11-03)
                var nq = pos + d * v * Mathf.Min(t, moveFor);
                if (!stopped && (!CombatWorld.InPlayerBound(nq, -0.1f) || Sim.Flat(nq - bp).magnitude < body)) { stopped = true; cost += 3; }
                if (!stopped) q = nq;
                float early = 1.5f - t / VolleyLook;   // hits sooner are worse: less time to fix them later
                for (int i = 0; i < volNear.Count; i++)
                {
                    var b = volNear[i]; var at = Sim.Flat(b.Origin) + BotSight.ShotVel(b) * t;
                    float g = Sim.Flat(q - at).magnitude - (b.Radius + Hazard.Margin + VolleyBuffer);
                    if (g < 0) cost += 100 * early;
                    else if (g < 0.5f) cost += (0.5f - g) * 4;
                }
                // a Furnace Spin jet where it will have turned to, every step like a bullet and scored for its 39 to a bullet's 20
                // (read every 0.2 s at a flat 100, a jet crossing cost less than one bullet, and the bots dodging bullets in the
                // second and third uprisings walked into the jets: 7 of 13 downs over 4 Hard party runs, 11-04); one hit each, so
                // a jet that has hit this player is done
                for (int i = 0; i < live.Count; i++)
                {
                    var h = live[i]; if (!Jet(h) || h.Hit.Contains(me)) continue;
                    float spin = spokeRef != null && Sim.Flat(spokeRef.Origin - h.Origin).magnitude < 0.1f ? spokeDir : 0;
                    if (JetAt(h, q, t, spin)) cost += VolleyJetHit * early;
                    else if (k == steps && JetAt(h, q, t + 0.5f, spin)) cost += 60;   // standing in its way as it comes round
                }
                if (k % 4 != 0) continue;
                // every 0.2 s: burning ground and strikes landing about now; at the end, the paths still to fire (bullets that
                // reach it after the look: a light cost, or it never stands to swing at a casting inside its turret's fan)
                if (k == steps) { float arrive = BotSight.PathArrive(q) + t; if (arrive < VolleyPathSoon) cost += VolleyPathCost * Mathf.Clamp01((VolleyPathSoon - arrive) / (VolleyPathSoon - VolleyLook)); }
                for (int i = 0; i < live.Count; i++)
                {
                    var h = live[i];
                    if (h.Track != null && h.Shape == HazardShape.Circle && h.Radius <= 0.7f) continue;   // the bullets, above
                    if (h.BurnPerSecond > 0) { if (h.Contains(q) && !h.Contains(pos)) cost += 25; else if (h.Contains(q)) cost += 8; continue; }
                    if (Jet(h)) continue;   // the jets, above
                    if (!Hits(h, me, q)) continue;
                    float lands = Lands(h, boss) / (float)Sim.Hz;
                    if (BotSight.IsPath(h)) { if (k == steps) cost += VolleyPathCost; }
                    else if (lands <= t + 0.15f && lands >= t - 0.25f) cost += 100;
                    else if (k == steps && lands < t + 0.6f) cost += 60;   // standing there as it lands just after
                }
            }
            // its spot, and (attacking, not carrying a brand) being in reach of the target: out of reach it never swings, and
            // a casting sits inside its turret's fan (the solo bot circled one just out of reach until the Pour Flood, 11-03)
            cost += Sim.Flat(q - home).magnitude * volPull;
            float turret = BotSight.TurretDist(q); if (turret < TurretKeep) cost += (TurretKeep - turret) * 8f;   // its spiral arms sweep the wall beside it
            // with Furnace Spin up, inside the ring where a jet comes round slower than a walk (out by the rim at 10-12 m they
            // outran the bots dodging bullets there: three went down in the second uprising, 11-04)
            if (volJets) { float out_ = Sim.Flat(q - volHub).magnitude - VolleyJetRing; if (out_ > 0) cost += out_ * 6f; }
            if (volReach) cost += Mathf.Max(0, Sim.Flat(tPos - q).magnitude - tRad - (Range(me) + 1.5f)) * 4f;
            return cost;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l);
            return Sim.Flat(p - (a + ab * t)).magnitude;
        }

        bool Brand(PlayerController me, BossEncounter boss, Vector3 pos, ref PlayerInput inp)
        {
            Hazard brand = null; var live = me.World.Hazards.Live;
            for (int i = 0; i < live.Count && brand == null; i++)
            {
                var h = live[i];
                if (h.Track != null && h.Age < h.LockAt && h.Shape == HazardShape.Circle && Spares(h, me)) brand = h;
            }
            if (brand == null) return false;
            // only what lands within 0.75 s is in the way: the other carriers' brands land with this one, so standing in one
            // (the party starts bunched round him) left every carrier holding still and their pools filled the jet ring (11-03)
            bad = Bad(me, boss, false, me.World.Tick + 45);
            if (bad(pos)) { dJob = "brand-bad"; return false; }
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
                if (ok && BotSight.TurretDist(q) < TurretKeep) ok = false;
                foreach (var p in me.World.Players) if (ok && p && p != me && !p.Down && Sim.Flat(p.transform.position - q).magnitude < brand.Radius + 1f) ok = false;
                if (!ok) continue;
                float c = Sim.Flat(q - pos).magnitude;
                if (c < bestCost) { bestCost = c; goal = q; }
            }
            dSpot = goal; dNote = bestCost == float.MaxValue ? 2 : 1;
            var v = Sim.Flat(goal - pos);
            var move = v.magnitude < 0.3f ? Vector3.zero : v.normalized;
            if (move != Vector3.zero && bad(pos + move * 0.6f)) move = Bend(pos, move);
            if (v.magnitude > 2f && me.Stamina > 30) inp.Sprint = true;
            dMove = move;
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            inp.Move = ToScreen(move);
            return true;
        }

        // jets turning round one hub (Crucivar's Furnace Spin) reach the wall, where they outrun a sprint: ride the middle of
        // the nearest gap about 6.5 m out, turning with them (shown jets first, the turn read from their yaw)
        Hazard spokeRef; float spokeYaw, spokeDir;
        static readonly float[] SpokeRadii = { 6.5f, 6.0f, 7.0f, 5.5f, 7.5f, 5.0f, 8.0f, 4.5f, 8.6f };   // near the hub a jet covers too wide an angle, far out the gap outruns a walk
        static readonly float[] SpokeShifts = { 0f, 4f, -4f, 8f, -8f, 11f, -11f, 14f, -14f, 17f, -17f };   // degrees off the gap's middle
        const float TurretKeep = 5f;   // m kept from a turret firing (Crucivar's rim turrets)
        const float BrandRing = 12.5f;   // where a brand is walked to: its pool (2 m) stays off the jet ring and leaves the inside of the quake's outer band
        // the turn of a set of jets, read from its first jet's yaw tick to tick (0 until it has turned)
        void TrackSpin(Hazard j0)
        {
            if (spokeRef != j0) { spokeRef = j0; spokeYaw = j0.Yaw; spokeDir = 0; }
            else { float dy = Mathf.DeltaAngle(spokeYaw, j0.Yaw); if (Mathf.Abs(dy) > 0.01f) spokeDir = Mathf.Sign(dy); spokeYaw = j0.Yaw; }
        }

        // a jet t s from now: live from its landing (less a hand) to the end of its sweep, turned by then at JetTurnDeg/s
        // (both ways while the turn is still unknown)
        static bool JetAt(Hazard h, Vector3 q, float t, float dir)
        {
            float on = (h.ResolveAt - h.Age) / (float)Sim.Hz, end = (h.ResolveAt + h.ActiveTicks - h.Age) / (float)Sim.Hz;
            if (t < on - 0.15f || t > end) return false;
            float turned = Mathf.Max(0, t - Mathf.Max(0, on)) * ForgeRite.JetTurnDeg;
            if (dir == 0 && turned > 0) return LaneAt(h, q, h.Yaw + turned) || LaneAt(h, q, h.Yaw - turned);
            return LaneAt(h, q, h.Yaw + dir * turned);
        }
        static bool LaneAt(Hazard h, Vector3 q, float yaw)
        {
            var l = Quaternion.Euler(0, -yaw, 0) * Sim.Flat(q - h.Origin);
            return Mathf.Abs(l.x) <= h.Width / 2 + Hazard.Margin + LanePad && l.z >= -Hazard.Margin && l.z <= h.Length + Hazard.Margin;
        }

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
            // a brand still following this player: the plain dodge, then the brand job, take it out to the rim (riding the gap
            // left its pool on the 6.5 m ring, where the riders then stood in it: a party ally went down to it, 11-03)
            for (int i = 0; i < live.Count; i++) { var h = live[i]; if (h.Track != null && h.Age < h.LockAt && h.Shape == HazardShape.Circle && Spares(h, me)) return false; }
            var j0 = jets[0];
            TrackSpin(j0);
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

        PlayerController dMe; int dDuty, dNote; string dThreat, dJob; bool dBad; Vector3 dMove, dPos;
        string Describe()
        {
            var s = (dJob != null ? $"job {dJob} " : "") + (dJob == "volley" ? $"cost {volCost:0.0} near {volNear.Count} dash {volDash} " : "") + $"duty {dDuty} threat {dThreat ?? "-"} bad {dBad} move ({dMove.x:0.0},{dMove.z:0.0}) at ({dPos.x:0.0},{dPos.z:0.0})"
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
            if (me.World.Parts.Count > 1 && HeldNear(me.World, me.transform.position, tPos)) return;
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
        static bool Hits(Hazard h, PlayerController me, Vector3 q) => !h.Soak && (h.Contains(q) || SideOf(h, q) || Ahead(h, q)) && (h.Affects == null || h.Affects(me)) && !Spares(h, me) && (!BotSight.IsPath(h) || BotSight.PathSoon(h, q));
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
        // a moving circle (a bullet with Drift) also covers the stretch it travels in the next BulletLook seconds: a bot that
        // only sees where bullets are now walks into them (solo Tollbreaker on Crucivar Medium went down to Ring Wave, 11-02)
        public const float BulletLook = 0.35f;
        static bool Ahead(Hazard h, Vector3 q)
        {
            if (h.Shape != HazardShape.Circle || h.Drift.sqrMagnitude < 0.01f) return false;
            var d = Sim.Flat(h.Drift) * BulletLook; var v = Sim.Flat(q - h.Origin);
            float u = Mathf.Clamp01(Vector3.Dot(v, d) / d.sqrMagnitude);
            return (v - d * u).magnitude <= h.Radius + Hazard.Margin;
        }

        // generic soak job (Project overview 11-01): any live hazard marked Soak is a tower to fill with its ShareCount (1 if
        // none). Bosses with their own tower job (Aurelion's Meridian Towers, with owners and order) keep that one. Only the
        // set that lands next counts: each of its towers takes its count of standing players, nearest first in player order,
        // and keeps them until it lands, so every bot reads the same split; the rest keep out of those towers.
        static readonly Dictionary<Hazard, List<PlayerController>> soakOwners = new Dictionary<Hazard, List<PlayerController>>();
        static readonly List<Hazard> soakSet = new List<Hazard>(), soakStale = new List<Hazard>();
        static readonly HashSet<PlayerController> soakTaken = new HashSet<PlayerController>();
        static CombatWorld soakWorld; static int soakTick = -1;
        const int SoakSetSpan = 6;   // towers landing within 0.1 s of the first are one set

        static bool OwnSoakJob(BossEncounter boss) => !(boss is AurelionEncounter a && a.Towers != null);

        static void SoakSet(CombatWorld w)
        {
            if (soakWorld == w && soakTick == w.Tick) return;
            if (soakWorld != w) soakOwners.Clear();
            soakWorld = w; soakTick = w.Tick; soakSet.Clear();
            var live = w.Hazards.Live; int first = int.MaxValue;
            for (int i = 0; i < live.Count; i++) { var h = live[i]; if (h.Soak && h.Age < h.ResolveAt) first = Mathf.Min(first, h.ResolveAt - h.Age); }
            for (int i = 0; i < live.Count; i++) { var h = live[i]; if (h.Soak && h.Age < h.ResolveAt && h.ResolveAt - h.Age <= first + SoakSetSpan) soakSet.Add(h); }
            soakStale.Clear();
            foreach (var h in soakOwners.Keys) if (!soakSet.Contains(h)) soakStale.Add(h);
            foreach (var h in soakStale) soakOwners.Remove(h);
            if (soakSet.Count == 0) return;
            soakSet.Sort((x, y) => x.Id.CompareTo(y.Id));
            soakTaken.Clear();
            foreach (var h in soakSet)
            {
                if (!soakOwners.TryGetValue(h, out var o)) soakOwners[h] = o = new List<PlayerController>();
                for (int i = o.Count - 1; i >= 0; i--) if (!o[i] || o[i].Down) o.RemoveAt(i);
                foreach (var p in o) soakTaken.Add(p);
            }
            foreach (var h in soakSet)
            {
                var o = soakOwners[h]; int need = Mathf.Max(1, h.ShareCount);
                while (o.Count < need)
                {
                    PlayerController pick = null; float best = float.MaxValue;
                    foreach (var p in w.Players)
                    {
                        if (!p || p.Down || soakTaken.Contains(p)) continue;
                        float d = Sim.Flat(p.transform.position - h.Origin).sqrMagnitude;
                        if (d < best) { best = d; pick = p; }
                    }
                    if (pick == null) break;
                    o.Add(pick); soakTaken.Add(pick);
                }
            }
        }

        static Hazard OwnSoak(PlayerController me, BossEncounter boss)
        {
            if (!OwnSoakJob(boss)) return null;
            SoakSet(me.World);
            foreach (var h in soakSet) if (soakOwners[h].Contains(me)) return h;
            return null;
        }

        // where this bot must not stand now: inside any shape that would hit it; a Perfect bot also avoids anywhere outside
        // the next Ecliptic anchor's disc and the wrong side of its Zenith Mark (with a 0.35-0.4 m buffer); v4 jobs: its
        // Meridian Tower, a light well during Total Eclipse, its Severance side (and clear of the line's next 2 s of sweep)
        // jobs false: the timed jobs (ring, mark, tower, Sunspot) are far enough off to ignore this tick; standing ones still count
        // Bad sets this state and returns the one cached delegate (BadAt), so a probe allocates nothing (perf agent, 28 Oct: 8 bots
        // made up to ~3 800 probes a tick each, 5-10 MB of garbage a frame); the job lookups run once per bot per tick
        System.Func<Vector3, bool> badAt;
        PlayerController bMe; AurelionEncounter bA; IReadOnlyList<Hazard> bLive;
        bool bFull, bAllowBurn, bAnyJob, bWells, bOthers; int bTick, bK, bSide, bIgnoreAfter; float bDisc; Vector3 bBp, bAnchor;
        ZenithMarks.Marked bMine; MeridianTowers.Tower bTower; Sunspots.Spot bSpot; Hazard bSoak; bool bSoakOthers;
        CombatWorld jWorld; int jobsTick = -1; int jK, jSide; bool jWells, jOthers, jSoakOthers; ZenithMarks.Marked jMine; MeridianTowers.Tower jTower; Sunspots.Spot jSpot; Hazard jSoak;

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
                jSoak = OwnSoak(me, boss);   // a generic soak tower (any boss without its own tower job)
                jSoakOthers = OwnSoakJob(boss) && soakSet.Count > (jSoak != null ? 1 : 0);
            }
            int k = jobs ? jK : -1;
            var mine = jobs ? jMine : null;
            var tower = jobs ? jTower : null;
            var soak = jobs ? jSoak : null;
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
            if (soak != null) due = Mathf.Min(due, tick + soak.ResolveAt - soak.Age);
            if (k >= 0 && a.Ecliptic.StrikeAt(k) > due) k = -1;
            if (mine != null && mine.ResolveTick > due) mine = null;
            if (tower != null && tower.ResolveTick > due) tower = null;
            if (spot != null && spot.DetonateTick > due) spot = null;
            if (soak != null && tick + soak.ResolveAt - soak.Age > due) soak = null;
            bLive = live; bA = a; bTick = tick; bAllowBurn = allowBurn;
            bK = k; bMine = mine; bTower = tower; bSpot = spot; bWells = wells; bSide = side; bSoak = soak;
            bAnyJob = tower != null || k >= 0 || wells || mine != null || spot != null || soak != null;
            bFull = !(k < 0 && mine == null && tower == null && spot == null && soak == null && !wells && side == 0 && soonest == int.MaxValue);
            bOthers = bFull && jOthers;
            bSoakOthers = jSoakOthers;
            bBp = Sim.Flat(boss.transform.position);
            bAnchor = k >= 0 ? a.Ecliptic.Anchors[k] : Vector3.zero;
            bDisc = k >= 0 ? a.Ecliptic.SafeRadius - Hazard.Margin - 0.2f : 0;
            // while a timed job is due, a shape that lands after it is not in the way yet: hold the job, dodge it afterwards
            int first = bFull ? Mathf.Min(due, soonest) : int.MaxValue;   // the job or the threat that lands first
            bIgnoreAfter = first == int.MaxValue ? int.MaxValue : first + 30;
            Reaches(live);
            return badAt;
        }

        // each live hazard's centre and the squared distance past which Hits can't be true (its shape plus the lane side
        // pad and the bullet look-ahead), so a probe skips it with one distance test. Crucivar Phase 2 (perf agent, 4 Nov):
        // 4 bots probing ~1 900 points against 110-140 turret bullets took 15-25 ms a tick, and the catch-up ticks stacked
        // into 100-228 ms frames
        float[] rX = new float[64], rZ = new float[64], rR2 = new float[64]; int rN;
        void Reaches(IReadOnlyList<Hazard> live)
        {
            rN = live.Count;
            if (rX.Length < live.Count) { int n = Mathf.NextPowerOfTwo(live.Count); rX = new float[n]; rZ = new float[n]; rR2 = new float[n]; }
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i]; float r;
                switch (h.Shape)
                {
                    case HazardShape.Tether: r = -1f; break;   // Contains is always false
                    case HazardShape.Circle: r = h.Radius + Hazard.Margin + Sim.Flat(h.Drift).magnitude * BulletLook; break;
                    case HazardShape.Ring: case HazardShape.Cone: r = h.Radius + Hazard.Margin; break;
                    default:
                    {
                        float l = Mathf.Max(h.Length, 0) + Hazard.Margin, wd = Mathf.Abs(h.Width) / 2 + Hazard.Margin;
                        r = Mathf.Sqrt(l * l + wd * wd) + LanePad; break;
                    }
                }
                r += 0.01f;   // rounding
                rX[i] = h.Origin.x; rZ[i] = h.Origin.z; rR2[i] = r < 0 ? -1f : r * r;
            }
        }

        bool BadAt(Vector3 q)
        {
            // allowBurn (no clean spot left): with a place to be (a tower, a ring, a well, a mark), a burning pool is a cost
            for (int i = 0; i < bLive.Count; i++)
            {
                if (i < rN) { float dx = q.x - rX[i], dz = q.z - rZ[i]; if (dx * dx + dz * dz > rR2[i]) continue; }   // out of its reach: Hits is false
                var h = bLive[i];
                if (h.BurnPerSecond <= 0 && bTick + (h.ResolveAt - h.Age) > bIgnoreAfter) continue;
                if (Hits(h, bMe, q) && !(bAllowBurn && h.BurnPerSecond > 0 && bAnyJob && !h.Pit)) return true;
            }
            if (BotSight.Orbs.Count > 0 && BotSight.OrbHits(q, OrbAhead, OrbPad)) return true;
            if (BotSight.GhostHits(bMe, q)) return true;
            // generic soak: never stand in a tower of the next set that isn't mine (an extra is hit, or overfills it)
            if (bSoakOthers)
                for (int i = 0; i < soakSet.Count; i++)
                {
                    var t = soakSet[i];
                    if (t != jSoak && Sim.Flat(q - t.Origin).magnitude <= t.Radius + Hazard.Margin + 0.3f) return true;
                }
            if (!bFull) return false;
            if (bSoak != null && Sim.Flat(q - bSoak.Origin).magnitude > bSoak.Radius - 0.4f) return true;
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
            if (a != null && a.Planets != null && a.Planets.Live && a.Planets.Near(q, 0.6f, 0.9f)) return true;   // where a planet is going (3 Nov: 5.5 m/s)
            if (bSide != 0 && (a.Sever.SideOfAt(q, a.Sever.LineYaw) != bSide || a.Sever.SideOfAt(q, a.Sever.YawIn(0.6f)) != bSide || a.Sever.SideOfAt(q, a.Sever.YawIn(1.2f)) != bSide || a.Sever.DistToLine(q) < 1.0f)) return true;
            return false;
        }

        // ticks until its job (Perfect: the next Ecliptic strike, its Zenith Mark; everyone: its tower) resolves; standing jobs (a
        // light well, a Severance side) count as due now; -1 = none
        int Duty(PlayerController me, BossEncounter boss)
        {
            var sk = OwnSoak(me, boss);   // a generic soak tower, any boss
            int t = me.World.Tick, best = sk != null ? sk.ResolveAt - sk.Age : -1;
            if (!(boss is AurelionEncounter a)) return best;
            bool memory = Mode == BotMode.Perfect;
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
        // pitsOnly: the fallback that accepts walking through burns still goes round a hole you fall through (Hazard.Pit)
        static bool ThroughBurn(Vector3 from, Vector3 to, BossEncounter b, bool pitsOnly = false)
        {
            var w = (b as AurelionEncounter)?.World ?? readWorld; if (w == null) return false;   // every boss since 11-03 (was Aurelion only: a Crucivar bot froze at a Brand Pool between it and its casting)
            var live = w.Hazards.Live;
            for (int j = 0; j < live.Count; j++)
            {
                var h = live[j];
                if (h.BurnPerSecond <= 0 || (pitsOnly && !h.Pit) || h.Contains(from)) continue;   // already standing in it: leaving is fine
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
            // a goal straight behind him: the lean's sign flips with noise every tick and the bot rocked in place against
            // him (Vesper Seedkeeper, 2 players opposite each other); within ±15° of dead ahead keep turning one way
            var tan = Vector3.Cross(Vector3.up, to.normalized);
            float lean = Vector3.Dot(tan, move.normalized);
            if (lean < -0.26f) tan = -tan;
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
                    if (bad(q) || ThroughBurn(pos, q, b, !avoidBurn)) continue;   // go round burning pools (always round a pit), not through them
                    need = r; return dir;
                }
            need = 99; return Vector3.zero;
        }

        // Sylvara v3: bullet-hell steering (SylvaraSteer). It holds its own angle round her (casters at range, Vanguard
        // close) and in Hollow Bloom cuts a Choir Bud (one bud per bot, by slot; BudStand). Every 3 ticks it
        // re-plans the cheapest path through the bullets and telegraphs 0.8 s ahead.
        Vector3 sylMove; bool sylSprint, sylOn, sylDash; float sylCost; Vector3 sylPos;
        int rvTick = -99; PlayerController rvFor; bool rvOk; Vector3 rvWp, rvStand;   // the revive route (ReviveRoute)
        string SylvaraLine() => $"{(dJob == "revive" ? $"revive {(rvOk ? "route" : "wait")} to ({rvStand.x:0.0},{rvStand.z:0.0}) " : "")}cost {sylCost:0.0} move ({sylMove.x:0.0},{sylMove.z:0.0}) sprint {sylSprint} dash {sylDash} at ({sylPos.x:0.0},{sylPos.z:0.0})";

        // how far from its Choir Bud it stands to cut it: Tollbreaker throws from 6 m (a tap flies 9 m), outside the thick of
        // the bud's own petals; the others stand next to it
        static float BudStand(PlayerController me) => me.ClassName == "Tollbreaker" ? 6f : 1.8f;

        PlayerInput ReadSylvara(PlayerController me, SylvaraEncounter sy)
        {
            sylOn = true;
            var inp = new PlayerInput();
            var pos = Sim.Flat(me.transform.position); var bp = Sim.Flat(sy.transform.position);
            var target = bp; Vector3? reach = null;

            // where it wants to be: its own angle round her, or next to its Choir Bud
            float ang = 360f * slot / of + 30f;
            var home = bp + Sim.Dir(ang) * (sy.BodyRadius + Range(me) + 0.5f);
            var buds = sy.AliveBudPositions();
            if (buds.Count > 0)
            {
                var bud = buds[slot % buds.Count];
                home = bud + Sim.Flat(pos - bud).normalized * BudStand(me); target = bud; reach = bud;
            }
            // revives: the spot beside a downed ally is home instead (the steer still dodges, and holding still there
            // channels the revive; no swings, which would pause it). Her bots never revived before (5 Nov sweep: 0 of 6)
            // anywhere on her floor: in phase 4 the party holds the four corners, 18-28 m apart, and the route crosses her bullets
            // (only the bot nearest to them goes: the others keep their posts, or the whole party left them, 5 Nov)
            bool reviving = ReviveSpot(me, pos, out var rspot, out var rwho, SylvaraReviveReach) && NearestBot(me, rwho, rvFor == rwho && dJob == "revive");
            if (reviving)
            {
                // a planned way through her bullets (ReviveRoute), re-planned every 0.1 s: the waypoint until the spot is near
                if (tick - rvTick >= 6 || rvFor != rwho) { rvOk = ReviveRoute.Plan(me, rwho.transform.position, out rvWp, out rvStand); rvTick = tick; rvFor = rwho; }
                home = Sim.Flat(rvStand - pos).magnitude < 1.8f ? rvStand : rvWp; reach = null; dJob = "revive";
            }
            else if (dJob == "revive") dJob = null;
            var flat = new Vector2(home.x, home.z); float lim = CombatWorld.PlayerBound - 1f;
            if (!reviving && flat.magnitude > lim) { flat = flat.normalized * lim; home = new Vector3(flat.x, 0, flat.y); }   // (a revive spot is in bounds already: her square floor's corners are past this circle)

            if (tick % 3 == 1 || me.Dashing)
            {
                sylMove = SylvaraSteer.Choose(me, pos, home, out sylSprint, out sylDash, out sylCost, reach, BudStand(me) + 0.8f, reviving ? SylvaraSteer.RevivePull : sy.PhaseName == SylvaraEncounter.PhaseNames[3] ? P4Pull : 0.35f);
                sylPos = pos;
                // in reach of the downed ally: hold still so the revive channels, unless standing here gets hit (a near miss
                // is fine); small steps toward the exact spot kept pausing it (5 Nov)
                if (reviving && Sim.Flat(rwho.transform.position - pos).magnitude <= PlayerController.ReviveRange - 0.15f && SylvaraSteer.HoldCost(me, pos) < 40f)
                { sylMove = Vector3.zero; sylSprint = false; sylDash = false; }
                // on a clear route, walk it while the step itself is clear: the steer's near-miss costs kept it a couple of
                // metres short of the spot the route found
                else if (reviving && rvOk && !sylDash && Sim.Flat(rvStand - pos).magnitude > 0.3f)
                {
                    var to = Sim.Flat((Sim.Flat(rvStand - pos).magnitude < 1.8f ? rvStand : rvWp) - pos);
                    if (to.magnitude > 0.2f && SylvaraSteer.StepCost(me, pos, to.normalized, PlayerController.WalkSpeed) < 40f) { sylMove = to.normalized; sylSprint = false; }
                }
                if (sylDash) inp.Dash = true;
            }
            inp.Sprint = sylSprint;
            inp.Aim = target;
            // Choir Buds are a mechanic (boss parts that must be cut), so every bot swings its basic attack at its bud, even
            // the non-attacking menu bots; the boss herself only when attacks are on
            bool atBud = reach.HasValue && Sim.Flat(target - pos).magnitude <= BudStand(me) + 1.4f;
            // (Tollbreaker plays its rotation: a held Twin Hurl lodges the axe, and lodged axes need a recall)
            if (!reviving && (atBud || Attacks && Sim.Flat(target - pos).magnitude <= (me.ClassName == "Tollbreaker" && sy.PhaseName == SylvaraEncounter.PhaseNames[3] ? P4Throw : Range(me)) + sy.BodyRadius + 2f))
            {
                if (me.Kit is TollbreakerKit) Attack(me, ref inp);
                else inp.P0 = inp.H0 = tick % 6 == 0 || me.ClassName != "Arcanist";
            }
            if (me.Hp < 70 && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            inp.Move = ToScreen(sylMove);
            return inp;
        }

        static Vector2 ToScreen(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
    }

    // Bullet-hell steering (Sylvara v3): scores candidate moves against every hostile bullet and telegraph that can hurt
    // this player in the next 0.8 s.
    public static class SylvaraSteer
    {
        const float Horizon = 0.8f, Step = 0.05f, Buffer = 0.25f;
        static readonly List<BulletSystem.Bullet> near = new List<BulletSystem.Bullet>();
        static readonly Vector3[] dirs = MakeDirs();
        static Vector3[] MakeDirs() { var d = new Vector3[16]; for (int i = 0; i < 16; i++) d[i] = Sim.Dir(i * 22.5f); return d; }

        // bud: a Choir Bud it is cutting; ending a move outside budReach costs more than a near miss (not a hit)
        // how hard it heads home (cost per metre): a revive pulls harder, or bullets near its path kept a reviver 10 m off the
        // downed ally for the whole bleed-out (5 Nov sweep)
        static float HomePull = 0.35f;
        public const float RevivePull = 1.2f;
        public static Vector3 Choose(PlayerController me, Vector3 pos, Vector3 home, out bool sprint, out bool dash, out float bestCost, Vector3? bud = null, float budReach = 2.6f, float homePull = 0.35f)
        {
            HomePull = homePull;
            var w = me.World; var boss = w.Boss;
            int mine = w.Bullets.ColourOf != null ? w.Bullets.ColourOf(me) : 0;
            // its ring flickers before a toll swaps colours (the same warning a player sees): both colours count until the swap
            bool both = w.Boss is SylvaraEncounter sy && sy.SwapWarned(me);
            // bullets that can reach anywhere it could be within the horizon
            near.Clear();
            float reach = PlayerController.SprintSpeed * Horizon + 1.2f;
            foreach (var b in w.Bullets.Live)
            {
                if (b.Dead || (b.Colour != 0 && b.Colour != mine && !both)) continue;
                var end = b.Pos + b.Vel * Horizon;
                if (SegDist(pos, b.Pos, end) < reach + (b.Split != null ? 3f : 0f)) near.Add(b);
            }
            sprint = false; dash = false;
            var best = Vector3.zero; bestCost = Cost(me, pos, Vector3.zero, 0, home, boss, bud, budReach);
            bool canSprint = me.Stamina > PlayerController.SprintCost;
            for (int s = 0; s < (canSprint ? 2 : 1); s++)
            {
                float v = s == 0 ? PlayerController.WalkSpeed : PlayerController.SprintSpeed;
                foreach (var d in dirs)
                {
                    float c = Cost(me, pos, d, v, home, boss, bud, budReach) + (s == 1 ? 0.6f : 0);
                    if (c < bestCost) { bestCost = c; best = d; sprint = s == 1; }
                }
            }
            // every path is hit: a 3 m dash (0.2 s) clears it when its line and landing are clean
            if (bestCost >= 50 && !me.Dashing && me.Stamina >= PlayerController.DashCost)
            {
                float dashSpeed = PlayerController.DashDistance / (PlayerController.DashTicks * Sim.Dt);
                foreach (var d in dirs)
                {
                    float c = Cost(me, pos, d, dashSpeed, home, boss, bud, budReach, PlayerController.DashTicks * Sim.Dt) + 2f;
                    if (c < bestCost) { bestCost = c; best = d; dash = true; sprint = false; }
                }
            }
            return best;
        }

        // moves at v along d for `moveFor` seconds (all of the horizon by default), then stands
        // what standing still here costs (bullets and telegraphs only): call after Choose, which gathers the near bullets
        public static float HoldCost(PlayerController me, Vector3 pos) => Cost(me, pos, Vector3.zero, 0, pos, me.World.Boss, null, 0);
        // the same for walking d at v (no pull home: the end of the walk is home)
        public static float StepCost(PlayerController me, Vector3 pos, Vector3 d, float v) => Cost(me, pos, d, v, pos + d * v * Horizon, me.World.Boss, null, 0);

        static float Cost(PlayerController me, Vector3 pos, Vector3 d, float v, Vector3 home, BossEncounter boss, Vector3? bud, float budReach, float moveFor = Horizon)
        {
            float cost = 0; var bp = boss != null ? Sim.Flat(boss.transform.position) : Vector3.zero;
            float body = boss != null ? boss.BodyRadius + PlayerController.Radius + 0.1f : 0;
            Vector3 q = pos;
            int steps = Mathf.RoundToInt(Horizon / Step);
            for (int k = 1; k <= steps; k++)
            {
                float t = k * Step;
                q = pos + d * v * Mathf.Min(t, moveFor);
                if (!CombatWorld.InPlayerBound(q, -0.3f)) return 500 + k;
                if (boss != null && Sim.Flat(q - bp).magnitude < body) return 400 + k;
                float early = 1.5f - t / Horizon;   // hits sooner are worse: there is less time to fix them later
                foreach (var b in near)
                {
                    float fuse = b.Split != null ? b.Life - b.Age : float.MaxValue;
                    if (t <= fuse)
                    {
                        float gap = Sim.Flat(q - (b.Pos + b.Vel * t)).magnitude - (b.Radius + Hazard.Margin + Buffer);
                        if (gap < 0) cost += 100 * early;
                        else if (gap < 0.6f) cost += (0.6f - gap) * 4;
                    }
                    else
                    {
                        // a seed bursts into a ring of petals: its ring hits like any bullet for 1 s after the burst, with the
                        // same near-miss margin (Tollbreaker lane, 2 Nov: seed bursts were the last hit in lost Hard runs)
                        var at = b.Pos + b.Vel * fuse; float since = t - fuse;
                        float ring = 4.5f * since, gap = Mathf.Abs(Sim.Flat(q - at).magnitude - ring) - (BulletArt.PetalHitRadius + Hazard.Margin + Buffer);
                        if (since < 1f) { if (gap < 0) cost += 100 * early; else if (gap < 0.6f) cost += (0.6f - gap) * 4; }
                    }
                }
            }
            // telegraphs that land within the horizon (plus a little) where it ends up
            foreach (var h in me.World.Hazards.Live)
            {
                bool hurts = (h.HitOnResolve && h.Damage > 0) || h.BurnPerSecond > 0;
                if (!hurts || !h.Contains(q)) continue;
                float left = (h.ResolveAt - h.Age) * Sim.Dt;
                cost += h.BurnPerSecond > 0 ? 60 : left < Horizon + 0.4f ? 150 : 25;
            }
            cost += Sim.Flat(q - home).magnitude * HomePull;
            if (bud.HasValue) { float past = Sim.Flat(q - bud.Value).magnitude - budReach; if (past > 0) cost += 6 + past * 2; }
            return cost;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l);
            return Sim.Flat(p - (a + ab * t)).magnitude;
        }
    }
}
