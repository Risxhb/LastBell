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
    // The phrase shows on the launch tick; each move's real telegraph appears only TellTicks (2.0 s) before it resolves,
    // shorter than the moves need on their own, so the phrase is how you get there in time.
    //
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the phrase. At[0] = move 1 resolves, At[1] = move 2 resolves
    // (P2 / P5: 7.0, 8.5; P4: 7.0, 9.5). Variant "phrase" (the seeded phrase) or "phrase:<fingers><L|R>" (tests,
    // practice: "phrase:3R"). The phrase per loop (§7): the four finger counts in a seeded order per phase, one per loop;
    // the swing from Cue.Seed. Identical for any N (Reflection images are per player, as always).
    //
    // Build decisions (report): every move is built here from UltShapes (2.0 s ResolveAt) so it can overlap anything;
    // move 4 launches the real Stopped Hour mechanic with stop = At[0] - 2.0 s and resume = At[0] (its glyph shows with
    // the phrase, 5 s before the stop), and is skipped (logged) if that mechanic is still running.
    // - Leaning Bell: the hit half is fixed by the phrase, on the half that holds the party (direction from the centre to
    //   the standing party's mean position, snapped to 45°; the raised hand leans that way). P5: the fissure line, toward
    //   the party's majority (as the plan's Leaning Bells). So the reader always has to cross: 13.4 m in 7.0 s.
    // - Reflection: one mirrored image per standing player (2·boss − player), tracking until 1.0 s before it strikes;
    //   solo adds an image locked on your own spot. Gather on one side before the telegraph.
    // - Backwards copies: the same footprints again (Leaning half, tide ring, the images' locked spots, the Stopped
    //   Hour's chain lanes as plain lanes that no longer stop at the boss's body), 40 damage, with ivory rewind marks.
    public sealed class ConductorsHand : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ConductorsHand;
        public override string Name => "The Conductor's Hand";

        public const int TellTicks = 120;            // real telegraphs: 2.0 s before each resolve
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
        public int RewindMarks => fx != null ? fx.Marks : 0;

        PhraseFx fx;
        Hazard leanProbe, tideProbe;
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
            Boss.PlanLog.Add((Now, $"hand phrase {Phrase}"));

            if (Fingers == 4)
            {
                // the Stopped Hour: its glyph shows with the phrase, the stop starts 2.0 s before move 1, time resumes on move 1
                var stopped = Boss.Mechanic<StoppedHour>();
                capturing = true;
                var cue = new UltCue { Variant = "lash", At = new[] { Mathf.Max(0, t1 - TellTicks) / (float)Sim.Hz, t1 / (float)Sim.Hz }, Phase = Cue.Phase, Seed = Cue.Seed };
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
            // the first move again, rewound: the same footprint(s), with ivory rewind marks
            int dmg = UltShapes.StandardDamage;
            switch (Fingers)
            {
                case 1:
                    Second.Add(Spawn(UltShapes.LeaningHalf(W, LeanDir, R, dmg, RewindPrefix + UltShapes.LeanSource)));
                    fx?.Mark(LeanDir * 6.5f);
                    break;
                case 2:
                    Second.Add(Spawn(UltShapes.Tide(W, R, dmg, RewindPrefix + UltShapes.TideSource)));
                    for (int k = 0; k < 4; k++) fx?.Mark(UltimateState.StationDir(k) * 9.5f);
                    break;
                case 3:
                    foreach (var img in First.ToArray())
                    {
                        Second.Add(Spawn(Image(img.Origin, img.Radius, R, 0, RewindPrefix + ImageSource, null)));
                        fx?.Mark(img.Origin);
                    }
                    break;
                case 4:
                    foreach (var lane in StopLanesNow())
                    {
                        lane.ResolveAt = R;
                        Second.Add(Spawn(lane));
                        fx?.Mark(lane.Origin + Sim.Dir(lane.Yaw) * 4f);
                    }
                    break;
            }
            if (Boss.Presenter) Boss.Presenter.PlayOnce("ReverseToll", R / (float)Sim.Hz);
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

        // The Stopped Hour's chain lanes as plain lanes (they no longer stop at the boss's body). If the Stopped Hour drew
        // none (skipped, or not built yet), a lane from each standing player's station (Ult.StationOf, else a seeded one)
        // through where they stand.
        List<Hazard> StopLanesNow()
        {
            var lanes = new List<Hazard>();
            foreach (var l in stopLanes)
                if (!l.Cancelled) lanes.Add(PlainLane(l.Origin, l.Yaw, l.Length, l.Width));
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
            Source = RewindPrefix + "Chain", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = Sim.Flat(origin), Yaw = yaw,
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
                        into.Add(new BotDemand { Tick = FirstAt, Since = since, Ok = q => !leanProbe.Contains(q), Hint = SafeHalfHint(pos), Memory = true, Why = "phrase: Leaning Bell", From = this });
                        break;
                    case 2:
                        into.Add(new BotDemand { Tick = FirstAt, Since = since, Ok = q => !tideProbe.Contains(q), Hint = tideHint, Memory = true, Why = "phrase: Toll Tide", From = this });
                        break;
                    case 3:
                        if (partySize >= 2)
                            into.Add(new BotDemand { Tick = FirstAt, Since = since, Ok = q => Vector3.Dot(Sim.Flat(q - Boss.transform.position), LeanDir) >= GatherDepth,
                                Hint = bp + LeanDir * 5f, Memory = true, Why = "phrase: Reflection (gather)", From = this });
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
                    into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => !leanProbe.Contains(q), Hint = SafeHalfHint(pos), Memory = true, Why = "phrase: rewound Leaning Bell", From = this });
                    break;
                case 2:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => !tideProbe.Contains(q), Hint = tideHint, Memory = true, Why = "phrase: rewound Toll Tide", From = this });
                    break;
                case 3:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since,
                        Ok = q => First.All(h => !h.Contains(q)) && (partySize < 2 || Vector3.Dot(Sim.Flat(q - Boss.transform.position), LeanDir) >= GatherDepth),
                        Hint = partySize < 2 ? pos : bp + LeanDir * 5f, Memory = true, Why = "phrase: rewound Reflection", From = this });
                    break;
                case 4:
                    into.Add(new BotDemand { Tick = SecondAt, Since = since, Ok = q => stopLanes.All(l => l.Cancelled || !PlainLane(l.Origin, l.Yaw, l.Length, l.Width).Contains(q)),
                        Hint = pos, Memory = true, Why = "phrase: rewound chains", From = this });
                    break;
            }
        }
        // the reader's spot for a Leaning half: across the line, at least 2.5 m into the safe half
        Vector3 SafeHalfHint(Vector3 pos)
        {
            float s = Vector3.Dot(pos, LeanDir);
            var q = pos - LeanDir * (Mathf.Max(0, s) + 2.5f);
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
        // Above the boss, readable at the game camera: on the screen-left, the conducting hand rises with 1-4 oversized
        // fingers lit gold (folded fingers stay dark brass); on the screen-right, the pendulum hand (brass rod, ivory bob)
        // swings out to the screen-left or screen-right and holds there. For a Leaning Bell phrase the hand leans toward
        // the hit half. Rewind marks: ivory double chevrons on each rewound footprint, turning backwards. Never coral.
        sealed class PhraseFx
        {
            static Material brass, gold, ivory, dark;
            readonly GameObject root;
            readonly Transform hand, rod, bob;
            readonly List<Transform> marks = new List<Transform>();
            readonly bool right;
            const float HandHeight = 6.4f, Side = 2.7f, RodLength = 2.4f, PivotHeight = 8.2f, SwingDeg = 42f;
            public bool Alive => root;
            public int Marks => marks.Count(m => m);

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
                var face = Quaternion.LookRotation(-fwd);
                boss = Sim.Flat(boss);
                // the hand rises over 0.5 s and holds; a Leaning Bell phrase leans it toward the hit half
                float rise = Mathf.SmoothStep(0, 1, Mathf.Clamp01(tick / 30f));
                var at = boss - scr * Side + Vector3.up * Mathf.Lerp(3.2f, HandHeight, rise);
                var tilt = Quaternion.identity;
                if (lean.sqrMagnitude > 0.5f) { at += lean * 1.4f * rise; tilt = Quaternion.AngleAxis(24f * rise, Vector3.Cross(Vector3.up, lean)); }
                hand.SetPositionAndRotation(at, tilt * face);
                hand.localScale = Vector3.one * 1.25f;
                // the pendulum swings out from 0.3 s, eases over 0.5 s, then holds with a faint tremble
                float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01((tick - 18) / 30f));
                float deg = (right ? 1 : -1) * SwingDeg * u + 2f * Mathf.Sin(tick * 0.21f) * u;
                var pivot = boss + scr * Side + Vector3.up * PivotHeight;
                float a = deg * Mathf.Deg2Rad;
                var down = -Vector3.up * Mathf.Cos(a) + scr * Mathf.Sin(a);
                var tip = pivot + down * RodLength;
                rod.position = (pivot + tip) / 2; rod.rotation = Quaternion.FromToRotation(Vector3.up, -down);
                bob.position = tip;
                foreach (var m in marks) if (m) m.Rotate(0, -90f * Sim.Dt, 0, Space.World);   // turning backwards
            }

            // ivory double chevron on the floor (an identity mark on a rewound footprint, never a hazard)
            public void Mark(Vector3 at)
            {
                if (!root) return;
                var m = new GameObject("rewind mark").transform; m.SetParent(root.transform, false);
                m.position = new Vector3(at.x, 0.09f, at.z);
                for (int i = 0; i < 2; i++)
                {
                    var c = Part(PrimitiveKind.Cone, m, new Vector3(0.38f * i - 0.19f, 0, 0), new Vector3(0.5f, 0.32f, 0.04f), ivory);
                    c.localRotation = Quaternion.Euler(90, 0, 90);   // flat on the floor, pointing anticlockwise
                }
                marks.Add(m);
            }

            public void Clear()
            {
                if (root) Object.Destroy(root);
                marks.Clear();
            }
        }
    }
}
