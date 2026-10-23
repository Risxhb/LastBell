using System;
using System.Collections.Generic;
using System.Linq;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Ledger (`bellkeeper.ult_ledger`), Bellkeeper Ultimate — proposal, built 2026-10-23, not human-approved.
    // Design: ultimate/ULTIMATE.md §3.3, §5.1, §5.6; strat GUIDE.md "The Ledger"; proof ultimate/LEDGER.md.
    //
    // Cue (set by the plan, BellkeeperUltimate.cs):
    // - "record" (P1 loop 1, launched at the loop's start): turns the shared recorder on (Ult.Recording) and keeps the first
    //   six P1 strikes of the reworked v2 moves as Ledger I..VI: Leaning Bell (8.5), Toll Tide ring (11.5), Heart Toll
    //   (13.5), Reflection images (19, one page with every image), Leaning Bell (22), Heart Toll (27). The recorder hears
    //   every Ultimate hazard (clocks, tolls); only those four sources count, grouped by resolve tick. Then it switches the
    //   recorder off. Each kept footprint shows as a dashed ivory outline with its numeral and a clock glyph for 2 s.
    //   Optional At = { Leaning Bell I's cast, Reflection's lock } (default 5, 17) for the bots' record strat.
    // - "replay" (P4): At[0] = the first replayed strike (VI), then 1.5 s apart down to I. Each strike is the recorded
    //   footprint exactly as it resolved (positions, sizes, every image), coral for 1.2 s, no dashed preview, 60 each.
    //   With nothing recorded this attempt (practice from P4, -lastbellUltimatePhase 4) it replays the authored clean
    //   ledger (what a good P1 records): both halves toward the NE station, images clumped NE.
    // Hardened beyond §3.3 (Chzy18 2026-10-23, "add and replace abilities as you see fit"): coral 1.2 s instead of 1.5 s.
    // The strikes stay 1.5 s apart, so a party that pre-positions from memory loses nothing; a party that only reacts
    // to coral gets 5.0 m + one dash instead of 6.3 m + one dash per strike.
    // A messy P1 costs (Ultimate session, 2026-10-23): images that weren't clumped (some image more than 4 m from their
    // centre; solo never counts) replay with radius x 1.5 ("the ink runs"), and Leaning Bells that struck opposite halves
    // (more than 90° apart) replay back to back: V moves to 0.75 s before I, so the line is crossed in 0.75 s. A clean
    // P1 replays exactly as recorded. Both stay solvable from memory (LEDGER.md proof; checked in code by Solve()).
    // No reflections in stopped time: a replayed images page that would strike while anyone is Stilled (before the resume
    // tick) is held and never strikes. In P4 block 2 the stop covers V and IV; a Stilled player whose station is opposite
    // the ledger half must hide inside V's half, so V alone is the one documented forced hit (60, survivable from full).
    // Look: P1 the left hand writes on the air (clip WriteLedger) at each kept strike; P4 the halo spins backwards
    // (TurnLedger). Neither clip exists yet (rig list); the outlines, numerals and clock glyphs are code-built stand-ins.
    public sealed class LedgerMechanic : UltimateMechanic
    {
        public override string Id => UltimateMechanics.Ledger;
        public override string Name => "The Ledger";

        public const string ReplaySource = "The Ledger", ImageSource = "Reflection of the Hour";
        public const int Pages = 6, ReplayDamage = UltShapes.HeavyDamage;
        public const int WarnTicks = 72;                      // coral 1.2 s before each replayed strike (hardened from 1.5 s)
        public const float Spacing = 1.5f;                    // replayed strikes VI -> I, 1.5 s apart
        public const float SplitGap = 0.75f, SpreadScale = 1.5f, ClumpRadius = 4f;   // messy-P1 costs (see Replay)
        public const int OutlineTicks = 120;                  // a kept P1 footprint shows for 2 s
        public const float RecordWindow = 40f;                // the recorder gives up this long after "record" (P1 never needs it)
        public const float WalkSpeed = 4.2f, HopSeconds = 1.5f, ShortHop = 2.5f;   // the escape proof's numbers (LEDGER.md)
        public static readonly string[] Recordable = { UltShapes.LeanSource, UltShapes.TideSource, UltShapes.HeartSource, ImageSource };

        public override UltCue DefaultCue() => new UltCue { Variant = "replay", At = new[] { 4f } };

        // One page of the ledger: every footprint one move left on one tick.
        public sealed class Page
        {
            public int Numeral;                                // 1..6 = I..VI
            public string Source;
            public int Tick;                                   // world tick it resolved in P1 (authored ledger: its replay's launch)
            public int Party = 1;                              // standing players when it was recorded (images: one per player)
            public readonly List<UltimateState.Footprint> Prints = new List<UltimateState.Footprint>();
            public string Roman => UltimateState.Roman[Numeral];
        }
        // One replayed strike, for the bots and tests: resolves on ResolveTick; Probes = the shapes it will strike (unspawned).
        public sealed class Step
        {
            public Page Page; public int ResolveTick, SpawnTick; public bool Spawned; public float Scale = 1f;
            public readonly List<Hazard> Probes = new List<Hazard>();
            public readonly List<Hazard> Live = new List<Hazard>();
            public bool Hits(Vector3 q, float pad = 0)
            {
                foreach (var h in Probes)
                {
                    if (h.Contains(q)) return true;
                    if (pad > 0 && (h.Contains(q + new Vector3(pad, 0, 0)) || h.Contains(q - new Vector3(pad, 0, 0))
                                    || h.Contains(q + new Vector3(0, 0, pad)) || h.Contains(q - new Vector3(0, 0, pad)))) return true;
                }
                return false;
            }
        }

        // ---- state (tests and bots read these) ----
        public IReadOnlyList<Page> Recorded => recorded;     // this attempt's P1 ledger (null = nothing recorded yet)
        public IReadOnlyList<Page> Book { get; private set; } // the ledger the current / last replay plays (I..VI)
        public bool UsedAuthored { get; private set; }
        public IReadOnlyList<Step> Upcoming => steps;        // the current replay's strikes, VI first
        public bool IsRecording => recording;
        public int MarksShown => marks.Count;                // live outlines and numerals (0 after a cancel)
        public bool SpreadImages { get; private set; }       // this replay: P1 images weren't clumped, they run x 1.5
        public bool SplitHalves { get; private set; }        // this replay: P1 halves were opposite, V strikes 0.75 s before I
        public readonly List<Page> Held = new List<Page>();  // this replay: pages held by stopped time (images during a Stopped Hour)
        public readonly Dictionary<int, int> PageOf = new Dictionary<int, int>();   // replayed hazard id -> its page numeral (tests, this encounter)
        public bool RouteExists { get; private set; }        // the current replay has a walking route (LEDGER.md proof)
        public float RouteLongestHop { get; private set; }

        List<Page> recorded, take;
        UltimateState.Footprint recordedKey;                 // the attempt's recorder still holds this footprint = same attempt
        bool recording, subscribed;
        readonly List<Step> steps = new List<Step>();

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            if (Cue.Variant == "record") { foreach (var w in Record()) yield return w; }
            else foreach (var w in Replay()) yield return w;
        }

        IEnumerable<int> Record()
        {
            take = new List<Page>();
            recording = true; Ult.Recording = true;
            if (!subscribed) { Ult.FootprintRecorded += OnPrint; subscribed = true; }
            int giveUp = LaunchTick + Sim.Ticks(RecordWindow);
            while (true)
            {
                StepMarks();
                if (recording && ((take.Count >= Pages && Now > take[Pages - 1].Tick) || Now >= giveUp)) StopRecording();
                if (!recording && marks.Count == 0) break;
                yield return 1;
            }
        }
        void OnPrint(UltimateState.Footprint f)
        {
            if (!recording || Array.IndexOf(Recordable, f.Source) < 0) return;
            var page = take.FirstOrDefault(p => p.Source == f.Source && p.Tick == f.Tick);
            if (page == null)
            {
                if (take.Count >= Pages) return;
                page = new Page { Numeral = take.Count + 1, Source = f.Source, Tick = f.Tick, Party = Mathf.Max(1, StandingCount) };
                take.Add(page);
                if (Boss.Presenter) Boss.Presenter.PlayOnce("WriteLedger", 0.9f);
            }
            f.Label = page.Roman;
            page.Prints.Add(f);
            ShowMark(f, page.Numeral, Ult.Outline(f), Now + OutlineTicks);
        }
        void StopRecording()
        {
            recording = false; Ult.Recording = false;
            if (subscribed) { Ult.FootprintRecorded -= OnPrint; subscribed = false; }
            if (take != null && take.Count > 0) { recorded = take; recordedKey = take[0].Prints[0]; }
            take = null;
        }

        IEnumerable<int> Replay()
        {
            Boss.ShowAttack(Name);
            Book = BookForReplay(out bool authored); UsedAuthored = authored;
            steps.Clear(); routes.Clear(); frozen.Clear(); grid = null; Held.Clear();
            float at0 = Cue.At0(4f);
            // the messy-P1 costs (Ultimate session 2026-10-23): unclumped images run (radius x 1.5), and Leaning Bells that
            // struck opposite halves replay back to back (V 0.75 s before I, in the slot just before I's)
            SpreadImages = !Clumped(Book.FirstOrDefault(p => p.Source == ImageSource));
            var leans = Book.Where(p => p.Source == UltShapes.LeanSource).ToList();
            SplitHalves = leans.Count >= 2 && Vector3.Angle(Sim.Dir(leans[0].Prints[0].Yaw), Sim.Dir(leans[leans.Count - 1].Prints[0].Yaw)) > 90f;
            for (int k = 0; k < Book.Count; k++)
            {
                var page = Book[Book.Count - 1 - k];   // VI first
                float at = at0 + Spacing * k;
                if (SplitHalves && page == leans[leans.Count - 1]) at = at0 + Spacing * (Book.Count - 1) - SplitGap;
                int resolve = TickAt(at);
                if (page.Source == ImageSource && TimeStoppedAt(resolve)) { Held.Add(page); continue; }   // no reflections in stopped time
                var s = new Step { Page = page, ResolveTick = resolve, SpawnTick = Mathf.Max(LaunchTick, resolve - WarnTicks), Scale = SpreadImages && page.Source == ImageSource ? SpreadScale : 1f };
                foreach (var f in page.Prints) s.Probes.Add(Copy(f, 1, s.Scale));
                steps.Add(s);
            }
            steps.Sort((a, b) => a.ResolveTick.CompareTo(b.ResolveTick));
            if (Boss.Presenter) Boss.Presenter.PlayOnce("TurnLedger", Mathf.Max(1, steps[steps.Count - 1].ResolveTick - Now) / (float)Sim.Hz);
            Solve();
            int end = steps[steps.Count - 1].ResolveTick;
            while (Now <= end)
            {
                foreach (var s in steps)
                {
                    if (s.Spawned || Now < s.SpawnTick) continue;
                    s.Spawned = true;
                    foreach (var f in s.Page.Prints)
                    {
                        var h = Spawn(Copy(f, UltShapes.ResolveIn(s.ResolveTick - Now), s.Scale));
                        h.OnResolve = Resolved;
                        s.Live.Add(h); PageOf[h.Id] = s.Page.Numeral;
                        ShowMark(f, s.Page.Numeral, null, s.ResolveTick + 1);
                    }
                    // one page is one strike: overlapping images hit a player once (60), not once per image
                    if (s.Live.Count > 1)
                    {
                        var page = s;
                        foreach (var h in page.Live) h.HitOnResolve = false;
                        page.Live[0].OnResolve = hz =>
                        {
                            foreach (var p in W.Players.ToArray())
                            {
                                if (p.Down) continue;
                                var by = page.Live.FirstOrDefault(x => x.Contains(p.transform.position));
                                if (by != null && by.Hit.Add(p)) p.ReceiveHit(by);
                            }
                            Resolved(hz);
                        };
                    }
                }
                StepMarks();
                yield return 1;
            }
            StepMarks();
            steps.Clear(); routes.Clear(); frozen.Clear();
            yield return 30;   // the halo settles; nothing damaging
            ClearMarks();
        }
        // Time is stopped on `tick`: someone is Stilled then, before the resume tick (the Stopped Hour's window runs through
        // its resume tick; time moves again on it).
        bool TimeStoppedAt(int tick)
        {
            foreach (var kv in Ult.All) { var s = kv.Value; if (s.StillFrom >= 0 && tick >= s.StillFrom && tick < s.StillUntil - 1) return true; }
            return false;
        }
        // P1's images were clumped: every mirrored image within ClumpRadius of their centre (one player can't scatter them)
        public static bool Clumped(Page images)
        {
            if (images == null || images.Party <= 1 || images.Prints.Count <= 1) return true;
            var c = Vector3.zero; foreach (var f in images.Prints) c += Sim.Flat(f.Origin); c /= images.Prints.Count;
            return images.Prints.All(f => Sim.Flat(f.Origin - c).magnitude <= ClumpRadius);
        }
        // a live copy of a recorded footprint: same shape and place (an unclumped image runs: radius x scale), the
        // replay's damage, the move's own push
        static Hazard Copy(UltimateState.Footprint f, int resolveAt, float scale = 1f)
        {
            var h = f.ToHazard(ReplaySource, resolveAt, ReplayDamage);
            if (h.Shape == HazardShape.Circle) h.Radius *= scale;
            h.Knockback = f.Source == UltShapes.TideSource ? -TollTideAbility.Push : 1.5f;   // the tide still pushes inward
            h.Stun = 15;
            return h;
        }
        void Resolved(Hazard h)
        {
            var fx = W.BossFx;
            switch (h.Shape)
            {
                case HazardShape.Cone: fx.SweepResolve(h.Origin, h.Yaw, h.Radius, h.HalfAngle); break;
                case HazardShape.Ring: fx.TollResolve(h.Origin, h.Inner, h.Radius); break;
                default: fx.ChimeResolve(h.Origin, h.Radius); break;
            }
            Boss.Flare(0.8f);
        }

        // This attempt's recording (topped up from the authored ledger if P1 was cut short), else the authored clean ledger.
        List<Page> BookForReplay(out bool authored)
        {
            var clean = Authored();
            bool same = recorded != null && recorded.Count > 0 && Ult.Recorded.Contains(recordedKey);
            authored = !same;
            if (!same) return clean;
            var book = new List<Page>(recorded);
            for (int i = book.Count; i < Pages; i++) book.Add(clean[i]);
            return book;
        }

        // ---------------- the authored clean ledger ----------------
        // What a good P1 records (GUIDE.md): both Leaning Bells on the ledger half (toward the NE station), the Heart Tolls on
        // the boss at the centre, and the images clumped on the hit half 5.5 m out (solo: the mirrored image there and your
        // own image on the safe side, 40° round from the half's middle, where a solo player stood at the lock).
        public const int AuthoredStation = 0;
        public List<Page> Authored()
        {
            var d = UltimateState.StationDir(AuthoredStation);
            int n = Mathf.Max(1, StandingCount), tick = Mathf.Max(0, LaunchTick);
            var book = new List<Page>();
            void Add(string source, params Hazard[] hs)
            {
                var p = new Page { Numeral = book.Count + 1, Source = source, Tick = tick, Party = n };
                foreach (var h in hs) p.Prints.Add(UltimateState.Footprint.Of(h, tick, UltimateState.Roman[p.Numeral]));
                book.Add(p);
            }
            Add(UltShapes.LeanSource, UltShapes.LeaningHalf(W, d, 1));
            Add(UltShapes.TideSource, UltShapes.Tide(W, 1));
            Add(UltShapes.HeartSource, UltShapes.HeartToll(W, Vector3.zero, 1));
            float r = n >= ReflectionAbility.LargePartyFrom ? ReflectionAbility.RadiusLargeParty : ReflectionAbility.RadiusSmallParty;
            float yaw = Quaternion.LookRotation(d).eulerAngles.y;
            var images = new List<Hazard>();
            for (int i = 0; i < n; i++) images.Add(Image(Sim.Dir(yaw + 10f * (i - (n - 1) / 2f)) * 5.5f, r));
            if (n == 1) images.Add(Image(Sim.Dir(yaw + 180 + 40) * 5.5f, r));
            Add(ImageSource, images.ToArray());
            Add(UltShapes.LeanSource, UltShapes.LeaningHalf(W, d, 1));
            Add(UltShapes.HeartSource, UltShapes.HeartToll(W, Vector3.zero, 1));
            return book;
        }
        static Hazard Image(Vector3 at, float r) => new Hazard
        {
            Source = ImageSource, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = r, ResolveAt = 1, Damage = UltShapes.StandardDamage,
        };

        // ---------------- the escape proof in code: a walking route through the replay ----------------
        // Grid 0.6 m over the floor (clear of the boss's body). A spot is safe for a strike if it is 0.25 m clear of every
        // footprint. Consecutive spots may be at most (gap − 0.1 s) x WalkSpeed apart (5.9 m in 1.5 s, 2.7 m in 0.75 s, no dash);
        // hops over ShortHop (2.5 m) cost extra, so the route prefers the guide's short steps. Solved once per replay,
        // backwards; each player's route starts from where they stand.
        List<Vector3> grid; List<int>[] safe, reach; float[][] togo; int[][] nextOf;
        readonly Dictionary<PlayerController, Vector3[]> routes = new Dictionary<PlayerController, Vector3[]>();
        const float GridStep = 0.6f, SafePad = 0.25f;

        void Solve()
        {
            grid = new List<Vector3>();
            var bp = Sim.Flat(Boss.transform.position); float body = Boss.BodyRadius + PlayerController.Radius + 0.2f;
            for (float x = -13.2f; x <= 13.2f; x += GridStep)
                for (float z = -13.2f; z <= 13.2f; z += GridStep)
                {
                    var q = new Vector3(x, 0, z);
                    if (CombatWorld.InPlayerBound(q, -0.4f) && Sim.Flat(q - bp).magnitude >= body) grid.Add(q);
                }
            int n = steps.Count, g = grid.Count;
            safe = new List<int>[n]; reach = new List<int>[n]; togo = new float[n][]; nextOf = new int[n][];
            for (int k = 0; k < n; k++)
            {
                safe[k] = new List<int>(); reach[k] = new List<int>(); togo[k] = new float[g]; nextOf[k] = new int[g];
                for (int i = 0; i < g; i++) { togo[k][i] = float.PositiveInfinity; nextOf[k][i] = -1; if (!steps[k].Hits(grid[i], SafePad)) safe[k].Add(i); }
            }
            RouteExists = safe.All(s => s.Count > 0);
            RouteLongestHop = float.PositiveInfinity;
            if (!RouteExists) return;
            foreach (var i in safe[n - 1]) { togo[n - 1][i] = 0; reach[n - 1].Add(i); }
            for (int k = n - 2; k >= 0; k--)
            {
                // this hop's walking budget: the time to the next strike (1.5 s, or 0.75 s for split halves) less 0.1 s, at 4.2 m/s
                float hop = Mathf.Max(0, (steps[k + 1].ResolveTick - steps[k].ResolveTick) / (float)Sim.Hz - 0.1f) * WalkSpeed, hop2 = hop * hop;
                foreach (var i in safe[k])
                {
                    float best = float.PositiveInfinity; int arg = -1; var a = grid[i];
                    foreach (var j in reach[k + 1])
                    {
                        float dx = grid[j].x - a.x, dz = grid[j].z - a.z, d2 = dx * dx + dz * dz; if (d2 > hop2) continue;
                        float dist = Mathf.Sqrt(d2), c = dist + (dist > ShortHop ? 4 * (dist - ShortHop) : 0) + togo[k + 1][j];
                        if (c < best) { best = c; arg = j; }
                    }
                    if (arg >= 0) { togo[k][i] = best; nextOf[k][i] = arg; reach[k].Add(i); }
                }
                if (reach[k].Count == 0) { RouteExists = false; return; }
            }
            var probe = RouteFrom(Vector3.zero);
            RouteLongestHop = 0; for (int k = 1; k < probe.Length; k++) RouteLongestHop = Mathf.Max(RouteLongestHop, Sim.Flat(probe[k] - probe[k - 1]).magnitude);
        }
        // the guide's spots through the replay (VI first) for someone starting at `from`; the nearest safe spot per strike if no route
        public Vector3[] RouteFrom(Vector3 from)
        {
            int n = steps.Count; var route = new Vector3[n];
            if (grid == null || n == 0) return route;
            if (RouteExists)
            {
                int cur = -1; float best = float.PositiveInfinity;
                foreach (var i in reach[0]) { float c = Sim.Flat(grid[i] - from).magnitude + togo[0][i]; if (c < best) { best = c; cur = i; } }
                for (int k = 0; k < n; k++) { route[k] = grid[cur]; if (k < n - 1) cur = nextOf[k][cur]; }
                return route;
            }
            var at = Sim.Flat(from);
            for (int k = 0; k < n; k++)
            {
                float best = float.PositiveInfinity; var pick = at;
                foreach (var i in safe[k]) { float c = Sim.Flat(grid[i] - at).magnitude; if (c < best) { best = c; pick = grid[i]; } }
                route[k] = at = pick;
            }
            return route;
        }

        // ---------------- the strat (GUIDE.md "The Ledger") ----------------
        // P1: record a clean ledger. Everyone stands together on the ledger half (toward clock III's station: the second
        // Leaning Bell leans there by the pairing rule) when Leaning Bell I is cast, so both halves fall on it; at
        // Reflection's lock everyone without a clock striking then clumps on one spot, so the images clump.
        // P4: be outside each replayed footprint on its tick, from memory (Since = the tick it was recorded in P1).
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running) return;
            if (Cue.Variant == "record")
            {
                if (!recording) return;
                int leanTick = TickAt(Cue.AtOr(0, 5f)), lockTick = TickAt(Cue.AtOr(1, 17f));
                var half = LedgerHalf(); var bp = Sim.Flat(Boss.transform.position);
                var spot = ClampIn(half * 5f);
                if (Now <= leanTick)
                    into.Add(new BotDemand { Tick = leanTick, Since = LaunchTick, Ok = q => Sim.Flat(q - spot).magnitude <= 1.5f, Hint = spot, Memory = true, Why = "Ledger: stand on the ledger half (I)", From = this });
                if (Now <= lockTick && !StrikesAt(p, lockTick))
                {
                    var clump = ClumpSpot(lockTick, half, bp);
                    into.Add(new BotDemand { Tick = lockTick, Since = LaunchTick, Ok = q => Sim.Flat(q - clump).magnitude <= 2.0f, Hint = clump, Memory = true, Why = "Ledger: clump the images (IV)", From = this });
                }
                return;
            }
            if (steps.Count == 0) return;
            if (!routes.TryGetValue(p, out var route)) routes[p] = route = RouteFrom(Sim.Flat(p.transform.position));
            // Stilled through some of the strikes: you dodge them where you freeze. When a hiding spot clear of every strike
            // in the stop exists, those strikes are due at the stop tick, with the hiding rule (else they stay on their own
            // ticks: the documented forced hit)
            var freeze = Frozen(p);
            // no clean hiding spot: the forced hit is coming, so be above it when you freeze (potions are locked while Stilled)
            if (freeze.forced && Now <= freeze.stop)
                into.Add(new BotDemand { Tick = freeze.stop, Since = LaunchTick, MinHp = ReplayDamage + 1, Memory = true, Why = "Ledger: forced hit in the stop, drink first", From = this });
            for (int k = 0; k < steps.Count; k++)
            {
                var s = steps[k]; if (s.ResolveTick < Now) continue;
                bool inStop = freeze.ok && Now <= freeze.stop && s.ResolveTick >= freeze.stop && s.ResolveTick <= freeze.resume;
                into.Add(new BotDemand
                {
                    Tick = inStop ? freeze.stop : s.ResolveTick, Since = s.Page.Tick, Ok = q => !s.Hits(q, 0.15f),
                    Hint = inStop ? freeze.spot : k < route.Length ? route[k] : Sim.Flat(p.transform.position),
                    Memory = true, Why = "Ledger " + s.Page.Roman + (inStop ? " (frozen)" : ""), From = this,
                });
            }
        }
        // p's Stopped Hour freeze over this replay: (stop, resume) and a hiding spot clear of every strike in it, if one exists.
        // Checked with the Stopped Hour's own hiding rule (its Demands), once per player per replay.
        readonly Dictionary<PlayerController, (bool ok, bool forced, int stop, int resume, Vector3 spot)> frozen = new Dictionary<PlayerController, (bool, bool, int, int, Vector3)>();
        public readonly List<(int replay, PlayerController player)> ForcedLog = new List<(int, PlayerController)>();   // Stilled with no clean spot (tests)
        (bool ok, bool forced, int stop, int resume, Vector3 spot) Frozen(PlayerController p)
        {
            if (frozen.TryGetValue(p, out var f)) return f;
            f = (false, false, -1, -1, Vector3.zero);
            var sh = Boss.Mechanic<StoppedHour>();
            if (sh != null && sh.Running && sh.StopTick >= Now && sh.StilledPlayers.Contains(p) && grid != null)
            {
                var hide = new List<BotDemand>(); sh.Demands(p, hide);
                var rule = hide.FirstOrDefault(x => x.Tick == sh.StopTick && x.Ok != null);
                var inStop = steps.Where(s => s.ResolveTick >= sh.StopTick && s.ResolveTick <= sh.ResumeTick).ToList();
                if (rule.Ok != null && inStop.Count > 0)
                {
                    float best = float.PositiveInfinity; var at = Sim.Flat(rule.Hint); bool any = false; Vector3 pick = at;
                    foreach (var q in grid)
                    {
                        if (!rule.Ok(q) || inStop.Any(s => s.Hits(q, SafePad))) continue;
                        float d2 = Sim.Flat(q - at).sqrMagnitude; if (d2 < best) { best = d2; pick = q; any = true; }
                    }
                    f = (any, !any, sh.StopTick, sh.ResumeTick, pick);
                    if (!any) ForcedLog.Add((LaunchTick, p));
                }
            }
            if (sh != null && sh.Running && sh.StopTick >= 0) frozen[p] = f;   // known once the hourglass is up
            return f;
        }
        // the ledger half: toward clock III's station once the clocks have assigned it, else the authored NE station
        public Vector3 LedgerHalf()
        {
            foreach (var kv in Ult.All) if (kv.Value.Hour == 3 && kv.Value.Station >= 0) return UltimateState.StationDir(kv.Value.Station);
            return UltimateState.StationDir(AuthoredStation);
        }
        bool StrikesAt(PlayerController p, int tick)
        {
            foreach (var kv in Ult.All) if (kv.Key == p) return kv.Value.Hour > 0 && Mathf.Abs(kv.Value.StrikeTick - tick) <= 6;
            return false;
        }
        // the clump: the clock spot of whoever strikes at the lock (the striker and everyone else stand together), else
        // opposite the ledger half (the images then fall on the half both Leaning Bells hit)
        Vector3 ClumpSpot(int lockTick, Vector3 half, Vector3 bossAt)
        {
            foreach (var kv in Ult.All)
                if (kv.Value.Hour > 0 && kv.Value.Station >= 0 && Mathf.Abs(kv.Value.StrikeTick - lockTick) <= 6)
                    return ClampIn(bossAt - UltimateState.StationDir(kv.Value.Station) * 5.5f);
            return ClampIn(bossAt - half * 5f);
        }
        static Vector3 ClampIn(Vector3 q) { q = Sim.Flat(q); float b = CombatWorld.PlayerBound - 0.8f; return q.magnitude > b ? q.normalized * b : q; }

        // ---------------- practice: a short P1 (the plan's own timings), then its replay ----------------
        public bool PracticeHold;   // tests: Practice() idles
        public override IEnumerable<int> Practice()
        {
            if (PracticeHold) { while (PracticeHold) yield return 1; yield break; }
            Boss.UltLaunch(this, new UltCue { Variant = "record" });
            int t0 = W.Tick;
            foreach (var (at, act) in new (float, Action)[]
            {
                (5f, () => Boss.UltMove(Boss.UltLean, BellkeeperEncounter.LeanTuning())),
                (9f, () => Boss.UltMove(Boss.UltTide, BellkeeperEncounter.TideTuning())),
                (15f, () => Boss.UltMove(Boss.UltMirror, BellkeeperEncounter.MirrorTuning())),
                (18.5f, () => Boss.UltMove(Boss.UltLean, BellkeeperEncounter.LeanTuning(LedgerHalf()))),   // as the pairing rule would
                (25f, () => Boss.UltMove(Boss.UltTide, BellkeeperEncounter.HeartTuning())),
            })
            {
                while (W.Tick < t0 + Sim.Ticks(at)) yield return 1;
                act();
            }
            while (Running) yield return 1;
            yield return 90;
            Boss.UltLaunch(this, DefaultCue());
            while (Running) yield return 1;
            yield return 120;
        }

        public override void Cancel()
        {
            if (recording) { recording = false; Ult.Recording = false; }
            if (subscribed) { Ult.FootprintRecorded -= OnPrint; subscribed = false; }
            take = null;
            steps.Clear(); routes.Clear(); frozen.Clear(); grid = null;
            ClearMarks();
            base.Cancel();
        }

        // ---------------- marks: dashed ivory outlines, numerals and clock glyphs (stand-ins) ----------------
        sealed class Mark { public BellkeeperFx.Telegraph Outline; public Transform Glyph, Numeral; public int Until; }
        readonly List<Mark> marks = new List<Mark>();
        static Material ivory, brass;
        static readonly Color IvoryBase = new Color(0.95f, 0.92f, 0.82f), BrassBase = new Color(0.82f, 0.64f, 0.3f);

        // outline = the dashed ivory memory look (P1); null on a replay, where the numeral stands on the live coral shape
        void ShowMark(UltimateState.Footprint f, int numeral, BellkeeperFx.Telegraph outline, int until)
        {
            var g = Glyph(numeral, Anchor(f));
            marks.Add(new Mark { Outline = outline, Glyph = g, Numeral = g.Find("numeral"), Until = until });
        }
        void StepMarks()
        {
            var cam = Camera.main; var face = cam ? cam.transform.rotation : Quaternion.identity;
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                if (Now >= m.Until) { Drop(m); marks.RemoveAt(i); continue; }
                if (m.Numeral) m.Numeral.rotation = face;
            }
        }
        void ClearMarks() { foreach (var m in marks) Drop(m); marks.Clear(); }
        static void Drop(Mark m) { m.Outline?.Dismiss(); if (m.Glyph) UnityEngine.Object.Destroy(m.Glyph.gameObject); }

        // where a footprint's numeral stands: in the half 6 m out, on the ring's south (camera) side, on an image, and
        // south of a Heart Toll's centre (clear of the boss's body)
        static Vector3 Anchor(UltimateState.Footprint f)
        {
            var south = new Vector3(0, 0, 1);
            switch (f.Shape)
            {
                case HazardShape.Cone: return Sim.Flat(f.Origin) + Sim.Dir(f.Yaw) * Mathf.Min(6f, f.Radius * 0.5f);
                case HazardShape.Ring: return Sim.Flat(f.Origin) + south * (f.Inner + 1.6f);
                case HazardShape.Lane: return Sim.Flat(f.Origin) + Sim.Dir(f.Yaw) * f.Length * 0.5f;
                default: return Sim.Flat(f.Origin) + (f.Source == UltShapes.HeartSource ? south * Mathf.Min(2.6f, f.Radius * 0.65f) : Vector3.zero);
            }
        }

        // An ivory clock face on the floor with a brass hand at the numeral's hour, and the Roman numeral standing over it.
        Transform Glyph(int numeral, Vector3 at)
        {
            if (!ivory) ivory = Prim.Material(IvoryBase, 1.6f);
            if (!brass) brass = Prim.Material(BrassBase, 1.2f);
            var root = new GameObject("Ledger " + UltimateState.Roman[numeral]).transform;
            root.SetParent(W.Root, false); root.position = new Vector3(at.x, 0, at.z);
            Bar(root, "face", new Vector3(0, 0.04f, 0), new Vector3(1.1f, 0.015f, 1.1f), ivory, Quaternion.identity, PrimitiveKind.Cylinder);
            var hand = new GameObject("hand").transform; hand.SetParent(root, false); hand.localPosition = new Vector3(0, 0.07f, 0);
            hand.localRotation = Quaternion.Euler(0, 180 + 30 * numeral, 0);   // 12 o'clock toward north (−Z), turning clockwise on screen
            Bar(hand, "pointer", new Vector3(0, 0, 0.26f), new Vector3(0.07f, 0.025f, 0.5f), brass, Quaternion.identity);
            var num = new GameObject("numeral").transform; num.SetParent(root, false); num.localPosition = new Vector3(0, 1.0f, 0);
            NumeralBars(UltimateState.Roman[numeral], num);
            num.localScale = Vector3.one * 1.5f;
            return root;
        }
        // I = one stroke, V = two leaning strokes, with clock-face serif bars top and bottom (as the v2 order numerals)
        static void NumeralBars(string numeral, Transform g)
        {
            const float h = 0.5f, stroke = 0.075f;
            float x = 0; var parts = new List<Transform>();
            foreach (char c in numeral)
            {
                if (c == 'I') { parts.Add(Bar(g, "I", new Vector3(x + 0.07f, 0, 0), new Vector3(stroke, h, 0.03f), ivory, Quaternion.identity)); x += 0.15f; }
                else
                {
                    parts.Add(Bar(g, "V", new Vector3(x + 0.085f, 0, 0), new Vector3(stroke, h * 1.04f, 0.03f), ivory, Quaternion.Euler(0, 0, 15)));
                    parts.Add(Bar(g, "V", new Vector3(x + 0.215f, 0, 0), new Vector3(stroke, h * 1.04f, 0.03f), ivory, Quaternion.Euler(0, 0, -15)));
                    x += 0.32f;
                }
            }
            x -= 0.01f;
            parts.Add(Bar(g, "serif", new Vector3(x / 2, h / 2 + 0.02f, 0), new Vector3(x + 0.06f, 0.045f, 0.03f), ivory, Quaternion.identity));
            parts.Add(Bar(g, "serif", new Vector3(x / 2, -h / 2 - 0.02f, 0), new Vector3(x + 0.06f, 0.045f, 0.03f), ivory, Quaternion.identity));
            foreach (var p in parts) p.localPosition -= new Vector3(x / 2, 0, 0);
        }
        static Transform Bar(Transform parent, string name, Vector3 pos, Vector3 scale, Material m, Quaternion rot, PrimitiveKind kind = PrimitiveKind.Cube)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(kind);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
