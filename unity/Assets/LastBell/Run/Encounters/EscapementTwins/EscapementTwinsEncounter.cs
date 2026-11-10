using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using R = LastBell.Run.EscapementTwinsRules;

namespace LastBell.Run
{
    // The Escapement Twins: a practice-only Brass Foundry miniboss (docs/bosses/escapement-twins.md, a proposal).
    // Two clock towers on one health bar: Tick (brass, this transform) and Tock (cobalt, a boss part whose hits go to the
    // same bar). They stand 12 m apart on an axis that turns 45 degrees between abilities. Four abilities:
    //
    //   Pallet Lock       every player wears brass or cobalt; each twin hits everything outside its 4.5 m disc, but
    //                     only for its own colour. The marks flip after each lock, so the two halves cross over.
    //   Escape Wheel      the twins take turns firing a fan of six 20-degree spokes from their own bodies, the first
    //                     spoke aimed at the player farthest from that twin. One fan on the floor at a time.
    //   Counterpoise      a numbered soak circle beyond each twin, landing together; empty wipes (settled rule).
    //                     Solo: one circle in the middle. Below 50%: a second set on the perpendicular axis.
    //   Lock and Release  they slam together along the lane between them, a shock ring hits everyone 7 m+ out, then
    //                     they stay locked 3.5 s (the punish window) and spring apart on an axis turned 90 degrees.
    //                     Hard: Recoil, the spring paths are lanes too.
    //
    // 50%: the current ability finishes, then a 2 s harmless shift; 100 -> 120 BPM and a new order. No enrage.
    // Bodies: the rigged towers (Characters/EscapementTick and EscapementTock, split from the 26 Oct model on 9 Nov) and the
    // escape-wheel prop (Characters/EscapementWheel), each with a labelled primitive stand-in when its model is missing.
    // Each tower turns its clock face to the camera (presentation only; gameplay rotation is unchanged). Strike clips land
    // their strike frame on the gameplay tick (BossClipCues). Every warning uses the raid look (HazardSystem.RaidStyle).
    // No ability text: PhaseName and Lesson stay empty, names live only in the F7 overlay.
    public sealed class EscapementTwinsEncounter : BossEncounter
    {
        public override string Id => "boss.escapement-twins";
        public override string DisplayName => "The Escapement Twins";
        public override string Lesson => "";
        // the rigged towers when both models are in the project, else the labelled stand-ins
        public override bool Placeholder => !tickPresenter || !tickPresenter.HasModel || !tockPresenter || !tockPresenter.HasModel;
        CharacterPresenter tickPresenter, tockPresenter, wheelPresenter; BossClipCues tickCues, tockCues, wheelCues;
        Transform tickPivot, tockPivot, wheelSpin;
        public CharacterPresenter TickPresenter => tickPresenter;
        public CharacterPresenter TockPresenter => tockPresenter;
        public CharacterPresenter WheelPresenter => wheelPresenter;
        public static readonly string[] ClipNames = { "Idle", "Walk", "PolarityLoop", "FacesDown", "AgreeAim", "AgreeFire", "VesicaLoop",
            "FacesOut", "Stagger", "PhaseShift", "Defeat" };
        public static readonly string[] WheelClipNames = { "Idle", "WheelSpin", "WheelLoop", "WheelReverse" };
        // clip lengths and strike frames (seconds), as keyed in escapement_twins_rig.py
        const float StompLen = 0.8f, StompAt = 0.5f, AimLen = 1.2f, AimAt = 1.1f, FireLen = 0.6f, FireAt = 0.1f, SpringLen = 0.8f, SpringAt = 0.15f;
        IEnumerable<BossClipCues> BothCues { get { if (tickCues != null) yield return tickCues; if (tockCues != null) yield return tockCues; } }
        public override float NextPhaseFraction() => 0.49f;

        public const int MaxHpMedium = 3000;
        public const string Pallet = "PalletLock", Wheel = "EscapeWheel", Counter = "Counterpoise", LockRelease = "LockRelease";
        static readonly string[] RotationHigh = { Pallet, Wheel, Counter, LockRelease };
        static readonly string[] RotationLow = { LockRelease, Pallet, Wheel, Counter };
        public static string[] Rotation(bool phase2) => (string[])(phase2 ? RotationLow : RotationHigh).Clone();

        // palette (HDR): brass and cobalt are identity (friendly dotted rims, head markers), hostile shapes stay coral/amber
        public static readonly Color Brass = new Color(0.86f, 0.66f, 0.30f), Cobalt = new Color(0.20f, 0.32f, 0.72f),
            Gold = new Color(1.0f, 0.82f, 0.38f), Silver = new Color(0.78f, 0.84f, 0.95f),
            BrassRim = new Color(2.2f, 1.6f, 0.55f), CobaltRim = new Color(0.45f, 0.85f, 2.6f);

        // ---------------- state (tests and the F7 overlay read it) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }
        public float Axis { get; private set; } = 90f;
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        public float Bpm => Phase2 ? R.BpmLow : R.BpmHigh;
        public int BeatTicks => R.BeatTicks(Phase2);
        public int Tick => W != null ? W.Tick : 0;
        public int BeatIndex { get; private set; }
        public string ForcedNext;                       // dev/test hook: replaces the next rotation entry once
        public readonly List<string> BlockLog = new List<string>();
        public int Blocks { get; private set; }
        public Vector3 TickPos => Sim.Flat(transform.position);
        public Vector3 TockPos { get; private set; }
        public BossPart TockPart => tockPart;
        public CombatWorld World => W;

