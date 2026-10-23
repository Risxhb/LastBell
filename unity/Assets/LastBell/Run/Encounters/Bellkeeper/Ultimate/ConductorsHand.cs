using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Conductor's Hand (`bellkeeper.ult_conductors_hand`), Bellkeeper Ultimate — proposal, built 2026-10-23, not
    // human-approved. Design: ultimate/ULTIMATE.md §3.6, §5.3, §5.6, §5.7; strat: GUIDE.md "The Conductor's Hand".
    //
    // The left hand rises and shows 1-4 fingers; the right pendulum hand swings left or right. Together they are a phrase:
    //   fingers = move 1: 1 Leaning Bell, 2 Toll Tide, 3 Reflection, 4 Stopped Hour
    //   swing   = move 2: left = Heart Toll, right = the first move's footprint again, rewound ("backwards")
    // The phrase shows on the launch tick; each move's real telegraph appears only TellTicks (1.5 s) before it resolves,
    // shorter than the moves need on their own, so the phrase is how you get there in time.
    //
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the phrase. At[0] = move 1 resolves, At[1] = move 2 resolves
    // (P2 / P5: 7.0, 8.5; P4: 7.0, 9.5). Variant "phrase" (the seeded phrase) or "phrase:<fingers><L|R>" (tests,
    // practice: "phrase:3R"). The phrase per loop (§7): the four finger counts in a seeded order per phase, one per loop;
    // the swing from Cue.Seed. Identical for any N (Reflection images are per player, as always).
    //
    // Build decisions (report): every move is built here from UltShapes (1.5 s ResolveAt) so it can overlap anything;
    // move 4 launches the real Stopped Hour mechanic with stop = At[0] - 2.0 s and resume = At[0] (its glyph shows with
    // the phrase, 5 s before the stop), and is skipped (logged) if that mechanic is still running.
    // - Leaning Bell: the hit half is fixed by the phrase, on the half that holds the party (direction from the centre to
    //   the standing party's mean position, snapped to 45°; the raised hand leans that way). P5: the fissure line, toward
    //   the party's majority (as the plan's Leaning Bells). So the reader always has to cross: 13.4 m in 7.0 s.
    // - Reflection: one mirrored image per standing player (2·boss − player), tracking until 1.0 s before it strikes;
    //   solo adds an image locked on your own spot. Gather on one side before the telegraph.
    // - Right swing = Backwards Bell's rule, "a rewound copy strikes everywhere its original did not", through its
    //   RewindOf (40, its arrows): the other Leaning half; the disc inside the tide (huggers too); the Reflection images
    //   again (invert: false); the Stopped Hour's chain lanes as the same lanes, no longer stopped by the boss's body.
    public sealed class ConductorsHand : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ConductorsHand;
        public override string Name => "The Conductor's Hand";

        public const int TellTicks = 90;             // real telegraphs: 1.5 s before each resolve
        public const int StopLeadTicks = 120;        // move 4: the Stopped Hour stops 2.0 s before time resumes on move 1
        public const float NearLine = 2.0f, NearTideEdge = 4.6f;   // 1R / 2R: where a reader waits to step across in time
        public const int ImageLockBefore = 60;       // Reflection images lock 1.0 s before they strike
        public const int RecoveryTicks = 30;
        public const float HeartClear = TollTideAbility.TollRadius + Hazard.Margin + 0.2f;   // the guide's 4.3 m+ band edge (4.5)
        public const float GatherDepth = 2.0f;       // Reflection: stand this far on the party's side of the boss
        public const string RewindPrefix = "Rewound ", ImageSource = "Reflection of the Hour";
        public static readonly string[] MoveNames = { "", "Leaning Bell", "Toll Tide", "Reflection", "Stopped Hour" };

        // ---------------- the phrase (§7: the four finger counts in a seeded order per phase, the swing from the seed) ----------------
        // practice (-lastbellAbility) walks every phrase: 1L 2R 3L 4R 1R 2L 3R 4L
        static readonly (int f, bool r)[] PracticeOrder = { (1, false), (2, true), (3, false), (4, true), (1, true), (2, false), (3, true), (4, false) };
        public static (int fingers, bool right) PhraseFor(int phase, int loop, int seed)
        {
            if (phase <= 0) return PracticeOrder[((loop - 1) % PracticeOrder.Length + PracticeOrder.Length) % PracticeOrder.Length];
            var order = new[] { 1, 2, 3, 4 };
            uint h = Hash((uint)phase * 2654435761u + 17u);
            for (int i = 3; i > 0; i--) { h = Hash(h); int j = (int)(h % (uint)(i + 1)); (order[i], order[j]) = (order[j], order[i]); }
            int k = ((loop - 1) % 4 + 4) % 4;
            return (order[k], (Hash((uint)seed ^ 0x9E3779B9u) & 1) == 1);
        }
        static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
        public static bool TryParse(string variant, out int fingers, out bool right)
        {
            fingers = 0; right = false;
            if (string.IsNullOrEmpty(variant)) return false;
            int c = variant.IndexOf(':'); if (c < 0) return false;
            var s = variant.Substring(c + 1).Trim().ToUpperInvariant();
            if (s.Length != 2 || s[0] < '1' || s[0] > '4' || (s[1] != 'L' && s[1] != 'R')) return false;
            fingers = s[0] - '0'; right = s[1] == 'R';
            return true;
        }

        // ---------------- state of the current cast (tests) ----------------
        public int Fingers { get; private set; }
        public bool SwingRight { get; private set; }
        public string Phrase => Fingers == 0 ? "" : $"{Fingers}{(SwingRight ? "R" : "L")}";
        public string Reading => Fingers == 0 ? "" : $"{MoveNames[Fingers]} then {(SwingRight ? "rewound " + MoveNames[Fingers] : "Heart Toll")}";
        public Vector3 LeanDir { get; private set; }         // the Leaning Bell's hit half (and the side the party stands on)
        public int FirstAt { get; private set; } = -1;       // world tick move 1 resolves
        public int SecondAt { get; private set; } = -1;      // world tick move 2 resolves
        public readonly List<Hazard> First = new List<Hazard>(), Second = new List<Hazard>();
        public string StoppedChoice { get; private set; } = "";
        public bool PhraseShown => fx != null && fx.Alive;

        PhraseFx fx;
        Hazard leanProbe, tideProbe, leanBack, tideBack;   // move 1's shapes and their rewinds, known from the phrase
        readonly List<Hazard> stopLanes = new List<Hazard>();
        bool capturing;
        int partySize;

        public override UltCue DefaultCue() => new UltCue { Variant = "phrase", At = new[] { 7f, 8.5f } };

        protected override void OnBind()
        {
            Boss.HazardSpawned += (a, h) => { if (capturing && a is StoppedHour && h.Shape == HazardShape.Lane) stopLanes.Add(h); };
        }

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            First.Clear(); Second.Clear(); stopLanes.Clear(); StoppedChoice = "";
            int t1 = Mathf.Max(1, Sim.Ticks(Cue.AtOr(0, 7f))), t2 = Mathf.Max(t1 + 1, Sim.Ticks(Cue.AtOr(1, Cue.AtOr(0, 7f) + 1.5f)));
            FirstAt = LaunchTick + t1; SecondAt = LaunchTick + t2;
            if (!TryParse(Cue.Variant, out int f, out bool r))
                (f, r) = PhraseFor(Cue.Phase, Boss.UltPlanActive ? Mathf.Max(1, Boss.SegmentLoops) : Casts, Cue.Seed);
            Fingers = f; SwingRight = r;
            partySize = StandingCount;
            LeanDir = PickLean();
            leanProbe = UltShapes.LeaningHalf(W, LeanDir, UltShapes.ResolveIn(TellTicks));
            tideProbe = UltShapes.Tide(W, UltShapes.ResolveIn(TellTicks));
            leanBack = BackwardsBell.Rewound(leanProbe, 1, UltShapes.StandardDamage, true);
            tideBack = BackwardsBell.Rewound(tideProbe, 1, UltShapes.StandardDamage, true);
            Boss.PlanLog.Add((Now, $"hand phrase {Phrase}"));

            if (Fingers == 4)
            {
                // the Stopped Hour: its glyph shows with the phrase, the stop starts StopLeadTicks before move 1, time resumes on move 1
                var stopped = Boss.Mechanic<StoppedHour>();
                capturing = true;
                var cue = new UltCue { Variant = "lash", At = new[] { Mathf.Max(0, t1 - StopLeadTicks) / (float)Sim.Hz, t1 / (float)Sim.Hz }, Phase = Cue.Phase, Seed = Cue.Seed };
                StoppedChoice = stopped != null && Boss.UltLaunch(stopped, cue) ? "launched" : "skipped (busy)";
                Boss.PlanLog.Add((Now, "hand stopped_hour " + StoppedChoice));
            }

            fx = PhraseFx.Create(W, Fingers, SwingRight);
            if (Boss.Presenter) Boss.Presenter.PlayOnce("ConductPhrase", Mathf.Max(1, t1 - TellTicks) / (float)Sim.Hz);
            int s1 = Mathf.Max(0, t1 - TellTicks), s2 = Mathf.Max(0, t2 - TellTicks);
            for (int i = 0; i <= t2; i++)
            {
                if (i == s1 && Fingers != 4) SpawnFirst(t1 - i);
                if (i == s2) SpawnSecond(t2 - i);
                if (i >= s1 && i <= t1 && Fingers == 1) Boss.Face(Quaternion.LookRotation(LeanDir).eulerAngles.y, 120);
                if (i >= s1) Boss.Glow(Mathf.Clamp01((i - s1) / (float)Mathf.Max(1, t2 - s1)));
                fx?.Step(Boss.transform.position, Fingers == 1 ? LeanDir : Vector3.zero, i);
                yield return 1;
            }
            capturing = false;
            fx?.Clear(); fx = null;
            yield return RecoveryTicks;
        }

        // the hit half: the party's side (direction to the standing party's mean, snapped to 45°); P5: the fissure line
        Vector3 PickLean()
        {
            if (Cue.Phase == 5)
            {
                var n = UltimateState.FissureNormal; int plus = 0, minus = 0;
                foreach (var p in W.Players) { if (p.Down) continue; if (Vector3.Dot(Sim.Flat(p.transform.position), n) > 0) plus++; else minus++; }
                return minus > plus ? -n : n;
            }
            var mean = Vector3.zero; int k = 0;
            foreach (var p in W.Players) { if (p.Down) continue; mean += Sim.Flat(p.transform.position); k++; }
            if (k > 0) mean /= k;
            if (mean.magnitude < 0.75f) return UltimateState.StationDir(Mathf.Abs(Cue.Seed) % 4);
            float yaw = Mathf.Round(Quaternion.LookRotation(mean).eulerAngles.y / 45f) * 45f;
            return Sim.Dir(yaw);
        }

        void SpawnFirst(int ticks)
        {
            int R = UltShapes.ResolveIn(ticks);
            switch (Fingers)
            {
                case 1:
                    leanProbe.ResolveAt = R; First.Add(Spawn(leanProbe));
                    if (Boss.Presenter) Boss.Presenter.PlayOnce("BellSweep", R / (float)Sim.Hz);
                    break;
                case 2:
                    tideProbe.ResolveAt = R; First.Add(Spawn(tideProbe));
                    if (Boss.Presenter) Boss.Presenter.PlayOnce("ShatteredChime", R / (float)Sim.Hz);
                    break;
                case 3:
                    float radius = Boss.UltMirror.RadiusFor(StandingCount); bool solo = StandingCount == 1;
                    foreach (var p in W.Players.ToArray())
                    {
                        if (p.Down) continue;
                        var who = p;
                        First.Add(Spawn(Image(Boss.UltMirror.MirrorOf(p.transform.position), radius, R, Mathf.Max(0, R - ImageLockBefore), ImageSource,
                            hz => { if (who && !who.Down) hz.Origin = Boss.UltMirror.MirrorOf(who.transform.position); })));
                        if (solo) First.Add(Spawn(Image(ReflectionAbility.ClampToBound(p.transform.position), radius, R, 0, ImageSource, null)));
                    }
                    if (Boss.Presenter) Boss.Presenter.PlayOnce("EchoTell", R / (float)Sim.Hz);
                    break;
            }
        }

        void SpawnSecond(int ticks)
        {
            int R = UltShapes.ResolveIn(ticks);
            if (!SwingRight)
            {
                Second.Add(Spawn(UltShapes.HeartToll(W, Boss.transform.position, R)));
                if (Boss.Presenter) Boss.Presenter.PlayOnce("HollowToll", R / (float)Sim.Hz);
                return;
            }
            // the first move again, rewound (Backwards Bell's rule: it strikes everywhere the original did not)
            switch (Fingers)
            {
                case 1: Back(leanProbe, true, R); break;                  // the other half
                case 2: Back(tideProbe, true, R); break;                  // the disc inside the tide
                case 3: foreach (var img in First.ToArray()) Back(img, false, R); break;   // the images again
                case 4: foreach (var lane in StopLanesNow()) Back(lane, false, R); break;  // the chains again, past the boss
            }
            if (Boss.Presenter) Boss.Presenter.PlayOnce("ReverseToll", R / (float)Sim.Hz);
        }
        // a rewound copy resolving on SecondAt, through Backwards Bell's RewindOf (its hazard, its arrows); our own copy if absent
        void Back(Hazard shape, bool invert, int resolveAt)
        {
            var bb = Boss.Mechanic<BackwardsBell>();
            var h = bb != null ? bb.RewindOf(shape, SecondAt, invert) : Spawn(BackwardsBell.Rewound(shape, resolveAt, UltShapes.StandardDamage, invert));
            if (h != null) Second.Add(h);
        }

        Hazard Image(Vector3 at, float radius, int resolveAt, int lockAt, string source, System.Action<Hazard> track)
        {
            var h = new Hazard
            {
                Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = radius,
                LockAt = lockAt, ResolveAt = resolveAt, Damage = UltShapes.StandardDamage, Knockback = 1.5f, Stun = 15, Track = track,
            };
            h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Boss.Flare(0.6f); };
            return h;
        }

        // The Stopped Hour's chain lanes (their rewinds no longer stop at the boss's body). If the Stopped Hour drew
        // none (skipped, or not built yet), a lane from each standing player's station (Ult.StationOf, else a seeded one)
        // through where they stand.
        List<Hazard> StopLanesNow()
        {
            var lanes = new List<Hazard>();
            foreach (var l in stopLanes)
                if (!l.Cancelled) lanes.Add(l);   // rewound as the same lane (a plain lane: no first-body stop)
            if (lanes.Count > 0) return lanes;
            int i = 0;
            foreach (var p in W.Players)
            {
                if (p.Down) { i++; continue; }
                int st = Ult.StationOf(p); if (st < 0) st = (i + Mathf.Abs(Cue.Seed)) % 4;
                var from = UltimateState.Station(st); var dir = Sim.Flat(p.transform.position - from);
                if (dir.sqrMagnitude < 1e-4f) dir = -from;
                lanes.Add(PlainLane(from, Quaternion.LookRotation(dir).eulerAngles.y, 30f, 1.6f));
                i++;
            }
            return lanes;
        }
        static Hazard PlainLane(Vector3 origin, float yaw, float length, float width) => new Hazard
        {
            Source = "Chain", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = Sim.Flat(origin), Yaw = yaw,
            Length = length, Width = width, LockAt = 0, ResolveAt = 1, Damage = UltShapes.StandardDamage, Knockback = 1.5f, Stun = 15,
        };

        // ---------------- the strat (GUIDE.md): from the phrase, be where both moves' safe spots are on their ticks ----------------
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || Fingers == 0 || p.Down) return;
            int since = LaunchTick;
            var pos = Sim.Flat(p.transform.position);
            var bp = Sim.Flat(Boss.transform.position);
            var tideHint = pos.magnitude > 5.5f ? pos.normalized * 5.5f : pos;
            if (Now <= FirstAt)
            {
                switch (Fingers)
                {
                    case 1:
                        // right swing: the other half strikes 1.5 s later, so wait near the line
                        into.Add(new BotDemand { Tick = FirstAt, Since = since, Ok = q => Clear(leanProbe, q) && (!SwingRight || Vector3.Dot(q, LeanDir) >= -NearLine),
                            Hint = SafeHalfHint(pos, SwingRight ? 1.2f : 2.5f), Memory = true, Why = "phrase: Leaning Bell", From = this });
                        break;
                    case 2:
                        // right swing: the disc inside the tide strikes 1.5 s later, so wait near the tide's edge
                        into.Add(new BotDemand { Tick = FirstAt, Since = since, Ok = q => Clear(tideProbe, q) && (!SwingRight || q.magnitude >= NearTideEdge),
                            Hint = SwingRight ? Dir(pos) * 5.8f : tideHint, Memory = true, Why = "phrase: Toll Tide", From = this });
                        break;
                    case 3:
                        // gather on the party's side (co-op); clear of the images once drawn (solo: your own spot's image too)
                        into.Add(new BotDemand { Tick = FirstAt, Since = since,
                            Ok = q => First.All(h => Clear(h, q)) && (partySize < 2 || Vector3.Dot(Sim.Flat(q - Boss.transform.position), LeanDir) >= GatherDepth),
                            Hint = partySize < 2 ? pos : bp + LeanDir * 5f, Memory = true, Why = "phrase: Reflection (gather)", From = this });
                        break;
                }
            }
            if (Now > SecondAt) return;
            if (!SwingRight)
            {
                into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => Sim.Flat(q - Boss.transform.position).magnitude >= HeartClear,
                    Hint = bp + (pos - bp).normalized * 5.2f, Memory = true, Why = "phrase: Heart Toll", From = this });
                return;
            }
            switch (Fingers)
            {
                case 1:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => Clear(leanBack, q), Hint = pos + LeanDir * (Mathf.Max(0, -Vector3.Dot(pos, LeanDir)) + 1.4f),
                        Memory = true, Why = "phrase: rewound Leaning Bell (the other half)", From = this });
                    break;
                case 2:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => Clear(tideBack, q), Hint = Dir(pos) * 8.2f,
                        Memory = true, Why = "phrase: rewound Toll Tide (the inner disc)", From = this });
                    break;
                case 3:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since,
                        Ok = q => First.All(h => Clear(h, q)) && (partySize < 2 || Vector3.Dot(Sim.Flat(q - Boss.transform.position), LeanDir) >= GatherDepth),
                        Hint = partySize < 2 ? pos : bp + LeanDir * 5f, Memory = true, Why = "phrase: rewound Reflection", From = this });
                    break;
                case 4:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => Second.Count > 0 ? Second.All(l => Clear(l, q)) : stopLanes.All(l => l.Cancelled || Clear(PlainLane(l.Origin, l.Yaw, l.Length, l.Width), q)),
                        Hint = pos, Memory = true, Why = "phrase: rewound chains", From = this });
                    break;
            }
        }
        // outside h with a 0.3 m step to spare (the bot stops up to 0.08 m short of a target on a shape's edge)
        const float ClearMargin = 0.3f;
        static bool Clear(Hazard h, Vector3 q)
        {
            if (h.Contains(q)) return false;
            for (int k = 0; k < 8; k++) if (h.Contains(q + Sim.Dir(45 * k) * ClearMargin)) return false;
            return true;
        }
        // the reader's spot for a Leaning half: across the line, at least 2.5 m into the safe half
        static Vector3 Dir(Vector3 pos) => pos.sqrMagnitude > 0.01f ? Sim.Flat(pos).normalized : Vector3.forward;
        Vector3 SafeHalfHint(Vector3 pos, float depth)
        {
            float s = Vector3.Dot(pos, LeanDir);
            var q = pos - LeanDir * (s + depth);
            float b = CombatWorld.PlayerBound - 0.8f; if (q.magnitude > b) q = q.normalized * b;
            return q;
        }

        public override void Cancel()
        {
            fx?.Clear(); fx = null;
            capturing = false; stopLanes.Clear(); First.Clear(); Second.Clear();
            Fingers = 0; FirstAt = SecondAt = -1;
            base.Cancel();
        }

        // ---------------- presentation (stand-in until the rig clip ConductPhrase exists) ----------------
        // By the boss, readable at the game camera: on the screen-left, the conducting hand rises with 1-4 oversized
        // fingers lit gold (folded fingers stay dark brass); on the screen-right, the pendulum hand (brass rod, ivory bob)
        // swings out to the screen-left or screen-right and holds there. For a Leaning Bell phrase the hand leans toward
        // the hit half (low and in front of him, facing the camera). Rewound copies carry Backwards Bell's arrows. Never coral.
        sealed class PhraseFx
        {
            static Material brass, gold, ivory, dark;
            readonly GameObject root;
            readonly Transform hand, rod, bob;
            readonly bool right;
            const float HandHeight = 1.2f, Side = 2.3f, Front = 3.6f, RodLength = 1.7f, PivotHeight = 3.0f, SwingDeg = 42f;   // low and in front of the boss: on screen when he is at the top edge
            public bool Alive => root;

            PhraseFx(CombatWorld w, int fingers, bool right)
            {
                this.right = right;
                if (brass == null)
                {
                    brass = Prim.Material(new Color(0.78f, 0.6f, 0.3f), 0.3f); gold = Prim.Material(new Color(1f, 0.8f, 0.36f), 2.6f);
                    ivory = Prim.Material(new Color(0.95f, 0.92f, 0.84f), 0.6f); dark = Prim.Material(new Color(0.45f, 0.36f, 0.2f));
                }
                root = new GameObject("Conductor's Hand phrase");
                if (w.Root) root.transform.SetParent(w.Root, false);
                hand = new GameObject("left hand").transform; hand.SetParent(root.transform, false);
                Part(PrimitiveKind.Cube, hand, new Vector3(0, 0, 0), new Vector3(1.5f, 1.1f, 0.3f), brass);            // palm
                Part(PrimitiveKind.Cylinder, hand, new Vector3(0, -0.85f, 0), new Vector3(0.6f, 0.35f, 0.3f), brass);  // wrist
                for (int i = 0; i < 4; i++)
                {
                    float x = -0.54f + 0.36f * i;
                    if (i < fingers) Part(PrimitiveKind.Cube, hand, new Vector3(x, 1.35f, 0), new Vector3(0.26f, 1.6f, 0.28f), gold);   // raised, lit
                    else Part(PrimitiveKind.Cube, hand, new Vector3(x, 0.62f, 0.2f), new Vector3(0.26f, 0.3f, 0.36f), dark);          // folded
                }
                rod = Part(PrimitiveKind.Cube, root.transform, Vector3.zero, new Vector3(0.14f, RodLength, 0.14f), brass);
                bob = Part(PrimitiveKind.Sphere, root.transform, Vector3.zero, Vector3.one * 0.8f, ivory);
            }
            public static PhraseFx Create(CombatWorld w, int fingers, bool right) => new PhraseFx(w, fingers, right);

            static Transform Part(PrimitiveKind k, Transform parent, Vector3 pos, Vector3 scale, Material m)
            {
                var go = new GameObject(k.ToString()); go.transform.SetParent(parent, false);
                go.transform.localPosition = pos; go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(k);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go.transform;
            }

            // tick: ticks since the phrase was shown
            public void Step(Vector3 boss, Vector3 lean, int tick)
            {
                if (!root) return;
                var cam = Camera.main;
                var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.back; if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.back;
                var scr = Vector3.Cross(Vector3.up, fwd);   // screen right, on the floor
                // billboard toward the camera: fingers point screen-up, the pendulum swings in the screen plane
                var camUp = cam ? cam.transform.up : Vector3.up;
                var camRight = cam ? cam.transform.right : scr;
                var face = cam ? Quaternion.LookRotation(-cam.transform.forward, camUp) : Quaternion.LookRotation(-fwd);
                boss = Sim.Flat(boss);
                // the hand rises over 0.5 s and holds; a Leaning Bell phrase leans it toward the hit half
                float rise = Mathf.SmoothStep(0, 1, Mathf.Clamp01(tick / 30f));
                var at = boss - fwd * Front - scr * Side + Vector3.up * Mathf.Lerp(0.4f, HandHeight, rise);
                var tilt = Quaternion.identity;
                if (lean.sqrMagnitude > 0.5f) { at += lean * 1.4f * rise; tilt = Quaternion.AngleAxis(24f * rise, Vector3.Cross(Vector3.up, lean)); }
                hand.SetPositionAndRotation(at, tilt * face);
                hand.localScale = Vector3.one * 1.25f;
                // the pendulum swings out from 0.3 s, eases over 0.5 s, then holds with a faint tremble
                float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01((tick - 18) / 30f));
                float deg = (right ? 1 : -1) * SwingDeg * u + 2f * Mathf.Sin(tick * 0.21f) * u;
                var pivot = boss - fwd * Front + scr * Side + Vector3.up * PivotHeight;
                float a = deg * Mathf.Deg2Rad;
                var down = -camUp * Mathf.Cos(a) + camRight * Mathf.Sin(a);
                var tip = pivot + down * RodLength;
                rod.position = (pivot + tip) / 2; rod.rotation = Quaternion.FromToRotation(Vector3.up, -down);
                bob.position = tip;
            }


            public void Clear()
            {
                if (root) Object.Destroy(root);
            }
        }
    }
}
