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

        float Range(PlayerController me) => HoldRange ?? (me.ClassName == "Vanguard" ? 0.9f : me.ClassName == "Chimesinger" ? 6.5f : 8.5f);

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss;
            Vector3 pos = me.transform.position, want;
            if (boss == null || !boss.Solid) { inp.Move = ToScreen(Vector3.zero); return inp; }
            var bpos = Sim.Flat(boss.transform.position);
            inp.Aim = bpos;

            // 1) dodge: in a hazard that locked at least our reaction time ago (test bots: from its first tick) -> nearest
            // clear spot. A Perfect bot also heads for its memory job (next anchor, mark side) as soon as it is due.
            var live = w.Hazards.Live;
            bad = Bad(me, boss);
            Hazard threat = null; int left = int.MaxValue;
            for (int i = 0; i < live.Count; i++)   // indexed: a foreach over the IReadOnlyList allocates its enumerator
            {
                var h = live[i];
                if (h.Age >= (Mode == BotMode.Passive ? Mathf.Max(h.LockAt, 0) + reactTicks : 0) && Hits(h, me, pos) && h.ResolveAt - h.Age < left) { threat = h; left = h.ResolveAt - h.Age; }
            }
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
                if (Attacks && me.ClassName != "Vanguard" && !inp.Dash && !me.Kit.Acting && tick - lastPress >= 6 && me.Kit.Cooldown01(0) <= 0.001f
                    && Sim.Flat(bpos - pos).magnitude - boss.BodyRadius <= Range(me) + 1.85f
                    && (threat == null || move == Vector3.zero || need / (PlayerController.WalkSpeed * 0.55f) < t - 0.3f))
                { inp.P0 = inp.H0 = true; lastPress = tick; }
                castHold = 0;
            }
            else
            {
                // 2) hold a position round the boss at this class's range, on this ally's own angle
                float ang = 360f * slot / of + 30f;
                float dist = boss.BodyRadius + Range(me);
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
                if (move != Vector3.zero && bad(pos + move * 0.4f) && !bad(pos)) move = Vector3.zero;
                if (castHold > 0) castHold--;
                // 3) attack when in range (off: Chzy18 2026-10-22, "the bots are doing too much damage": they only do the
                // mechanics). Set Attacks = true to bring the rotation back.
                float gap = Sim.Flat(bpos - pos).magnitude - boss.BodyRadius;
                if (Attacks && gap <= Range(me) + 1.85f) Attack(me, ref inp);   // Vanguard: 2.75 m; the swing's lunge closes to the 2.6 m reach
                // Vanguard's Last Word reaches 8 m with its wave: use it from range too
                else if (Attacks && me.ClassName == "Vanguard" && gap <= 7.5f && me.Kit.Cooldown01(2) <= 0.001f && tick - lastPress >= 6 && Clear(me, 125)) { inp.P2 = true; lastPress = tick; }
            }
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;   // test bots: before a Hard kill hit (65) could down them
            // walk round his body, not into it: a straight line across the centre would pin the bot against him
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            dMe = me; dDuty = duty; dThreat = threat?.Source; dBad = bad(pos); dMove = move; dPos = pos; dNote = spotNote; spotNote = 0;
            inp.Move = ToScreen(move);
            return inp;
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
            if (pendingSlot > 0 && me.Kit.Cooldown01(pendingSlot) <= 0.001f && !(me.ClassName != "Vanguard" && me.Kit.Acting))
            {
                if (tick < pendingUntil) return;
                skipUntil[pendingSlot] = tick + 480;
            }
            pendingSlot = -1;
            int s = -1;
            switch (me.ClassName)
            {
                case "Vanguard":
                    // Last Word and Sundering Blow can't be dashed out of after release: only start them with nothing landing here
                    // before they end and it has walked out (the dodge waits until 0.75 s plus the walk is left)
                    if (ready(2) && Clear(me, 125)) s = 2;           // Last Word (its wave also reaches 8 m)
                    else if (ready(1) && Clear(me, 100)) s = 1;      // Sundering Blow
                    else if (Clear(me, 90)) s = 0;                   // Steel Rhythm (roots too, up to 0.55 s)
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
        static bool Hits(Hazard h, PlayerController me, Vector3 q) => !h.Soak && h.Contains(q) && (h.Affects == null || h.Affects(me));

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
        bool AttackSpot(PlayerController me, BossEncounter boss, out Vector3 spot)
        {
            var pos = me.transform.position; var bp = Sim.Flat(boss.transform.position);
            float lo = boss.BodyRadius + PlayerController.Radius + Mathf.Max(0.2f, Range(me) - 1f), hi = boss.BodyRadius + Range(me) + 1.6f;
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

        static bool ThroughBurn(Vector3 from, Vector3 to, BossEncounter b)
        {
            var w = (b as AurelionEncounter)?.World; if (w == null) return false;
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