        // per-ability counters
        public int PalletCasts, LocksResolved, LockHits, Swaps;
        public int WheelCasts, VolleysResolved, SpokeHits;
        public int CounterCasts, SoaksResolved, SoakPlayers, SoakEmpty;
        public int LockReleaseCasts, LaneHits, ShockHits, LandHits, RecoilHits;
        public readonly List<(int start, int end)> PunishLog = new List<(int, int)>();
        public bool Locked { get; private set; }
        public readonly List<(int tick, int volley, bool tick_, float yaw, int warn)> VolleyLog = new List<(int, int, bool, float, int)>();
        public readonly List<(int tick, int set, Vector3 at, int need, int inside)> SoakLog = new List<(int, int, Vector3, int, int)>();
        public readonly List<(int tick, int lockNo, int brass, int cobalt, int warn)> LockLog = new List<(int, int, int, int, int)>();
        readonly Dictionary<PlayerController, int> marks = new Dictionary<PlayerController, int>();   // 0 brass (Tick), 1 cobalt (Tock)
        public int MarkOf(PlayerController p) => p != null && marks.TryGetValue(p, out int m) ? m : -1;
        public int MarkedCount => marks.Count;

        // ---------------- bodies (placeholders) ----------------
        Transform tockBody, wheel;
        Renderer tickFace, tockFace;
        BossPart tockPart;
        float tickPulse, tockPulse;
        readonly Dictionary<PlayerController, Transform> markViews = new Dictionary<PlayerController, Transform>();
        readonly List<BellkeeperFx.Telegraph> rims = new List<BellkeeperFx.Telegraph>();
        readonly List<GameObject> numerals = new List<GameObject>();

        void Awake() { MaxHp = MaxHpMedium; BodyRadius = 1.2f; HitHeight = 2.0f; MarkHeight = 4.4f; }

        protected override void Build()
        {
            PhaseName = "";
            tickPivot = new GameObject("Tick facing").transform; tickPivot.SetParent(transform, false);
            tickPresenter = CharacterPresenter.Create(tickPivot, "EscapementTick", 1.5f, 1f, 0, h => { tickFace = Tower(h, "Tick (STAND-IN placeholder)", Brass, Gold); return h; });
            tickCues = new BossClipCues(tickPresenter); Tint(tickPresenter, TickTint);
            var go = new GameObject("Tock");
            go.transform.SetParent(transform.parent, false);
            TockPos = R.TockAt(Axis);
            go.transform.SetPositionAndRotation(TockPos, Quaternion.Euler(0, Sim.YawTo(TockPos, TickPos, 0), 0));
            tockBody = go.transform;
            // Tock's presenter lives under its own body (not this boss's children), so LAN capture picks Tick's presenter
            tockPivot = new GameObject("Tock facing").transform; tockPivot.SetParent(tockBody, false);
            tockPresenter = CharacterPresenter.Create(tockPivot, "EscapementTock", 1.5f, 1f, 0, h => { tockFace = Tower(h, "Tock (STAND-IN placeholder)", Cobalt, Silver); return h; });
            tockCues = new BossClipCues(tockPresenter); Tint(tockPresenter, TockTint);
            SimSmoother.Attach(go);
            tockPart = new BossPart { Name = "Tock", Pos = TockPos, BodyRadius = BodyRadius, HitHeight = HitHeight };
            tockPart.TakeHit = (dmg, contact) => TakeHit(dmg, contact);   // one health bar for both
            transform.rotation = Quaternion.Euler(0, Sim.YawTo(TickPos, TockPos, 0), 0);
            gameObject.AddComponent<EscapementTwinsHud>().Bind(this);
        }

