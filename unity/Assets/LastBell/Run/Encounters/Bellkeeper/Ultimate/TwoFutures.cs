using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Two Futures (`bellkeeper.ult_two_futures`), Bellkeeper Ultimate — proposal. Design: ultimate/ULTIMATE.md §3.5, §5.4,
    // §5.7; strat: GUIDE.md "Two Futures". Built, not human-approved.
    //
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the fingers start tracing. At[0] = the first interleaved step
    // resolves; six steps 1.2 s apart. Phase 5 plays the P5 (band) futures, anything else the P3 (hour) futures.
    //
    // Timeline (seconds after launch):
    //   0.0-4.8   the trace: Future I (ivory) steps 1, 2, 3, then Future II (brass) steps 1, 2, 3, each drawn for 0.8 s as
    //             dashed outlines of its real hit shapes (clip TraceFuture); then it fades.
    //   At0 - 1.5 the halo verdict: the halo snaps to I or II (clip HaloVerdict); that future plays first.
    //   At0 + 1.2k the six steps resolve interleaved: first-1, second-1, first-2, second-2, first-3, second-3. Each goes
    //             coral only 0.5 s before it strikes (no dashed preview): 60, once per player per step.
    //   last step a Heart Toll (40, r 4 m on the boss) resolves on the same tick. P3's plan launches it; when none is on its
    //             way (P5, practice) this cast rings its own with the usual 2.0 s warning.
    //
    // A step is "everything but one pocket" of the dial round the boss (anchor = where he stood at launch):
    //   hour step (P3 steps 1-2): five 60° cones from the boss (Strike) leave one 60° wedge centred on an hour mark, and a
    //             circle on the boss + an outer ring (Ground) leave one band of it: a safe "hour mark, mid or outer".
    //   band step (P3 step 3, every P5 step): the circle and the ring only: a safe band round the boss, at any angle.
    // The authored pairs (seeded: which pair, rotation, mirror, which future first) keep consecutive pockets 1.1-2.4 m
    // apart in both orders: the remembered route is ≤ 2.4 m per 1.2 s step (0.57 s at 4.2 m/s), and it ends beyond the
    // Heart Toll. P3's third steps and every P5 step are bands, so the clock strike on P5 step 2, the Stopped Hours after
    // and the P5 fissure can all be met on the player's own clock line. Without memory a step gives 0.5 s: 1.05 m at
    // 4.2 m/s after a 0.25 s reaction, short of every gap.
    public sealed class TwoFutures : UltimateMechanic
    {
        public override string Id => UltimateMechanics.TwoFutures;
        public override string Name => "Two Futures";

        public const int StepDamage = UltShapes.HeavyDamage;     // 60
        public const float StepGap = 1.2f, TraceSlot = 0.8f, VerdictLead = 1.5f, CoralLead = 0.5f;
        public const float Reach = 15f, ConeHalf = 30f;
        public const string Source = "Two Futures";
        const float MaxAnchor = 1.5f;                             // the dial's anchor stays this close to the centre
        public const float FissureOffset = 1.78f;                 // P5 spots on a NW / SE clock line: this far off the fissure axis
        // P5 step 2 strikes with a clock pair: its striker stands 3.8-7.4 m from the boss (Pendulum Clocks, §3.1)
        public const float ClockMin = 3.8f, ClockMax = 7.4f;

        // ---------------- authored futures ----------------
        // A pocket: Hour = the safe wedge's hour mark (0..11, before the seeded rotation), -1 = a band at any angle;
        // Lo..Hi = the safe radii from the boss (the hit shapes start 0.3 m outside, the players' margin).
        public readonly struct Pocket
        {
            public readonly int Hour; public readonly float Lo, Hi;
            public Pocket(int hour, float lo, float hi) { Hour = hour; Lo = lo; Hi = hi; }
            public bool Band => Hour < 0;
            public override string ToString() => (Band ? "band" : "hour " + (Hour == 0 ? 12 : Hour)) + $" {Lo:0.0}-{Hi:0.0}";
        }
        static Pocket H(int h, float lo, float hi) => new Pocket(h, lo, hi);
        static Pocket B(float lo, float hi) => new Pocket(-1, lo, hi);
        const float MidLo = 4.9f, MidHi = 6.3f, OutLo = 7.9f, OutHi = 9.3f;
        // each pair: Future I steps 1..3, then Future II steps 1..3
        public static readonly Pocket[][] P3Pairs =
        {
            new[] { H(0, MidLo, MidHi), H(1, MidLo, MidHi), B(MidLo, MidHi),  H(1, OutLo, OutHi), H(2, OutLo, OutHi), B(OutLo, OutHi) },
            new[] { H(0, OutLo, OutHi), H(11, OutLo, OutHi), B(OutLo, OutHi), H(1, MidLo, MidHi), H(0, MidLo, MidHi), B(MidLo, MidHi) },
            new[] { H(0, MidLo, MidHi), H(10, MidLo, MidHi), B(MidLo, MidHi), H(11, OutLo, OutHi), H(11, OutLo, OutHi), B(OutLo, OutHi) },
        };
        public static readonly Pocket[][] P5Pairs =
        {
            new[] { B(3.8f, 5.2f), B(3.4f, 4.8f), B(9.0f, 10.4f),  B(6.5f, 7.5f), B(6.3f, 7.5f), B(6.2f, 7.4f) },
            new[] { B(3.7f, 5.1f), B(9.3f, 10.7f), B(9.3f, 10.9f), B(6.4f, 7.6f), B(6.6f, 7.8f), B(6.1f, 7.5f) },
        };
        public static readonly int[] OrderIFirst = { 0, 3, 1, 4, 2, 5 }, OrderIIFirst = { 3, 0, 4, 1, 5, 2 };

        // ---------------- this cast (tests read these) ----------------
        public bool BandFutures { get; private set; }        // the P5 set
        public int PairIndex { get; private set; }
        public int Rotation { get; private set; }            // hours added to the authored hour marks
        public bool Mirrored { get; private set; }
        public bool FirstIsI { get; private set; }           // the halo's verdict
        public Vector3 Anchor { get; private set; }
        public Pocket[] Futures { get; private set; } = new Pocket[0];   // I1, I2, I3, II1, II2, II3 (rotated)
        public int[] Order { get; private set; } = new int[0];          // resolve step k -> index into Futures
        public int VerdictTick { get; private set; } = -1;
        public int[] StepTicks { get; private set; } = new int[0];      // world tick each interleaved step resolves
        public readonly List<int> StepResolves = new List<int>();       // world ticks the steps actually resolved
        public Hazard OwnHeartToll { get; private set; }                // the Heart Toll this cast rang itself, if any
        public int OutlinesShown => outlines.Count;
        public bool HaloShown => halo != null;
        public IReadOnlyList<Hazard> PartsOf(int futureIndex) => parts[futureIndex];

        readonly List<Hazard>[] parts = new List<Hazard>[6];             // per Futures index: its hit shapes
        readonly List<BellkeeperFx.Telegraph> outlines = new List<BellkeeperFx.Telegraph>();
        Transform halo; readonly List<Material> haloMats = new List<Material>();
        readonly Vector3[][] route = new Vector3[2][];                   // P3 guide route: [I first ? 0 : 1][k]
        readonly Dictionary<PlayerController, (int station, Vector3 dir, float[][] r)> rays = new Dictionary<PlayerController, (int, Vector3, float[][])>();

        static readonly Color IvoryFill = new Color(0.35f, 0.38f, 0.45f), IvoryLine = new Color(0.95f, 0.9f, 0.78f);
        static readonly Color BrassFill = new Color(0.36f, 0.3f, 0.2f), BrassLine = new Color(0.86f, 0.66f, 0.3f);

        // -lastbellAbility ult_two_futures: the default loop alternates the P3 (hour) and P5 (band) futures, new seed each time
        public override UltCue DefaultCue() => new UltCue { Variant = "futures", At = new[] { 7f }, Phase = Casts % 2 == 0 ? 3 : 5, Seed = Casts * 7 + 3 };

        // ---------------- geometry ----------------
        // hour h on the dial (12 = north = -Z, 3 = east = -X: the arena's convention, UltimateState.StationDir)
        public static float HourYaw(float h) => 30f * h + 180f;
        public static Vector3 HourDir(float h) => Sim.Dir(HourYaw(h));

        List<Hazard> BuildParts(Pocket k)
        {
            var list = new List<Hazard>();
            if (!k.Band)
                for (int j = 1; j <= 5; j++)
                    list.Add(new Hazard { Shape = HazardShape.Cone, Kind = HazardKind.Strike, Yaw = HourYaw(k.Hour + 2 * j), Radius = Reach, HalfAngle = ConeHalf });
            if (k.Lo - Hazard.Margin > 0.5f)
                list.Add(new Hazard { Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = k.Lo - Hazard.Margin });
            list.Add(new Hazard { Shape = HazardShape.Ring, Kind = HazardKind.Ground, Inner = k.Hi + Hazard.Margin, Radius = Reach });
            foreach (var h in list) { h.Source = Source; h.Origin = Anchor; h.Damage = StepDamage; h.Knockback = 0; h.Stun = 15; h.LockAt = 0; }
            return list;
        }
        // would resolve step k hit a player standing at q?
        public bool StepHits(int k, Vector3 q) => Hits(parts[Order[k]], q);
        // the guide's test: safe with 0.15 m to spare on every side (a player who stops on the edge is hit)
        public bool StepSafeWithSlack(int k, Vector3 q)
        {
            var ps = parts[Order[k]];
            for (int s = 0; s < 5; s++) if (Hits(ps, s == 0 ? q : q + Sim.Dir(90 * s) * Slack)) return false;
            return true;
        }
        public const float Slack = 0.15f;
        static bool Hits(List<Hazard> ps, Vector3 q) { for (int i = 0; i < ps.Count; i++) if (ps[i].Contains(q)) return true; return false; }
        bool SafeDeep(List<Hazard> ps, Vector3 q)
        {
            if (!CombatWorld.InPlayerBound(q, -0.3f) || Sim.Flat(q - Anchor).magnitude < Boss.BodyRadius + PlayerController.Radius + 0.2f) return false;
            for (int s = 0; s < 5; s++) if (Hits(ps, s == 0 ? q : q + Sim.Dir(90 * s) * Slack)) return false;
            return true;
        }

        // ---------------- the cast ----------------
        void Choose()
        {
            var rng = new System.Random(Cue.Seed * 16807 + 11);
            BandFutures = Cue.Phase == 5;
            var set = BandFutures ? P5Pairs : P3Pairs;
            PairIndex = rng.Next(set.Length); Rotation = rng.Next(12); Mirrored = rng.Next(2) == 1; FirstIsI = rng.Next(2) == 0;
            var a = Sim.Flat(Boss.transform.position);
            float maxA = Mathf.Clamp(CombatWorld.PlayerBound - 0.5f - 11.3f, 0, MaxAnchor);
            Anchor = a.magnitude > maxA ? a.normalized * maxA : a;
            Futures = new Pocket[6];
            for (int i = 0; i < 6; i++)
            {
                var k = set[PairIndex][i];
                Futures[i] = k.Band ? k : new Pocket(((Mirrored ? 12 - k.Hour : k.Hour) + Rotation) % 12, k.Lo, k.Hi);
                parts[i] = BuildParts(Futures[i]);
            }
            Order = FirstIsI ? OrderIFirst : OrderIIFirst;
            int t0 = TickAt(Cue.At0(7f));
            StepTicks = new int[6];
            for (int k = 0; k < 6; k++) StepTicks[k] = t0 + Sim.Ticks(StepGap * k);
            VerdictTick = t0 - Sim.Ticks(VerdictLead);
            rays.Clear();
            route[0] = BandFutures ? null : Route(OrderIFirst);
            route[1] = BandFutures ? null : Route(OrderIIFirst);
        }

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            StepResolves.Clear(); OwnHeartToll = null; ClearPresentation();
            Choose();
            if (Boss.Presenter) Boss.Presenter.PlayOnce("TraceFuture", 6 * TraceSlot);
            int slot = Sim.Ticks(TraceSlot), coral = Sim.Ticks(CoralLead), heart = UltShapes.HeartTicks;
            int last = StepTicks[5];
            while (true)
            {
                int t = Now, rel = t - LaunchTick;
                // the trace: one footprint per 0.8 s slot, I1 I2 I3 II1 II2 II3, then it fades
                if (rel % slot == 0 && rel <= 6 * slot)
                {
                    ClearOutlines();
                    int j = rel / slot;
                    if (j < 6) { DrawOutline(j); Boss.ShowAttack($"{Name}: {(j < 3 ? "I" : "II")} · {j % 3 + 1}"); }
                }
                if (t == VerdictTick) Verdict();
                for (int k = 0; k < 6; k++) if (t == StepTicks[k] - coral) SpawnStep(k, coral);
                if (t == last - heart) RingHeartToll(heart);
                if (t > last) { DestroyHalo(); yield break; }
                yield return 1;
            }
        }

        void DrawOutline(int j)
        {
            var fx = W.BossFx;
            foreach (var h in parts[j])
            {
                var t = h.Shape switch
                {
                    HazardShape.Cone => fx.Cone(h.Radius, h.HalfAngle),
                    HazardShape.Ring => fx.Ring(h.Inner, h.Radius),
                    _ => fx.Circle(h.Radius),
                };
                t.Memory();
                if (j < 3) t.Tint(IvoryFill, IvoryLine); else t.Tint(BrassFill, BrassLine);
                t.Place(h.Origin, h.Yaw);
                outlines.Add(t);
            }
        }
        void ClearOutlines() { foreach (var t in outlines) t.Dismiss(); outlines.Clear(); }

        void Verdict()
        {
            Boss.ShowAttack($"{Name}: {(FirstIsI ? "I" : "II")} first");
            if (Boss.Presenter) Boss.Presenter.PlayOnce("HaloVerdict", 0.7f);
            DestroyHalo();
            // stand-in for the halo snapping to I or II: a flat disc over the boss, ivory for I, brass with a dark core for II
            halo = new GameObject("Two Futures halo").transform; halo.SetParent(W.Root, false);
            halo.position = Sim.Flat(Boss.transform.position) + Vector3.up * (Boss.MarkHeight - 0.6f);
            HaloPart(Prim.Make(PrimitiveKind.Cylinder, halo, Vector3.zero, new Vector3(1.5f, 0.04f, 1.5f), FirstIsI ? IvoryLine : BrassLine, 0.6f));
            if (!FirstIsI) HaloPart(Prim.Make(PrimitiveKind.Cylinder, halo, new Vector3(0, 0.05f, 0), new Vector3(0.8f, 0.04f, 0.8f), BrassFill, 0));
        }
        void HaloPart(Transform t) { var r = t.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; haloMats.Add(r.sharedMaterial); }
        void DestroyHalo()
        {
            if (halo) Object.Destroy(halo.gameObject);
            foreach (var m in haloMats) if (m) Object.Destroy(m);
            haloMats.Clear(); halo = null;
        }

        void SpawnStep(int k, int coral)
        {
            var ps = parts[Order[k]];
            foreach (var h in ps)
            {
                h.ResolveAt = UltShapes.ResolveIn(coral);
                bool first = h == ps[0];
                h.OnResolve = hz =>
                {
                    // one step = one hit: whoever this shape hit is spared by the step's other shapes
                    foreach (var p in hz.Hit) foreach (var s in ps) if (s != hz) s.Hit.Add(p);
                    if (first) { StepResolves.Add(W.Tick); Boss.Flare(1.5f); }
                };
                Spawn(h);
            }
        }

        // the last step's partner: P3's plan rings a Heart Toll on that tick; otherwise this cast rings its own
        void RingHeartToll(int warn)
        {
            int at = StepTicks[5];
            foreach (var h in W.Hazards.Live)
                if (h.Source == UltShapes.HeartSource && !h.Cancelled && Now + h.ResolveAt - h.Age - 1 == at) return;
            OwnHeartToll = Spawn(UltShapes.HeartToll(W, Boss.transform.position, UltShapes.ResolveIn(warn)));
        }

        // ---------------- the strat (GUIDE.md) ----------------
        // P3: one route for everyone through the hour pockets, solved once per cast (minimises the longest move).
        Vector3[] Route(int[] order)
        {
            var pts = new List<Vector3>[6];
            for (int k = 0; k < 6; k++)
            {
                pts[k] = new List<Vector3>();
                var f = Futures[order[k]]; float lo = f.Lo + Slack + 0.02f, hi = f.Hi - Slack - 0.02f;
                int rings = Mathf.CeilToInt((hi - lo) / 0.2f);
                for (int ri = 0; ri <= rings; ri++)
                {
                    float r = Mathf.Lerp(lo, hi, ri / (float)Mathf.Max(1, rings));
                    int n = Mathf.Max(12, Mathf.RoundToInt(2 * Mathf.PI * r / 0.2f));
                    for (int i = 0; i < n; i++)
                    {
                        var q = Anchor + Sim.Dir(360f * i / n) * r;
                        if (SafeDeep(parts[order[k]], q) && (k < 5 || Sim.Flat(q - Anchor).magnitude >= TollTideAbility.TollRadius + 0.6f)) pts[k].Add(q);
                    }
                }
            }
            var path = Chain(pts, (a, b) => Sim.Flat(a - b).magnitude, out float worst);
            if (worst > 2.45f) Debug.LogWarning($"Two Futures: authored route needs a {worst:0.00} m step (pair {PairIndex}, rotation {Rotation})");
            return path;
        }
        // DP over candidate spots per step: the chain whose longest move is shortest
        static T[] Chain<T>(List<T>[] c, System.Func<T, T, float> dist, out float worst)
        {
            int n = c.Length; var cost = new float[n][]; var from = new int[n][];
            cost[0] = new float[c[0].Count]; from[0] = new int[c[0].Count];
            for (int k = 1; k < n; k++)
            {
                cost[k] = new float[c[k].Count]; from[k] = new int[c[k].Count];
                for (int i = 0; i < c[k].Count; i++)
                {
                    float best = float.MaxValue; int bi = 0;
                    for (int j = 0; j < c[k - 1].Count; j++) { float v = Mathf.Max(cost[k - 1][j], dist(c[k][i], c[k - 1][j])); if (v < best) { best = v; bi = j; } }
                    cost[k][i] = best; from[k][i] = bi;
                }
            }
            var path = new T[n]; int at = 0; worst = float.MaxValue;
            for (int i = 0; i < cost[n - 1].Length; i++) if (cost[n - 1][i] < worst) { worst = cost[n - 1][i]; at = i; }
            for (int k = n - 1; k >= 0; k--) { if (c[k].Count == 0) { worst = float.MaxValue; break; } path[k] = c[k][at]; if (k > 0) at = from[k][at]; }
            return path;
        }

        // P5: each player walks the bands along their own clock line (the far side of the boss from their clock station:
        // the clock spot and the Stopped Hour hiding strip), so step 2's clock strike and the stop after are met on it.
        float[] RayRadii(int[] order, bool besideFissure)
        {
            var cand = new List<float>[6];
            for (int k = 0; k < 6; k++)
            {
                var f = Futures[order[k]]; cand[k] = new List<float>();
                for (float r = f.Lo + 0.2f; r <= f.Hi - 0.2f + 1e-3f; r += 0.1f)
                    if ((k != 1 || (r >= ClockMin + 0.1f && r <= ClockMax - 0.1f)) && (k < 5 || r >= TollTideAbility.TollRadius + 0.6f)) cand[k].Add(r);
            }
            // beside the fissure the spots sit FissureOffset off the line, so a radial change costs a little more
            System.Func<float, float> along = r => besideFissure ? Mathf.Sqrt(Mathf.Max(0, r * r - FissureOffset * FissureOffset)) : r;
            return Chain(cand, (a, b) => Mathf.Abs(along(a) - along(b)), out _);
        }
        (int station, Vector3 dir, float[][] r) RayOf(PlayerController p)
        {
            int st = Ult.StationOf(p);
            if (rays.TryGetValue(p, out var c) && c.station == st) return c;
            Vector3 dir;
            if (st >= 0) dir = -UltimateState.StationDir(st);
            else { int i = Mathf.Max(0, W.Players.IndexOf(p)), n = Mathf.Max(1, W.Players.Count); dir = Sim.Dir(360f * i / n + 20f); }
            bool beside = Mathf.Abs(Vector3.Dot(dir, UltimateState.FissureNormal)) < 0.35f;
            c = (st, dir, new[] { RayRadii(OrderIFirst, beside), RayRadii(OrderIIFirst, beside) });
            rays[p] = c; return c;
        }
        Vector3 OnRay(Vector3 dir, float r)
        {
            var q = Anchor + dir * r;
            // a clock line along the fissure (NW / SE): stand beside it, clear of the 6 s toll flares (FractureOfTime.InFlare,
            // 1.5 m; the Stopped Hour's P5 strip is 1.65-1.9 m off the axis), at the same distance from the boss
            var nrm = UltimateState.FissureNormal;
            if (Mathf.Abs(Vector3.Dot(dir, nrm)) < 0.35f)
                for (int i = 0; i < 6; i++) { float s = Vector3.Dot(q, nrm); q += nrm * ((s >= 0 ? FissureOffset : -FissureOffset) - s); q = Anchor + Sim.Flat(q - Anchor).normalized * r; }
            return q;
        }

        // the guide's spot for player p at resolve step k (this cast's verdict), and before the verdict (between both
        // futures' first steps)
        public Vector3 HintFor(PlayerController p, int k) => HintFor(p, k, FirstIsI);
        public Vector3 HintFor(PlayerController p, int k, bool iFirst)
        {
            int o = iFirst ? 0 : 1;
            if (!BandFutures) return route[o][k];
            var ray = RayOf(p); return OnRay(ray.dir, ray.r[o][k]);
        }
        public Vector3 PrepositionFor(PlayerController p)
        {
            if (!BandFutures) return (route[0][0] + route[1][0]) * 0.5f;
            var ray = RayOf(p); return OnRay(ray.dir, (ray.r[0][0] + ray.r[1][0]) * 0.5f);
        }
        // longest single move of p's guide route for a verdict (tests)
        public float RouteMaxStep(PlayerController p, bool iFirst)
        {
            float m = 0; for (int k = 1; k < 6; k++) m = Mathf.Max(m, Sim.Flat(HintFor(p, k, iFirst) - HintFor(p, k - 1, iFirst)).magnitude);
            return m;
        }

        // GUIDE.md: memorise both futures (three safe hour marks or bands each), stand between both first steps, and on the
        // verdict walk the interleaved path, ending beyond the Heart Toll. Since = the verdict for the steps (their ticks
        // depend on it); the end of Future II's first trace for the pre-position.
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || StepTicks.Length < 6 || p.Down || Now > StepTicks[5]) return;
            if (Now <= VerdictTick)
            {
                var pre = PrepositionFor(p);
                into.Add(new BotDemand { Tick = VerdictTick, Since = LaunchTick + Sim.Ticks(4 * TraceSlot), Ok = q => Sim.Flat(q - pre).magnitude <= 1.5f, Hint = pre, Memory = true, Why = "Two Futures: between both first steps", From = this });
            }
            for (int k = 0; k < 6; k++)
            {
                if (StepTicks[k] < Now) continue;
                int step = k;
                into.Add(new BotDemand { Tick = StepTicks[k], Since = VerdictTick, Ok = q => StepSafeWithSlack(step, q), Hint = HintFor(p, k), Memory = true, Why = $"Two Futures step {k + 1}", From = this });
            }
        }

        public override void Cancel()
        {
            base.Cancel();
            ClearPresentation();
            rays.Clear();
        }
        void ClearPresentation() { ClearOutlines(); DestroyHalo(); }
    }
}
