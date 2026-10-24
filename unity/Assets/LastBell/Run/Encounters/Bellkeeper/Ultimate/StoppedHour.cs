using System.Collections.Generic;
using System.Linq;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Stopped Hour (`bellkeeper.ult_stopped_hour`), Bellkeeper Ultimate — proposal. Design: ultimate/ULTIMATE.md §3.2,
    // §5, §6; strat: GUIDE.md. Contract: UltimateMechanic.cs.
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the cracked-hourglass glyph. At[0] = the stop starts (Stilled),
    // At[1] = time resumes and the chain lanes lash (default 5, 9). Variant "alone" (T1) or "lash" (with partners on the
    // resume tick). Phase says which partners the guide expects on the resume tick (2, 4, 5: a Heart Toll; 3, 5: Reflection).
    //
    // Rule: half the standing party (seeded, alternating halves; solo: you) is Stilled from At[0] through the resume tick.
    // Each Stilled player owns a station: the one their clock hand points to when the hourglass appears (locked then and
    // shown on the glyph), or one assigned here when they have no clock (T1). Without clocks at N >= 4 the stations come in
    // opposite pairs, so the hiding spots crowd from both sides of the boss. At resume a chain lane (1.6 m, Strike) lashes
    // from that station through the player and stops at the first body: an ally takes 60 (Brace helps), the Bellkeeper's
    // body stops it harmlessly, and if it reaches its Stilled player that player takes 999.
    // Hardened (Chzy18 2026-10-23, "add and replace abilities as you see fit"): the body stops a chain only when the chain's
    // centre line passes through it (BodyRadius), not the shared first-body rule's body + half the lane (2.2 m), so the
    // hiding strip is the narrow one the design asks for. P5: hiding spots keep FissureClear off the burning fissure (it
    // runs along the NW / SE hiding lines).
    //
    // Presentation: the hourglass glyph (Ult.Still) plus a brass needle over the head toward the lash station and "LASH NE"
    // on the glyph line from launch; the boss catches his clapper (CatchClapper, held for the stop; stand-in: glow hold);
    // for the first half of the stop the chains are dashed ivory outlines (taut, friendly), for the second half the real
    // coral lane; on resume the chains lash (ReleaseClapper) with a chime where each one stops.
    public sealed class StoppedHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.StoppedHour;
        public override string Name => "The Stopped Hour";

        public const float LaneWidth = 1.6f;
        public const int AllyDamage = 60;
        public const float HeartClear = 4.6f;          // Heart Toll r 4 + body margin 0.3 + 0.3: the guide's "beyond 4.3 m"
        public const float ClockMax = 7.2f;            // a clock struck from the hiding spot still reaches the boss (7.4 m)
        public const float LashMin = 3.9f, AloneMin = 2.2f, AloneMax = 9f;
        public override UltCue DefaultCue() => new UltCue { Variant = "alone", At = new[] { 5f, 9f } };

        // tests: force every Stilled player's station (-1 = the rule)
        public int ForceStation = -1;

        sealed class Stilled
        {
            public PlayerController P; public int Station; public Vector3 Hint; public bool HintOk;
            public Hazard Lane; public BellkeeperFx.Telegraph Chain; public Transform Needle; public string Note;
        }
        readonly List<Stilled> stilled = new List<Stilled>();
        int stopTick = -1, resumeTick = -1, stops;
        bool heartResume, mirrorResume, fissure;
        // P5: the burning fissure (1.4 m, 30/s) and the great bell's flare along it (2.4 m, 60 on each toll, one inside every
        // P5 stop; FractureOfTime) run along the NW and SE hiding lines. A Stilled player can't step off them, so P5 hiding
        // spots keep the flare's half-width + body margin + 0.15 m off the axis. Still hidden: 1.65 m to the side at
        // 6.6-7.2 m behind passes the chain 1.0-1.1 m from the boss's centre, inside his 1.4 m body (a thin strip, by design).
        public const float FissureClear = 1.65f;
        bool OffFissure(Vector3 q) => !fissure || Mathf.Abs(Vector3.Dot(Sim.Flat(q), UltimateState.FissureNormal)) >= FissureClear;
        float minD, maxD;

        // what each lash did (tests and the report)
        public struct Lash { public int Tick, Station; public PlayerController Target, Struck; public bool Boss; public int Damage; public Vector3 TargetAt; }
        public readonly List<Lash> Lashes = new List<Lash>();
        public IEnumerable<PlayerController> StilledPlayers => stilled.Select(s => s.P);
        public int StopTick => stopTick;
        public int ResumeTick => resumeTick;
        public int StationFor(PlayerController p) { var s = Find(p); return s != null ? s.Station : -1; }
        public Vector3? HintFor(PlayerController p) { var s = Find(p); return s != null ? s.Hint : (Vector3?)null; }
        public bool FormationOk => stilled.All(s => s.HintOk);
        public int NeedlesShown => stilled.Count(s => s.Needle);
        Stilled Find(PlayerController p) { foreach (var s in stilled) if (s.P == p) return s; return null; }

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            Clear();
            int launch = Now;
            stopTick = TickAt(Cue.AtOr(0, 5)); resumeTick = Mathf.Max(stopTick + 1, TickAt(Cue.AtOr(1, 9)));
            bool lash = Cue.Variant == "lash";
            heartResume = lash && (Cue.Phase == 2 || Cue.Phase == 4 || Cue.Phase == 5);
            mirrorResume = lash && (Cue.Phase == 3 || Cue.Phase == 5);
            fissure = Cue.Phase == 5;
            minD = heartResume ? HeartClear : lash ? LashMin : AloneMin;
            maxD = lash ? ClockMax : AloneMax;
            Choose();
            Formation();
            foreach (var s in stilled)
            {
                Ult.Still(s.P, stopTick, resumeTick - stopTick + 1, stopTick - launch);
                var st = Ult.Of(s.P);
                s.Note = "LASH " + UltimateState.StationNames[s.Station];
                if (st.Note == null) st.Note = s.Note;
                s.Needle = BuildNeedle(s.P);
            }
            Boss.PlanLog.Add((Now, "stopped hour: " + string.Join(", ", stilled.Select(s => s.P.name + " " + UltimateState.StationNames[s.Station]))));

            // the hourglass warning (5 s by default)
            while (Now < stopTick) { PlaceNeedles(); Boss.Glow(0.3f * (Now - launch) / Mathf.Max(1f, stopTick - launch)); yield return 1; }

            // the stop: the clapper is caught, the chains go taut (dashed ivory), then turn into the real coral lane
            if (Boss.Presenter) Boss.Presenter.PlayOnce("CatchClapper", (resumeTick - stopTick) / (float)Sim.Hz);
            foreach (var s in stilled)
            {
                if (s.P.Down) continue;
                s.Chain = W.BossFx.Lane(ChainLength(s), LaneWidth); s.Chain.Memory(); PlaceChain(s);
            }
            int coralAt = stopTick + (resumeTick - stopTick) / 2;
            while (Now < coralAt) { PlaceNeedles(); foreach (var s in stilled) if (s.Chain != null) PlaceChain(s); Boss.Glow(0.3f); yield return 1; }
            foreach (var s in stilled)
            {
                s.Chain?.Dismiss(); s.Chain = null;
                if (s.P.Down) continue;
                SpawnLane(s);
            }
            while (Now < resumeTick) { PlaceNeedles(); Boss.Glow(0.3f + 0.7f * (Now - coralAt) / Mathf.Max(1f, resumeTick - coralAt)); yield return 1; }

            // resume: the lanes resolve on this tick (they were spawned to), the clapper is released
            if (Boss.Presenter) Boss.Presenter.PlayOnce("ReleaseClapper", 0.5f);
            Boss.Flare(0.8f);
            yield return 1;
            DropPresentation();
            yield return 29;
        }

        // ---------------- who, and from where ----------------
        void Choose()
        {
            var standing = W.Players.Where(p => !p.Down).ToList();
            if (standing.Count == 0) { stops++; return; }
            // a fixed seeded order per party size, then alternate halves cast by cast (N = 2: one each, alternating)
            var rng = new System.Random(0x5709 + standing.Count * 131);
            var order = standing.OrderBy(_ => rng.Next()).ToList();
            int count = (standing.Count + 1) / 2, start = (stops % 2) * count;
            var srng = new System.Random(Cue.Seed * 31 + stops * 7 + 17);
            int baseStation = srng.Next(4), free = 0;
            // no clock: opposite stations first (the hiding spots crowd from both sides). With Reflection on the resume,
            // two Stilled players on opposite stations have no layout (keeping out of each other's chains lines their
            // images up on each other), so there the Stilled come from two adjacent stations.
            int[] spread = mirrorResume ? new[] { 0, 1, 0, 1 } : new[] { 0, 2, 1, 3 };
            var station = new Dictionary<PlayerController, int>();
            foreach (var p in order)
            {
                int st = ForceStation >= 0 ? ForceStation : Ult.StationOf(p);
                station[p] = st < 0 ? -1 : ((st % 4) + 4) % 4;
            }
            var window = Enumerable.Range(0, count).Select(i => order[(start + i) % order.Count]).ToList();
            var pick = window;
            if (mirrorResume && count >= 2 && window.Any(p => station[p] >= 0))
            {
                // the adjacent pair of stations (q, q + 1) the alternating half already holds most of; fill from the rest
                int q = Enumerable.Range(0, 4).Select(k => (k + baseStation) % 4)
                    .OrderByDescending(k => window.Count(p => station[p] < 0 || station[p] == k || station[p] == (k + 1) % 4)).First();
                bool In(PlayerController p) => station[p] < 0 || station[p] == q || station[p] == (q + 1) % 4;
                pick = window.Where(In).Concat(order.Where(p => !window.Contains(p) && In(p))).Concat(window.Where(p => !In(p))).Take(count).ToList();
                if (pick.Any(p => !In(p))) Boss.PlanLog.Add((Now, "stopped hour: opposite stations in a Reflection resume"));
                baseStation = q;
            }
            foreach (var p in pick)
            {
                int st = station[p];
                if (st < 0) st = (baseStation + spread[free++ % 4]) % 4;
                stilled.Add(new Stilled { P = p, Station = st });
            }
            stops++;
        }

        // The guide's hiding spots as one call-out (a formation): each Stilled player behind the boss from their station,
        // in the phase's distance band, out of every other Stilled player's lane, and (resume with Reflection) where no
        // reflected image lands on another Stilled player. The call-out: everyone steps the same way round the boss off their
        // line (clockwise or counter-clockwise), which keeps opposite stations out of each other's chains. The spots are
        // searched depth-first, nearest first, placing players round-robin over the stations (the most crowded station,
        // then its opposite, then the others): placing one at a time without backtracking boxes later players out once all
        // four stations are Stilled in the Heart Toll band (found by the Conductor's Hand: N = 8, phrase 4). Both directions
        // are tried, then neither; then without the image rule. If nothing fits, each player still gets their own nearest
        // hidden spot (the formation is reported incomplete).
        const int LayoutBudget = 20000;
        void Formation()
        {
            var tiers = new[] { (1, true), (-1, true), (0, true), (1, false), (-1, false), (0, false) };
            Vector3[] hints = null; int tier = -1;
            for (int t = 0; t < tiers.Length && hints == null; t++)
            {
                if (!tiers[t].Item2 && !mirrorResume) break;   // no images on this resume: the image rule is moot
                if (TryLayout(tiers[t].Item1, tiers[t].Item2, out var h)) { hints = h; tier = t; }
            }
            bool clean = hints != null && (!mirrorResume || tier < 3);
            if (hints == null) hints = OwnSpots();
            for (int i = 0; i < stilled.Count; i++)
            {
                stilled[i].Hint = hints[i];
                stilled[i].HintOk = clean && Hidden(stilled[i].Station, hints[i], 0.25f);
                if (!stilled[i].HintOk) Boss.PlanLog.Add((Now, "stopped hour: no clean spot for " + stilled[i].P.name));
            }
        }
        // every hidden spot in the band for one Stilled player, nearest (and least sideways) first
        List<Vector3> Candidates(Stilled s, int sign)
        {
            var bp = BossPos; var back = -UltimateState.StationDir(s.Station);
            var list = new List<(float score, Vector3 q)>();
            for (float d = minD; d <= maxD + 1e-3f; d += 0.2f)
                for (float a = sign > 0 ? 0 : -39; a <= (sign < 0 ? 0 : 39); a += 1.5f)
                {
                    var q = bp + Quaternion.Euler(0, a, 0) * back * d;
                    if (!CombatWorld.InPlayerBound(q, -0.6f) || !Hidden(s.Station, q, 0.25f) || !OffFissure(q)) continue;
                    list.Add((Flat(q - s.P.transform.position) + 0.05f * Mathf.Abs(a), q));
                }
            return list.OrderBy(c => c.score).Select(c => c.q).ToList();
        }
        // round-robin over stations: the most crowded first, then its opposite, then the other two
        List<int> SearchOrder()
        {
            var by = new Dictionary<int, Queue<int>>();
            for (int i = 0; i < stilled.Count; i++) { if (!by.ContainsKey(stilled[i].Station)) by[stilled[i].Station] = new Queue<int>(); by[stilled[i].Station].Enqueue(i); }
            var order = new List<int>();
            if (by.Count == 0) return order;
            int k0 = by.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key).First().Key;
            var pref = new[] { k0, (k0 + 2) % 4, (k0 + 1) % 4, (k0 + 3) % 4 }.Where(by.ContainsKey).ToArray();
            while (order.Count < stilled.Count) foreach (var k in pref) if (by[k].Count > 0) order.Add(by[k].Dequeue());
            return order;
        }
        bool TryLayout(int sign, bool images, out Vector3[] hints)
        {
            var order = SearchOrder();
            var cands = order.Select(i => Candidates(stilled[i], sign)).ToList();
            var pick = new Vector3[order.Count];
            float img = ImageRadius() + Hazard.Margin + 0.2f;
            int nodes = 0;
            bool Fits(int k, Vector3 q)
            {
                var s = stilled[order[k]];
                for (int j = 0; j < k; j++)
                {
                    var o = stilled[order[j]]; var oh = pick[j];
                    if (Flat(q - oh) < 1f || Shadows(o.Station, oh, q, 0.35f) || Shadows(s.Station, q, oh, 0.35f)) return false;
                    if (images && mirrorResume && (Flat(Mirror(q) - oh) < img || Flat(Mirror(oh) - q) < img)) return false;
                }
                return true;
            }
            bool Place(int k)
            {
                if (k == order.Count) return true;
                foreach (var q in cands[k])
                {
                    if (++nodes > LayoutBudget) return false;
                    if (!Fits(k, q)) continue;
                    pick[k] = q;
                    if (Place(k + 1)) return true;
                }
                return false;
            }
            bool ok = Place(0);
            hints = new Vector3[stilled.Count];
            for (int k = 0; k < order.Count; k++) hints[order[k]] = pick[k];
            return ok;
        }
        // fallback: each Stilled player's own nearest hidden spot, ignoring the others
        Vector3[] OwnSpots()
        {
            var hints = new Vector3[stilled.Count];
            for (int i = 0; i < stilled.Count; i++)
            {
                var c = Candidates(stilled[i], 0);
                hints[i] = c.Count > 0 ? c[0] : BossPos - UltimateState.StationDir(stilled[i].Station) * Mathf.Max(minD, 3f);
            }
            return hints;
        }

        // ---------------- the lash ----------------
        void SpawnLane(Stilled s)
        {
            var from = UltimateState.Station(s.Station);
            var h = new Hazard
            {
                Source = Name, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = from, Width = LaneWidth, Length = ChainLength(s),
                Yaw = YawTo(from, s.P.transform.position), ResolveAt = UltShapes.ResolveIn(resumeTick - Now), HitOnResolve = false,
            };
            // the chain stays on its player until it strikes (a Stilled player can still be knocked a step)
            h.LockAt = h.ResolveAt;
            h.Track = hz => { if (s.P && !s.P.Down) { hz.Yaw = YawTo(hz.Origin, s.P.transform.position); hz.Length = ChainLength(s); } };
            h.OnResolve = hz => Resolve(s, hz);
            s.Lane = Spawn(h);
        }

        void Resolve(Stilled s, Hazard hz)
        {
            var dir = Sim.Dir(hz.Yaw);
            PlayerController first = null; float firstAlong = float.MaxValue;
            if (s.P && !s.P.Down)
                foreach (var p in W.Players)
                {
                    if (p.Down || !hz.Contains(p.transform.position)) continue;
                    float along = Vector3.Dot(Sim.Flat(p.transform.position - hz.Origin), dir);
                    if (along < firstAlong) { firstAlong = along; first = p; }
                }
            bool boss = first != null && BodyFirst(hz.Origin, dir, firstAlong, out _);
            int dmg = 0;
            Vector3 stopAt;
            if (first != null && !boss)
            {
                dmg = first == s.P ? UltimateState.Lethal : AllyDamage;
                hz.Damage = dmg; hz.Hit.Add(first); first.ReceiveHit(hz);
                stopAt = first.transform.position;
            }
            else
            {
                BodyFirst(hz.Origin, dir, float.MaxValue, out float hitAt);
                stopAt = hz.Origin + dir * (first != null ? hitAt : hz.Length);
            }
            W.BossFx.ChimeResolve(Sim.Flat(stopAt), 0.7f);
            Lashes.Add(new Lash { Tick = Now, Station = s.Station, Target = s.P, Struck = boss ? null : first, Boss = boss, Damage = dmg, TargetAt = s.P ? Sim.Flat(s.P.transform.position) : Vector3.zero });
            Boss.PlanLog.Add((Now, $"lash {UltimateState.StationNames[s.Station]} -> {(boss ? "boss" : first ? first.name + " " + dmg : "-")}"));
        }

        // the boss's body stops a chain whose centre line passes through it, before `along` metres (hitAt = where)
        bool BodyFirst(Vector3 origin, Vector3 dir, float along, out float hitAt)
        {
            hitAt = float.MaxValue;
            var b = W.Boss; if (b == null || !b.Solid) return false;
            var to = Sim.Flat(b.transform.position - origin); float a = Vector3.Dot(to, dir), side = (to - dir * a).magnitude;
            if (a <= 0 || side > b.BodyRadius) return false;
            hitAt = a - Mathf.Sqrt(Mathf.Max(0, b.BodyRadius * b.BodyRadius - side * side));
            return hitAt < along;
        }
        // the chain from `station` to a player standing at q is stopped by the body (margin: how far inside it the line runs)
        bool Hidden(int station, Vector3 q, float margin)
        {
            var b = W.Boss; if (b == null || !b.Solid) return false;
            var from = UltimateState.Station(station); var v = Sim.Flat(q - from); float len = v.magnitude; if (len < 0.01f) return false;
            var dir = v / len; var to = Sim.Flat(b.transform.position - from); float a = Vector3.Dot(to, dir), side = (to - dir * a).magnitude;
            return side <= b.BodyRadius - margin && a < len - b.BodyRadius * 0.5f;
        }
        // a body at `body` would be struck by the chain from `station` to a player at `target` before the boss stops it
        bool Shadows(int station, Vector3 target, Vector3 body, float margin)
        {
            var from = UltimateState.Station(station); var v = Sim.Flat(target - from); float len = v.magnitude; if (len < 0.01f) return false;
            var dir = v / len; var tb = Sim.Flat(body - from); float along = Vector3.Dot(tb, dir), side = (tb - dir * along).magnitude;
            if (along < -Hazard.Margin || along > len + 0.5f || side > LaneWidth / 2 + Hazard.Margin + margin) return false;
            return !BodyFirst(from, dir, along, out _);
        }

        // ---------------- the perfect bot's strat (GUIDE.md "The Stopped Hour") ----------------
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || stopTick < 0 || Now > resumeTick) return;
            var mine = Find(p);
            if (mine != null)
            {
                if (Now > stopTick || p.Down) return;
                // be on the hiding strip behind the boss from your station by the stop (from the glyph: a read)
                into.Add(new BotDemand
                {
                    Tick = stopTick, Since = LaunchTick, Hint = mine.Hint, Memory = true, From = this,
                    Why = "Stopped Hour: hide from " + UltimateState.StationNames[mine.Station],
                    Ok = q => StilledOk(mine, q),
                });
                return;
            }
            if (p.Down) return;
            // when Reflection resolves with the resume, never put your image on a Stilled ally (images lock 2 s before the
            // resume; known from the glyphs: clear of both their spots and where they actually stand)
            if (mirrorResume && Boss.UltMirror != null)
            {
                int lockTick = resumeTick - Boss.UltMirror.LockBeforeTicks - 1;
                float r = ImageRadius() + Hazard.Margin + 0.3f;
                if (Now <= lockTick)
                    into.Add(new BotDemand
                    {
                        Tick = lockTick, Since = LaunchTick, Hint = p.transform.position, From = this, Why = "Stopped Hour: image off the Stilled",
                        Ok = q => stilled.All(s => s.P.Down || (Flat(Mirror(q) - s.Hint) > r && Flat(Mirror(q) - s.P.transform.position) > r)),
                    });
            }
            if (Now < stopTick) return;
            // free: out of every chain before it is stopped (the dashed chains show from the stop)
            Stilled save = SaverFor(p);
            into.Add(new BotDemand
            {
                Tick = resumeTick, Since = stopTick, Hint = save != null ? SavePoint(save) : p.transform.position, From = this,
                MinHp = save != null ? AllyDamage + 1 : 0,
                Why = save != null ? "Stopped Hour: cover " + save.P.name : "Stopped Hour: out of the chains",
                Ok = q => FreeOk(q, save),
            });
        }

        bool StilledOk(Stilled s, Vector3 q)
        {
            if (Flat(q - s.Hint) > 0.5f || !Hidden(s.Station, q, 0.15f)) return false;
            float d = Flat(q - BossPos); if (d < minD - 0.15f || d > maxD + 0.3f || !OffFissure(q)) return false;
            if (!s.HintOk) return true;   // incomplete formation: at least hide from your own chain (never an empty rule)
            float img = ImageRadius() + Hazard.Margin + 0.1f;
            foreach (var o in stilled)
            {
                if (o == s || o.P.Down) continue;
                var at = Flat(o.P.transform.position - o.Hint) <= 1f ? o.P.transform.position : o.Hint;
                if (Shadows(o.Station, at, q, 0.2f) || Shadows(s.Station, q, at, 0.2f)) return false;
                if (mirrorResume && (Flat(Mirror(q) - at) < img || Flat(Mirror(at) - q) < img)) return false;
            }
            return true;
        }
        bool FreeOk(Vector3 q, Stilled save)
        {
            foreach (var s in stilled)
            {
                if (s.P.Down) continue;
                bool inIt = Shadows(s.Station, s.P.transform.position, q, 0.25f);
                if (s == save) { if (!inIt || Flat(q - s.P.transform.position) < 0.9f) return false; }
                else if (inIt) return false;
            }
            return true;
        }
        // the safety net (optional): a Stilled ally who isn't hidden at the stop is covered by the nearest healthy free player
        Stilled SaverFor(PlayerController p)
        {
            if (p.Hp <= AllyDamage && p.Potions == 0) return null;
            foreach (var s in stilled)
            {
                if (s.P.Down || Hidden(s.Station, s.P.transform.position, 0)) continue;
                var spot = SavePoint(s); float mine = Flat(p.transform.position - spot);
                if (mine > 4.2f * (resumeTick - Now) / Sim.Hz) continue;
                bool nearest = true;
                foreach (var o in W.Players)
                    if (o != p && !o.Down && Find(o) == null && (o.Hp > AllyDamage || o.Potions > 0) && Flat(o.transform.position - spot) < mine - 0.01f) { nearest = false; break; }
                if (nearest) return s;
            }
            return null;
        }
        Vector3 SavePoint(Stilled s)
        {
            var from = UltimateState.Station(s.Station); var to = Sim.Flat(s.P.transform.position);
            return to + Sim.Flat(from - to).normalized * 1.5f;
        }

        // ---------------- helpers and presentation ----------------
        Vector3 BossPos => Sim.Flat(Boss.transform.position);
        static float Flat(Vector3 v) => Sim.Flat(v).magnitude;
        Vector3 Mirror(Vector3 q) => ReflectionAbility.ClampToBound(2 * BossPos - Sim.Flat(q));
        float ImageRadius() => Boss.UltMirror != null ? Boss.UltMirror.RadiusFor(Mathf.Max(1, StandingCount)) : ReflectionAbility.RadiusSmallParty;
        static float YawTo(Vector3 from, Vector3 to) { var d = Sim.Flat(to - from); return d.sqrMagnitude < 1e-4f ? 0 : Quaternion.LookRotation(d).eulerAngles.y; }
        static float ChainLength(Stilled s) => Flat(s.P.transform.position - UltimateState.Station(s.Station)) + 0.8f;
        void PlaceChain(Stilled s) { var from = UltimateState.Station(s.Station); s.Chain.Place(from, YawTo(from, s.P.transform.position)); }
        bool HasStatus(PlayerController p) { foreach (var kv in Ult.All) if (kv.Key == p) return true; return false; }

        static Material needleMat;
        Transform BuildNeedle(PlayerController p)
        {
            if (needleMat == null) needleMat = Prim.Material(new Color(0.82f, 0.64f, 0.3f), 0.5f);   // brass: identity, never coral
            var root = new GameObject("Stopped Hour lash needle · " + p.name).transform;
            root.SetParent(W.Root, false);
            var go = new GameObject("needle"); go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0, 0, 0.45f); go.transform.localScale = new Vector3(0.09f, 0.04f, 0.9f);
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(PrimitiveKind.Cube);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = needleMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }
        void PlaceNeedles()
        {
            foreach (var s in stilled)
            {
                if (!s.Needle) continue;
                s.Needle.gameObject.SetActive(!s.P.Down);
                s.Needle.position = s.P.transform.position + Vector3.up * 3.25f;
                s.Needle.rotation = Quaternion.LookRotation(UltimateState.StationDir(s.Station));
            }
        }
        void DropPresentation()
        {
            foreach (var s in stilled)
            {
                s.Chain?.Dismiss(); s.Chain = null;
                if (s.Needle) Object.Destroy(s.Needle.gameObject);
                s.Needle = null;
                if (s.P && Ult != null && HasStatus(s.P)) { var st = Ult.Of(s.P); if (st.Note == s.Note) st.Note = null; }
            }
        }
        void Clear() { DropPresentation(); stilled.Clear(); }

        public override void Cancel()
        {
            // frees the Stilled players too (the encounter also clears every status on death and phase change)
            if (Ult != null) foreach (var s in stilled) if (s.P && Ult.IsStilled(s.P)) Ult.Of(s.P).StillUntil = W.Tick;
            Clear();
            Lashes.Clear();
            stopTick = resumeTick = -1;
            base.Cancel();
        }
    }
}