        // a labelled stand-in tower: plinth, body, face disc and a pallet arm
        static Renderer Tower(Transform host, string name, Color body, Color face)
        {
            var root = new GameObject(name).transform; root.SetParent(host, false);
            Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.15f, 0), new Vector3(2.4f, 0.15f, 2.4f), body * 0.7f);
            Prim.Make(PrimitiveKind.Cube, root, new Vector3(0, 1.8f, 0), new Vector3(1.3f, 3.0f, 1.3f), body);
            Prim.Make(PrimitiveKind.Cone, root, new Vector3(0, 3.65f, 0), new Vector3(1.5f, 0.7f, 1.5f), body * 0.85f);
            var f = Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 2.6f, 0.68f), new Vector3(1.0f, 0.06f, 1.0f), face, 1.2f, Quaternion.Euler(90, 0, 0));
            Prim.Make(PrimitiveKind.Cube, root, new Vector3(0, 2.6f, 0.75f), new Vector3(0.08f, 0.42f, 0.04f), new Color(0.1f, 0.1f, 0.12f));
            Prim.Make(PrimitiveKind.Cube, root, new Vector3(0.95f, 1.6f, 0), new Vector3(0.25f, 1.6f, 0.25f), face * 0.8f, 0, Quaternion.Euler(0, 0, -20));
            return f.GetComponent<Renderer>();
        }

        public override void BeginCombat()
        {
            base.BeginCombat();
            if (!W.Parts.Contains(tockPart)) W.Parts.Add(tockPart);
        }

        int Beats(float b) => Mathf.Max(1, Mathf.RoundToInt(b * BeatTicks));
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        bool Hard => Level == Difficulty.Hard; bool Easy => Level == Difficulty.Easy;
        List<PlayerController> StandingList() { var l = new List<PlayerController>(); foreach (var p in W.Players) if (!p.Down) l.Add(p); return l; }
        int Standing => StandingList().Count;
        bool IsLive(Hazard h) { if (h == null) return false; var live = W.Hazards.Live; for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }
        float Floor => CombatWorld.PlayerBound + 0.6f;

        Hazard Warn(Hazard h)
        {
            h.Knockback = 0; if (h.Stun > 6) h.Stun = 6;
            W.Hazards.Spawn(h);
            HazardSystem.RaidStyle(h, CrucivarEncounter.FoundryMotif);   // the raid warning look (Telegraph-Style v2), Brass Foundry motif
            if (h.Tele != null) h.Tele.SpentFast = true;
            return h;
        }

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            foreach (var w in Place(Axis)) yield return w;
            int rotHigh = 0, rotLow = 0;
            while (true)
            {
                if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
                if (GatePending) foreach (var w in Transition()) yield return w;
                Blocks++;
                string atk = ForcedNext != null && Array.IndexOf(RotationHigh, ForcedNext) >= 0 ? ForcedNext
                    : Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk);
                IEnumerable<int> body = atk switch
                {
                    Pallet => PalletLock(),
                    Wheel => EscapeWheel(),
                    Counter => Counterpoise(),
                    _ => LockAndRelease(),
                };
                foreach (var w in body) yield return w;
                Current = "turning";
                foreach (var w in Place(Axis + R.TurnPerAbility)) yield return w;
                Current = "between blocks";
                yield return Beats(1);
            }
        }

        protected override void StepAlways()
        {
            if (Age % BeatTicks == 0) { BeatIndex++; if (BeatIndex % 2 == 0) tickPulse = 1; else tockPulse = 1; }
            if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
            if (!Dead && W != null && tockPart != null && !W.Parts.Contains(tockPart)) W.Parts.Add(tockPart);   // Tock is hittable all fight
            bool walking = Current == "reposition";
            foreach (var c in BothCues) c.Step(walking);
            wheelCues?.Step(false);
        }
        protected override void OnHalt() { foreach (var c in BothCues) { c.Clear(); c.Loop = "Idle"; } }
        void LoopBoth(string loop, float speed = 1) { foreach (var c in BothCues) c.LoopAt(0, loop, speed); }
        void StrikeBoth(string clip, int inTicks, float at, float len) { foreach (var c in BothCues) c.Strike(clip, inTicks, at, len); }

        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n) { if (Phase2) return; Phase2 = true; GatePending = false; }

        // both twins walk to their spots on the given axis (straight lines, at once)
        IEnumerable<int> Place(float axis)
        {
            Axis = Mathf.Repeat(axis, 360f);
            Vector3 a0 = TickPos, b0 = TockPos, a1 = R.TickAt(Axis), b1 = R.TockAt(Axis);
            float dist = Mathf.Max(Sim.Flat(a1 - a0).magnitude, Sim.Flat(b1 - b0).magnitude);
            int ticks = Mathf.CeilToInt(dist / R.WalkSpeed * Sim.Hz);
            if (dist < 0.05f) { SetBodies(a1, b1); yield break; }
            Current = "reposition";
            for (int t = 1; t <= ticks; t++) { SetBodies(Vector3.Lerp(a0, a1, t / (float)ticks), Vector3.Lerp(b0, b1, t / (float)ticks)); yield return 1; }
        }
        void SetBodies(Vector3 tick, Vector3 tock)
        {
            transform.position = Sim.Flat(tick);
            TockPos = Sim.Flat(tock); if (tockPart != null) tockPart.Pos = TockPos;
            float yaw = Sim.YawTo(TickPos, TockPos, transform.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (tockBody) tockBody.SetPositionAndRotation(TockPos, Quaternion.Euler(0, yaw + 180f, 0));
        }

        // ================= 1. Pallet Lock =================
        IEnumerable<int> PalletLock()
        {
            Current = "Pallet Lock"; PalletCasts++;
            AssignMarks();
            var brassRim = Rim(TickPos, R.DiscRadius, BrassRim); var cobaltRim = Rim(TockPos, R.DiscRadius, CobaltRim);
            int locks = R.Locks(Phase2);
            LoopBoth("PolarityLoop", Bpm / R.BpmHigh);   // each tower bows on its own beat while the lock winds up
            for (int k = 0; k < locks; k++)
            {
                int warn = Beats(k == 0 ? R.FirstLockBeats(Phase2, Level) : R.SwapLockBeats(Phase2, Level));
                StrikeBoth("FacesDown", warn, StompAt, StompLen);   // both stomp as their locks land
                int brass = 0, cobalt = 0; foreach (var m in marks.Values) if (m == 0) brass++; else cobalt++;
                LockLog.Add((Tick, k + 1, brass, cobalt, warn));
                var a = LockRing(TickPos, 0, warn); var b = LockRing(TockPos, 1, warn);
                for (int t = 0; t < warn && (IsLive(a) || IsLive(b)); t++) { Windup((t + 1) / (float)warn); yield return 1; }
                while (IsLive(a) || IsLive(b)) yield return 1;
                Release(1f); LocksResolved++;
                if (k < locks - 1) { FlipMarks(); Swaps++; Current = $"Pallet Lock (swap {k + 1})"; }
            }
            ClearMarks(); brassRim.Dismiss(); cobaltRim.Dismiss(); rims.Remove(brassRim); rims.Remove(cobaltRim); LoopBoth("Idle");
            Current = "Pallet Lock (recovery)";
            yield return Beats(1);
        }

        Hazard LockRing(Vector3 at, int mark, int warn)
        {
            var h = new Hazard
            {
                Source = mark == 0 ? "Pallet Lock (Tick)" : "Pallet Lock (Tock)", Shape = HazardShape.Ring, Kind = HazardKind.Ground,
                Origin = at, Inner = R.DiscRadius, Radius = 40f, ClipRadius = Floor, LockAt = 0, ResolveAt = warn, Damage = R.LockDmg,
            };
            h.Affects = p => MarkOf(p) == mark;
            h.OnResolve = hz => { LockHits += hz.Hit.Count; CameraShake.Shake(0.1f, 0.15f); };
            return Warn(h);
        }

        void AssignMarks()
        {
            ClearMarks();
            var party = StandingList();
            // the half nearer Tick wears brass, so nobody starts on the wrong side by more than they must
            party.Sort((x, y) => Lean(x).CompareTo(Lean(y)));
            int brass = R.BrassCount(party.Count);
            for (int i = 0; i < party.Count; i++) SetMark(party[i], i < brass ? 0 : 1);
        }
        float Lean(PlayerController p) => Sim.Flat(p.transform.position - TickPos).magnitude - Sim.Flat(p.transform.position - TockPos).magnitude;
        void FlipMarks() { foreach (var p in new List<PlayerController>(marks.Keys)) SetMark(p, 1 - marks[p]); }
        void SetMark(PlayerController p, int m)
        {
            marks[p] = m;
            if (!markViews.TryGetValue(p, out var v) || !v)
            {
                v = Prim.Make(PrimitiveKind.Sphere, transform.parent, p.transform.position + Vector3.up * 2.6f, new Vector3(0.5f, 0.5f, 0.5f), Brass, 2f);
                v.name = "Pallet marker (identity)";
                markViews[p] = v;
            }
            v.GetComponent<Renderer>().material = Prim.Material(m == 0 ? Brass : Cobalt, 2.5f);
            p.Marks.Remove(MechanicIcons.TeamBrass); p.Marks.Remove(MechanicIcons.TeamCobalt); p.Marks.Add(m == 0 ? MechanicIcons.TeamBrass : MechanicIcons.TeamCobalt);
        }
        void ClearMarks()
        {
            foreach (var kv in markViews) if (kv.Value) Destroy(kv.Value.gameObject);
            foreach (var p in marks.Keys) if (p) { p.Marks.Remove(MechanicIcons.TeamBrass); p.Marks.Remove(MechanicIcons.TeamCobalt); }
            markViews.Clear(); marks.Clear();
        }
        BellkeeperFx.Telegraph Rim(Vector3 at, float r, Color rim)
        {
            var t = W.BossFx.Circle(r); t.Motif(0); t.Friendly(rim); t.Order = 2; t.Progress(1); t.Place(at, 0);
            rims.Add(t); return t;
        }

        // ================= 2. Escape Wheel =================
        IEnumerable<int> EscapeWheel()
        {
            Current = "Escape Wheel"; WheelCasts++;
            var mid = (TickPos + TockPos) / 2;
            wheel = MakeWheel(mid); wheel.name = "Escape wheel";
            wheelCues = new BossClipCues(wheelPresenter); wheelCues.Loop = "WheelLoop"; wheelCues.Play("WheelSpin", Sim.Ticks(0.8f));
            int volleys = R.Volleys(Phase2);
            for (int v = 0; v < volleys; v++)
            {
                bool fromTick = v % 2 == 0; var origin = fromTick ? TickPos : TockPos;
                if (fromTick) tickPulse = 1.5f; else tockPulse = 1.5f;
                var far = Farthest(origin);
                float yaw = far ? Sim.YawTo(origin, far.transform.position, 0) : (fromTick ? Axis : Axis + 180f);
                int warn = Sim.Ticks(R.VolleySeconds(v, Phase2, Level));
                VolleyLog.Add((Tick, v + 1, fromTick, yaw, warn));
                // the firing tower rears back (aimed by the last 0.1 s) and snaps forward as its fan lands; the wheel kicks back
                var fc = fromTick ? tickCues : tockCues;
                fc.Strike("AgreeAim", warn - Sim.Ticks(FireAt), AimAt, AimLen); fc.Strike("AgreeFire", warn, FireAt, FireLen);
                wheelCues?.Strike("WheelReverse", warn, 0.05f, 0.6f);
                var fan = new List<Hazard>();
                for (int i = 0; i < R.Spokes; i++)
                {
                    var h = new Hazard
                    {
                        Source = fromTick ? "Escape Wheel (Tick)" : "Escape Wheel (Tock)", MechanicId = MechanicIcons.Cone, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = origin,
                        Yaw = yaw + i * 360f / R.Spokes, Radius = R.SpokeReach, HalfAngle = R.SpokeHalf, ClipRadius = Floor, LockAt = 0, ResolveAt = warn, Damage = R.SpokeDmg,
                    };
                    h.OnResolve = hz => SpokeHits += hz.Hit.Count;
                    fan.Add(Warn(h));
                }
                for (int t = 0; t < warn && fan.Exists(IsLive); t++) yield return 1;
                while (fan.Exists(IsLive)) yield return 1;
                VolleysResolved++; CameraShake.Shake(0.06f, 0.1f);
            }
            if (wheel) Destroy(wheel.gameObject);
            wheel = null; wheelPresenter = null; wheelCues = null; wheelSpin = null;
            Current = "Escape Wheel (recovery)";
            yield return Beats(1);
        }
        PlayerController Farthest(Vector3 from)
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players) { if (p.Down) continue; float d = Sim.Flat(p.transform.position - from).magnitude; if (d > bd) { bd = d; best = p; } }
            return best;
        }

        // ================= 3. Counterpoise =================
        IEnumerable<int> Counterpoise()
        {
            Current = "Counterpoise"; CounterCasts++;
            int sets = R.Sets(Phase2);
            for (int s = 0; s < sets; s++)
            {
                int warn = Beats(s == 0 ? R.SoakBeats(Phase2, Level) : R.SecondSetBeats + (Easy ? 2 : 0));
                int standing = Standing;
                var spots = R.SoakSpots(Axis, s, standing);
                var set = new List<Hazard>();
                foreach (var (at, need) in spots)
                {
                    int total = standing <= 1 ? R.SoloSoakDmg : R.SoakDmg;
                    var h = new Hazard
                    {
                        Source = "Counterpoise", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = R.SoakRadius,
                        LockAt = 0, ResolveAt = warn, Damage = total, ShareTotal = total, ShareCount = need, WipeIfEmpty = true, Soak = true,
                    };
                    int setNo = s, want = need;
                    h.OnResolve = hz => { SoaksResolved++; SoakPlayers += hz.Hit.Count; if (hz.Hit.Count == 0) SoakEmpty++; SoakLog.Add((Tick, setNo + 1, hz.Origin, want, hz.Hit.Count)); CameraShake.Shake(0.12f, 0.18f); };
                    set.Add(Warn(h));
                    numerals.Add(Numeral(at, need));
                }
                Current = s == 0 ? "Counterpoise" : "Counterpoise (second set)";
                LoopBoth("VesicaLoop"); StrikeBoth("FacesOut", warn, SpringAt, SpringLen); foreach (var c in BothCues) c.LoopAt(warn, "Idle");
                for (int t = 0; t < warn && set.Exists(IsLive); t++) { Windup((t + 1) / (float)warn); yield return 1; }
                while (set.Exists(IsLive)) yield return 1;
                Release(1.2f); ClearNumerals();
            }
            Current = "Counterpoise (recovery)";
            yield return Beats(1);
        }
        static readonly string[] Roman = { "", "I", "II", "III", "IV" };
        GameObject Numeral(Vector3 at, int n)
        {
            var go = new GameObject("Counterpoise numeral");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = at + Vector3.up * 1.6f;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.anchor = TextAnchor.MiddleCenter; text.characterSize = 0.22f; text.fontSize = 64; text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 0.95f, 0.82f);
            text.text = Roman[Mathf.Clamp(n, 0, Roman.Length - 1)];
            return go;
        }
        void ClearNumerals() { foreach (var g in numerals) if (g) Destroy(g); numerals.Clear(); }

        // ================= 4. Lock and Release =================
        IEnumerable<int> LockAndRelease()
        {
            Current = "Lock and Release"; LockReleaseCasts++;
            Vector3 a0 = TickPos, b0 = TockPos, mid = (a0 + b0) / 2, dir = Sim.Flat(b0 - a0).normalized;
            float len = Sim.Flat(b0 - a0).magnitude;
            int warn = Beats(R.LaneBeats(Level)), shock = warn + Beats(R.ShockAfterBeats);
            var lane = Warn(new Hazard
            {
                Source = "Lock and Release (lane)", MechanicId = MechanicIcons.Lane, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = a0, Yaw = Sim.YawTo(a0, b0, 0),
                Length = len, Width = R.LaneWidth, LockAt = 0, ResolveAt = warn, Damage = R.LaneDmg,
                OnResolve = hz => { LaneHits += hz.Hit.Count; CameraShake.Shake(0.2f, 0.25f); W.BossFx.ChargeLand(mid); },
            });
            var ring = Warn(new Hazard
            {
                Source = "Lock and Release (shock)", MechanicId = MechanicIcons.Ring, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = mid, Inner = R.ShockInner, Radius = 40f,
                ClipRadius = Floor, LockAt = 0, ResolveAt = shock, Damage = R.ShockDmg,
                OnResolve = hz => { ShockHits += hz.Hit.Count; CameraShake.Shake(0.15f, 0.25f); },
            });
            // lean in, then slam: the last 12 ticks of the warning they slide together and meet as the lane lands
            StrikeBoth("FacesDown", warn, StompAt, StompLen);
            int slide = Mathf.Min(12, warn - 1);
            for (int t = 0; t < warn && IsLive(lane); t++)
            {
                Windup((t + 1) / (float)warn);
                int into = t - (warn - slide);
                if (into >= 0)
                {
                    float u = (into + 1) / (float)slide;
                    SetBodies(Vector3.Lerp(a0, mid - dir * BodyRadius, u), Vector3.Lerp(b0, mid + dir * BodyRadius, u));
                }
                yield return 1;
            }
            SetBodies(mid - dir * BodyRadius, mid + dir * BodyRadius);
            Release(1.5f);
            Current = "Lock and Release (shock)";
            while (IsLive(lane) || IsLive(ring)) yield return 1;
            // the punish window: locked together, nothing live
            Current = "Lock and Release (locked)"; Locked = true;
            foreach (var c in BothCues) { c.Clear(); c.Play("Stagger", Sim.Ticks(R.PunishSeconds)); }   // reeling: the punish window
            int from = Tick;
            yield return Sim.Ticks(R.PunishSeconds);
            PunishLog.Add((from, Tick)); Locked = false;
            // release on an axis turned 90 degrees: landing circles shown 2 beats before they arrive
            float axis = Axis + 90f; Vector3 la = R.TickAt(axis), lb = R.TockAt(axis);
            int leap = Beats(R.LeapBeats);
            Current = "Lock and Release (release)";
            StrikeBoth("FacesOut", leap, SpringAt, SpringLen);
            var landing = new List<Hazard>();
            foreach (var at in new[] { la, lb })
            {
                landing.Add(Warn(new Hazard
                {
                    Source = "Lock and Release (landing)", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = R.LandRadius,
                    LockAt = 0, ResolveAt = leap, Damage = R.LandDmg, OnResolve = hz => LandHits += hz.Hit.Count,
                }));
                if (Hard)
                    landing.Add(Warn(new Hazard
                    {
                        Source = "Lock and Release (recoil)", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = Sim.Flat(mid), Yaw = Sim.YawTo(mid, at, 0),
                        Length = Sim.Flat(at - mid).magnitude, Width = R.RecoilWidth, LockAt = 0, ResolveAt = leap, Damage = R.RecoilDmg, OnResolve = hz => RecoilHits += hz.Hit.Count,
                    }));
            }
            Vector3 c0 = TickPos, d0 = TockPos; slide = Mathf.Min(12, leap - 1);
            for (int t = 0; t < leap && landing.Exists(IsLive); t++)
            {
                int into = t - (leap - slide);
                if (into >= 0) { float u = (into + 1) / (float)slide; SetBodies(Vector3.Lerp(c0, la, u), Vector3.Lerp(d0, lb, u)); }
                yield return 1;
            }
            SetBodies(la, lb); Axis = Mathf.Repeat(axis, 360f);
            while (landing.Exists(IsLive)) yield return 1;
            Current = "Lock and Release (recovery)";
            yield return Beats(1);
        }

        // ================= the 50% shift =================
        IEnumerable<int> Transition()
        {
            GatePending = false;
            Current = "Shift"; Log("Shift"); ShiftStart = Tick; HpAtShiftStart = Hp;
            CameraShake.Shake(0.12f, 0.3f);
            foreach (var c in BothCues) { c.Clear(); c.Play("PhaseShift", R.ShiftTicks); c.Loop = "Idle"; }
            for (int t = 0; t < R.ShiftTicks; t++)   // harmless: nothing spawns and both stay damageable
            {
                transform.Rotate(0, 6f, 0); if (tockBody) tockBody.Rotate(0, -6f, 0);
                tickPulse = tockPulse = 1;
                yield return 1;
            }
            Phase2 = true; Release(1.5f); ShiftEnd = Tick;
            SetBodies(TickPos, TockPos);
            Log("tempo -> " + Bpm);
        }

        // ---------------- presentation ----------------
        protected override void Update()
        {
            base.Update();
            tickPulse = Mathf.MoveTowards(tickPulse, 0, Time.deltaTime * 3f); tockPulse = Mathf.MoveTowards(tockPulse, 0, Time.deltaTime * 3f);
            if (tickFace) Prim.SetGlow(tickFace, Gold * (0.6f + 0.5f * glowLevel + 2f * tickPulse));
            if (tockFace) Prim.SetGlow(tockFace, Silver * (0.6f + 0.5f * glowLevel + 2f * tockPulse));
            if (wheelSpin) wheelSpin.Rotate(0, 90f * Time.deltaTime * (Phase2 ? 1.6f : 1f), 0);
            PresentModels(glowLevel, tickPulse, tockPulse);
            foreach (var kv in markViews) if (kv.Key && kv.Value) kv.Value.position = kv.Key.transform.position + Vector3.up * 2.6f;
        }

        // the escape wheel: the rigged prop (it shows WheelLoop by default, so a LAN client's copy turns too), else a gold disc
        Transform MakeWheel(Vector3 at)
        {
            var root = new GameObject("Escape wheel").transform; root.SetParent(transform.parent, false); root.position = Sim.Flat(at);
            wheelPresenter = CharacterPresenter.Create(root, "EscapementWheel", 1.5f, 1f, 0, h =>
            {
                wheelSpin = Prim.Make(PrimitiveKind.Cylinder, h, Vector3.up * 0.05f, new Vector3(2.6f, 0.03f, 2.6f), Gold, 1.5f);
                wheelSpin.name = "Escape wheel (placeholder)"; return h;
            });
            if (wheelPresenter.HasModel) wheelPresenter.SetIdleClip("WheelLoop");
            return root;
        }

        // the split towers share one texture: a warm tint on Tick (sun) and a cool one on Tock (moon) tells them apart at range
        static readonly Color TickTint = new Color(1.0f, 0.9f, 0.7f), TockTint = new Color(0.68f, 0.8f, 1.0f);
        static void Tint(CharacterPresenter p, Color c)
        {
            if (!p || !p.HasModel) return;
            foreach (var r in p.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials) if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", m.GetColor("baseColorFactor") * c);
        }

        // presentation on host and LAN client alike: each tower and the wheel turn their faces to this machine's camera,
        // and the clock faces glow with the beat pulses
        void PresentModels(float glow, float tickP, float tockP)
        {
            var cam = Camera.main;
            if (cam)
            {
                void Face(Transform t) { if (!t) return; var d = Sim.Flat(cam.transform.position - t.position); if (d.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(d); }
                Face(tickPivot); Face(tockPivot); if (wheelPresenter && wheelPresenter.HasModel) Face(wheelPresenter.transform.parent);
            }
            if (tickPresenter) tickPresenter.GlowLevel = 0.6f + 0.5f * glow + 1.5f * tickP;
            if (tockPresenter) tockPresenter.GlowLevel = 0.6f + 0.5f * glow + 1.5f * tockP;
        }

        void ClearAll()
        {
            ClearMarks(); ClearNumerals();
            foreach (var r in rims) r.Dismiss(); rims.Clear();
            if (wheel) Destroy(wheel.gameObject);
            wheel = null; wheelPresenter = null; wheelCues = null; wheelSpin = null;
            Locked = false;
        }

        protected override void OnDefeat()
        {
            ClearAll();
            foreach (var c in BothCues) c.Clear();
            if (tickPresenter) tickPresenter.Action("Defeat", 150);
            if (tockPresenter) tockPresenter.Action("Defeat", 150);
            if (Placeholder)
            {
                if (tockBody) tockBody.Rotate(0, 0, 8f);   // a placeholder slump
                transform.Rotate(0, 0, -8f);
            }
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        // ---------------- LAN ----------------
        // Host: Tock's spot and the Escape Wheel travel as props (Tock first: it is a damage target), the face glow, beat
        // pulses and the phase as Look numbers. Client replica (never stepped): the built Tock follows its prop anchor and
        // faces Tick (spins in the Shift, slumps on defeat); the Pallet spheres come from the players' replicated marks;
        // the Counterpoise numerals from the hazard stand-ins (Source + ShareCount).
        const byte PropTock = 0, PropWheel = 1;
        readonly int tockNet = NextNetPropId--, wheelNet = NextNetPropId--;
        public override void CaptureNetProps(List<Networking.LanWorldFrame.Prop> into)
        {
            into.Add(NetProp(tockNet, PropTock, TockPos));
            if (wheel) into.Add(NetProp(wheelNet, PropWheel, wheel.position));
        }
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(Phase2 ? 1 : 0); into.Look.Add(tickPulse); into.Look.Add(tockPulse);   // [0] phase 2, [1] Tick pulse, [2] Tock pulse
            // [3] Tock's clip (index in ClipNames, -1 none) and [4] its speed: Tick's clip travels as the boss's own presenter
            into.Look.Add(tockPresenter ? Array.IndexOf(ClipNames, tockPresenter.CurrentClip) : -1);
            into.Look.Add(tockPresenter ? tockPresenter.NetworkClipSpeed : 1);
        }

        // client-side state and counters (two-process smoke tests read these)
        public int NetFrames { get; private set; }          // ReplicaPresent calls
        public int NetTockSynced { get; private set; }      // frames Tock followed the host's spot
        public int NetMarkViews => replicaMarks.Count;      // Pallet spheres shown now
        public int NetMarkViewsMade { get; private set; }
        public int NetNumeralsMade { get; private set; }    // Counterpoise numerals built from stand-ins
        public int NetWheelsMade { get; private set; }
        public int NetTockClips { get; private set; }       // frames Tock's rigged clip was applied from the host
        Transform tockAnchor, replicaWheel;
        bool replicaReady; float replicaSpin;
        readonly Dictionary<PlayerController, (Transform view, int mark)> replicaMarks = new Dictionary<PlayerController, (Transform, int)>();
        readonly Dictionary<Hazard, GameObject> replicaNumerals = new Dictionary<Hazard, GameObject>();
        static readonly List<PlayerController> scratchPlayers = new List<PlayerController>();
        static readonly List<Hazard> scratchHazards = new List<Hazard>();

        public override Transform MakeNetProp(byte kind)
        {
            if (kind == PropTock)
            {
                // an empty anchor: the client moves it along the host's positions and the built Tock follows it
                var a = new GameObject("Tock anchor (LAN)").transform; a.SetParent(transform.parent, false);
                tockAnchor = a; return a;
            }
            if (kind == PropWheel)
            {
                var t = MakeWheel(Vector3.zero);   // the rigged wheel (or its stand-in), as on the host
                t.name = "Escape wheel (LAN)"; replicaWheel = t; NetWheelsMade++; return t;
            }
            return null;
        }

        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            NetFrames++;
            var look = state.Look;
            bool phase2 = look.Count > 0 && look[0] > 0.5f;
            float tp = look.Count > 1 ? look[1] : 0, kp = look.Count > 2 ? look[2] : 0;
            if (tickFace) Prim.SetGlow(tickFace, Gold * (0.6f + 0.5f * state.Glow + 2f * tp));
            if (tockFace) Prim.SetGlow(tockFace, Silver * (0.6f + 0.5f * state.Glow + 2f * kp));

            // Tock: the host's spot, facing Tick; turning in the Shift; slumped once defeated
            if (tockBody)
            {
                if (!replicaReady) { replicaReady = true; var s = tockBody.GetComponent<SimSmoother>(); if (s) s.enabled = false; }
                if (tockAnchor && tockAnchor.gameObject.activeSelf)
                {
                    TockPos = Sim.Flat(tockAnchor.position); if (tockPart != null) tockPart.Pos = TockPos; NetTockSynced++;
                }
                replicaSpin = Current == "Shift" ? replicaSpin - 6f * Sim.Hz * dt : 0;
                float face = Sim.YawTo(TockPos, Sim.Flat(transform.position), tockBody.eulerAngles.y);
                tockBody.SetPositionAndRotation(TockPos, Quaternion.Euler(0, face + replicaSpin, 0));
                if (tockFace) tockFace.transform.parent.localRotation = Quaternion.Euler(0, 0, Dead ? 8f : 0);
            }
            if (tickFace) tickFace.transform.parent.localRotation = Quaternion.Euler(0, 0, Dead ? -8f : 0);
            if (wheelSpin) wheelSpin.Rotate(0, 90f * dt * (phase2 ? 1.6f : 1f), 0);
            // Tock's clip from the host (Tick's arrives as the boss presenter's own pose)
            if (tockPresenter && look.Count > 4)
            {
                int ci = Mathf.RoundToInt(look[3]);
                if (ci >= 0 && ci < ClipNames.Length) { tockPresenter.ApplyNetworkPose(ClipNames[ci], look[4], null, 1, false); if (tockPresenter.HasModel) NetTockClips++; }
            }
            PresentModels(state.Glow, tp, kp);
            ReplicaMarks();
            ReplicaNumerals();
        }

        // the brass / cobalt sphere over each marked player (the marks themselves arrive with the players)
        void ReplicaMarks()
        {
            if (W == null) return;
            foreach (var p in W.Players)
            {
                if (!p) continue;
                int m = p.Marks.Contains(MechanicIcons.TeamBrass) ? 0 : p.Marks.Contains(MechanicIcons.TeamCobalt) ? 1 : -1;
                replicaMarks.TryGetValue(p, out var e);
                if (m < 0 || p.Down)
                {
                    if (e.view) Destroy(e.view.gameObject);
                    replicaMarks.Remove(p); continue;
                }
                if (!e.view)
                {
                    e.view = Prim.Make(PrimitiveKind.Sphere, transform.parent, Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f), m == 0 ? Brass : Cobalt, 2.5f);
                    e.view.name = "Pallet marker (identity, LAN)"; e.mark = m; NetMarkViewsMade++;
                }
                else if (e.mark != m) { e.view.GetComponent<Renderer>().material = Prim.Material(m == 0 ? Brass : Cobalt, 2.5f); e.mark = m; }
                e.view.position = p.transform.position + Vector3.up * 2.6f;
                replicaMarks[p] = e;
            }
            scratchPlayers.Clear();
            foreach (var kv in replicaMarks) if (!kv.Key || !W.Players.Contains(kv.Key)) scratchPlayers.Add(kv.Key);
            foreach (var p in scratchPlayers) { if (replicaMarks[p].view) Destroy(replicaMarks[p].view.gameObject); replicaMarks.Remove(p); }
        }

        // a Roman numeral (players needed) over every live Counterpoise circle
        void ReplicaNumerals()
        {
            if (W == null || W.Hazards == null) return;
            var live = W.Hazards.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h == null || !h.Replica || h.Cancelled || h.Source != "Counterpoise" || replicaNumerals.ContainsKey(h)) continue;
                replicaNumerals[h] = Numeral(h.Origin, h.ShareCount); NetNumeralsMade++;
            }
            scratchHazards.Clear();
            foreach (var kv in replicaNumerals)
            {
                bool on = !kv.Key.Cancelled;
                if (on) { on = false; for (int i = 0; i < live.Count; i++) if (live[i] == kv.Key) { on = true; break; } }
                if (!on) scratchHazards.Add(kv.Key);
                else if (kv.Value) kv.Value.transform.position = kv.Key.Origin + Vector3.up * 1.6f;
            }
            foreach (var h in scratchHazards) { if (replicaNumerals[h]) Destroy(replicaNumerals[h]); replicaNumerals.Remove(h); }
        }

        void ClearReplica()
        {
            foreach (var kv in replicaMarks) if (kv.Value.view) Destroy(kv.Value.view.gameObject);
            replicaMarks.Clear();
            foreach (var kv in replicaNumerals) if (kv.Value) Destroy(kv.Value);
            replicaNumerals.Clear();
        }

        void OnDestroy()
        {
            ClearAll(); ClearReplica();
            if (tockBody) Destroy(tockBody.gameObject);
        }
    }
}
