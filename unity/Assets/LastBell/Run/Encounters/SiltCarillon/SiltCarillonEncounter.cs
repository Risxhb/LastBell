using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Silt Carillon: a practice-only Verdant Chimes miniboss (docs/bosses/silt-carillon.md, a proposal). A drowned
    // bell-tower that barely moves and controls the floor instead. Four abilities:
    //
    //   Silt Tide      mud floods part of the floor and stays as a standing burn. Above 50% High Tide (the ring from 7 m to
    //                  the edge); below 50% Crosscurrent (three of six 60-degree sectors, then the other three).
    //                  Hard: Backwash (drained ground strikes once 1.5 beats after it drains).
    //   Sunken Bells   with a tide, bells surface on the dry ground: shared soaks with a numeral (solo I; 2 = I+I;
    //                  3 = II+I; 4 = II+II). 30 per player it wants, split between those inside; an empty bell hits
    //                  the whole party for 35 (review 3 Nov 2026: a party-wide hit instead of the §5 wipe). Every bell rung: the tide drains at once and the Carillon is Drained for 3.5 s (punish window).
    //   Silt Fall      a drop follows every player, one per beat (4, 5 below 50%), locks 1.5 beats before it lands (1.75
    //                  at 116 BPM) and leaves a burning silt mound for 6 beats. Whoever stands still is hit.
    //   Drowned Peal   three bands (0-4.5, 4.5-9, 9-edge) land one after another, 1.5 beats apart: outgoing (A, B, C) or,
    //                  below 50%, ebbing (C, B, A). Step into the band that just landed.
    //
    // 50%: it finishes the current attack, then a 2 s harmless transformation (damageable, no heal); 96 -> 116 BPM.
    // No enrage. No ability text reaches the player (PhaseName and Lesson stay empty; names only in the F7 overlay).
    // Placeholder body only: primitives, no model, no audio. Every warning uses the raid look (HazardSystem.RaidStyle).
    public sealed class SiltCarillonEncounter : BossEncounter
    {
        public override string Id => "boss.silt-carillon";
        public override string DisplayName => "The Silt Carillon";
        public override string Lesson => "";
        public override bool Placeholder => true;
        public override float NextPhaseFraction() => 0.49f;

        // ---------------- tunables (Medium) ----------------
        public const float BpmHigh = 96f, BpmLow = 116f;
        public const float MudDps = 25f, MoundDps = 20f;
        public const int DropDmg = 30, PealDmg = 40, BackwashDmg = 30;
        public const int DrainedTicks = 210, ShiftTicks = 120;
        public const float GapBeats = 1f;
        static Vector3 Centre => Vector3.zero;

        // palette (HDR): strikes coral, ground amber, mud a dark amber-brown, the bells a friendly ivory-teal rim
        public static readonly Color StrikeFill = new Color(1.3f, 0.32f, 0.16f), StrikeBorder = new Color(2.4f, 0.8f, 0.3f),
            GroundFill = new Color(1.3f, 0.6f, 0.12f), GroundBorder = new Color(2.6f, 1.35f, 0.35f),
            MudFill = new Color(0.75f, 0.38f, 0.1f), MudBorder = new Color(2.2f, 1.15f, 0.35f),
            MoundFill = new Color(0.85f, 0.45f, 0.14f), MoundBorder = new Color(2.0f, 1.2f, 0.45f),
            BellRim = new Color(1.6f, 2.6f, 2.2f);

        // ---------------- state (read by tests and the F7 overlay) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;                 // dev/test hook: replaces the next rotation entry once
        public int Seed = Environment.TickCount;
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        public float Bpm => Phase2 ? BpmLow : BpmHigh;
        public int BeatTicks => Mathf.RoundToInt(60f * Sim.Hz / Bpm);
        public int Tick => W != null ? W.Tick : 0;
        public int BeatIndex { get; private set; }
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }
        public bool Drained { get; private set; }
        bool Hard => Level == Difficulty.Hard;
        int Standing { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }

        // logs
        public readonly List<(int tick, bool cross, int set, float baseYaw, int lockTick)> TideLog = new List<(int, bool, int, float, int)>();
        public readonly List<(int tick, bool early)> DrainLog = new List<(int, bool)>();
        public readonly List<(int tick, int bells, int[] needs, int[] inside, bool allRung)> BellSetLog = new List<(int, int, int[], int[], bool)>();
        public readonly List<(int start, int end)> DrainedLog = new List<(int, int)>();
        public readonly List<(int tick, int drops, int players, int lead)> FallLog = new List<(int, int, int, int)>();
        public readonly List<(int tick, bool ebb, int first, int gap)> PealLog = new List<(int, bool, int, int)>();
        public int DropsSpawned, DropsLanded, MoundsSpawned, MoundsSet, PealBands, BackwashCount, BellsRung, BellsEmpty;
        public int TideLive => mud.Count;
        public int FallLive { get; private set; }
        public float LastBaseYaw { get; private set; }
        public IReadOnlyList<(Hazard h, int need)> LiveBells => bells;
        // the bells the HUD numbers: the host's own list, or on a LAN client (a never-stepped replica, bells empty) the
        // host's live "Sunken Bell" soaks, mirrored as hazard stand-ins
        public void ShownBells(List<(Vector3 at, int need)> into)
        {
            into.Clear();
            if (!IsNetReplica) { foreach (var (h, need) in bells) into.Add((h.Origin, need)); return; }
            if (W == null) return;
            foreach (var h in W.Hazards.Live)
                if (h != null && h.Replica && !h.Cancelled && h.Soak && h.Source == "Sunken Bell") into.Add((h.Origin, h.ShareCount));
            if (into.Count > 0) ReplicaNumeralsShown = Mathf.Max(ReplicaNumeralsShown, into.Count);
        }
        public int ReplicaNumeralsShown { get; private set; }   // LAN test: most bell numerals a client's HUD drew at once
        public bool FirstBells => firstBells;

        enum Atk { TideBells, Fall, Peal, TidePeal }
        static readonly Atk[] RotationHigh = { Atk.TideBells, Atk.Fall, Atk.Peal, Atk.TidePeal, Atk.Fall };
        static readonly Atk[] RotationLow = { Atk.TideBells, Atk.Fall, Atk.Peal, Atk.TidePeal, Atk.Fall };
        public static string[] Rotation(bool phase2) => Array.ConvertAll(phase2 ? RotationLow : RotationHigh, a => a.ToString());
        int rotHigh, rotLow, beatAge;
        System.Random rng;
        bool firstBells = true;
        readonly List<(int at, Action act)> scheduled = new List<(int, Action)>();
        readonly List<Hazard> mud = new List<Hazard>();
        readonly List<(Hazard h, int need)> bells = new List<(Hazard, int)>();
        readonly List<(int need, int inside)> bellResults = new List<(int, int)>();

        void Awake() { MaxHp = 3200; BodyRadius = 1.6f; HitHeight = 2.0f; MarkHeight = 4.4f; }

        // ---------------- placeholder body ----------------
        Transform body; Renderer windowsL, windowsR, crown;
        static readonly Color Stone = new Color(0.86f, 0.84f, 0.74f), Moss = new Color(0.42f, 0.55f, 0.32f), Silt = new Color(0.42f, 0.33f, 0.22f),
            Bronze = new Color(0.45f, 0.55f, 0.42f), Window = new Color(0.3f, 0.55f, 1.0f);

        protected override void Build()
        {
            body = new GameObject("Silt Carillon (STAND-IN, labelled placeholder)").transform; body.SetParent(transform, false);
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 0.25f, 0), new Vector3(3.4f, 0.25f, 3.4f), Silt);              // mud-caked base
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 1.0f, 0), new Vector3(2.7f, 0.55f, 2.7f), Stone);              // drum with the arches
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 2.0f, 0), new Vector3(2.9f, 2.3f, 2.9f), Moss);                  // mossy dome
            crown = Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 3.25f, 0), Vector3.one * 0.45f, new Color(0.95f, 0.8f, 0.45f), 1f).GetComponent<Renderer>();
            foreach (float a in new[] { -55f, 0f, 55f })   // three sunken bells under the front arches
                Prim.Make(PrimitiveKind.Cone, body, Sim.Dir(a) * 1.4f + Vector3.up * 1.1f, new Vector3(0.6f, 0.45f, 0.6f), Bronze);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 2.2f, 1.35f), new Vector3(0.7f, 0.4f, 0.15f), Stone);             // small ivory face (front)
            windowsL = Prim.Make(PrimitiveKind.Sphere, body, new Vector3(-0.55f, 2.6f, 1.15f), Vector3.one * 0.35f, Window, 1.5f).GetComponent<Renderer>();
            windowsR = Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0.55f, 2.6f, 1.15f), Vector3.one * 0.35f, Window, 1.5f).GetComponent<Renderer>();
            gameObject.AddComponent<SiltCarillonHud>().Bind(this);
            PhaseName = "";
            SiltCarillonRules.Bound = CombatWorld.PlayerBound;
            W.Hazards.Resolving += OnResolving;
        }

        int Beats(float b) => Mathf.Max(1, Mathf.RoundToInt(b * BeatTicks));
        void Schedule(int ticks, Action a) => scheduled.Add((Age + Mathf.Max(1, ticks), a));
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");

        Hazard Live(Hazard h, Color fill, Color border, float flash = 0.4f)
        {
            W.Hazards.Spawn(h);
            HazardSystem.RaidStyle(h);   // Telegraph-Style v2 (no Verdant Chimes motif yet: the shared default)
            h.Tele.Tint(fill, border); h.Tele.FlashScale = flash; h.Tele.SpentFast = true;
            return h;
        }

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            if (Sim.Flat(transform.position).magnitude > 0.3f) foreach (var w in WalkTo(Centre, 3f, 300)) yield return w;
            while (true)
            {
                if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
                if (GatePending) foreach (var w in Transition()) yield return w;

                Blocks++;
                Atk atk;
                if (ForcedNext != null && Enum.TryParse(ForcedNext, out Atk forced)) atk = forced;
                else atk = Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk.ToString());
                IEnumerable<int> block = atk switch
                {
                    Atk.TideBells => TideBells(),
                    Atk.Fall => Fall(),
                    Atk.Peal => PealBlock(Phase2),
                    _ => TidePeal(),
                };
                foreach (var w in block) yield return w;
                Current = "between blocks";
                yield return Beats(GapBeats);
            }
        }

        protected override void StepAlways()
        {
            if (++beatAge >= BeatTicks) { beatAge = 0; BeatIndex++; }
            for (int i = scheduled.Count - 1; i >= 0; i--)
                if (Age >= scheduled[i].at) { var a = scheduled[i].act; scheduled.RemoveAt(i); a(); }
            for (int i = mud.Count - 1; i >= 0; i--) if (mud[i].Cancelled || !IsLive(mud[i])) mud.RemoveAt(i);
            // it turns slowly to face the nearest player (presentation; no shape depends on its facing)
            if (!Drained) { var p = W.NearestStanding(transform.position); if (p) TurnTowards(Sim.YawTo(transform.position, p.transform.position, transform.eulerAngles.y), 45); }
            if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
        }
        bool IsLive(Hazard h) { var live = W.Hazards.Live; for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }

        // practice phase row: the end of the 50% transformation (faster tempo), without the shift itself
        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n)
        {
            if (Phase2) return;
            Phase2 = true; GatePending = false; beatAge = 0;
        }

        // ================= 1. Silt Tide =================
        public int TideCasts { get; private set; }

        Hazard HighTide(int warn, int mudTicks)
        {
            var h = new Hazard
            {
                Source = "Silt Tide", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Centre, Inner = SiltCarillonRules.TideInner, Radius = 30f,
                LockAt = warn, ResolveAt = warn + mudTicks, BurnPerSecond = MudDps, Knockback = 0, Stun = 0, ClipRadius = CombatWorld.PlayerBound + 0.6f,
            };
            h.OnResolve = OnMudEbb;
            Live(h, MudFill, MudBorder); mud.Add(h);
            TideLog.Add((Tick, false, 0, 0, Tick + warn)); TideCasts++;
            return h;
        }
        List<Hazard> Crosscurrent(float baseYaw, int set, int warn, int mudTicks)
        {
            var list = new List<Hazard>();
            foreach (float yaw in SiltCarillonRules.SectorYaws(baseYaw, set))
            {
                var h = new Hazard
                {
                    Source = "Silt Tide", Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Centre, Yaw = yaw, Radius = 30f, HalfAngle = SiltCarillonRules.SectorHalf,
                    LockAt = warn, ResolveAt = warn + mudTicks, BurnPerSecond = MudDps, Knockback = 0, Stun = 0, ClipRadius = CombatWorld.PlayerBound + 0.6f,
                };
                h.OnResolve = OnMudEbb;
                Live(h, MudFill, MudBorder); mud.Add(h); list.Add(h);
            }
            TideLog.Add((Tick, true, set, baseYaw, Tick + warn)); TideCasts++;
            return list;
        }
        // the mud ebbed by itself (its time ran out): Hard adds the Backwash
        void OnMudEbb(Hazard h)
        {
            if (Dead) return;
            mud.Remove(h);
            if (mud.Count == 0) DrainLog.Add((Tick, false));
            if (Hard) Backwash(h);
        }
        // the bells were rung: every mud pool drains at once
        void Drain()
        {
            var was = new List<Hazard>(mud); mud.Clear();
            foreach (var h in was) { W.Hazards.Cancel(h); if (Hard && h.Locked) Backwash(h); }
            DrainLog.Add((Tick, true));
            W.BossFx.TollResolve(Centre, 0, CombatWorld.PlayerBound);
        }
        void Backwash(Hazard src)
        {
            var h = new Hazard
            {
                Source = "Backwash", Shape = src.Shape, Kind = HazardKind.Ground, Origin = src.Origin, Yaw = src.Yaw, Radius = src.Radius, Inner = src.Inner, HalfAngle = src.HalfAngle,
                LockAt = 0, ResolveAt = Beats(SiltCarillonRules.BackwashBeats), Damage = BackwashDmg, Knockback = 0, Stun = 6, ClipRadius = src.ClipRadius,
            };
            Live(h, GroundFill, GroundBorder, 0.45f); BackwashCount++;
        }
        IEnumerable<int> WaitMud(int guard = 1800) { for (int i = 0; i < guard && mud.Count > 0; i++) yield return 1; }
        IEnumerable<int> WaitQuiet(int guard = 900) { for (int i = 0; i < guard && W.Hazards.Count > 0; i++) yield return 1; }

        // ================= 2. Sunken Bells =================
        public int BellCasts { get; private set; }

        void RaiseBells(float baseYaw, int toll)
        {
            var needs = SiltCarillonRules.BellNeeds(Standing);
            var spots = SiltCarillonRules.BellSpots(Phase2, baseYaw, needs.Length);
            bells.Clear(); bellResults.Clear(); BellCasts++; firstBells = false;
            for (int i = 0; i < needs.Length; i++)
            {
                var h = new Hazard
                {
                    Source = "Sunken Bell", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = spots[i],
                    Radius = SiltCarillonRules.BellRadius, LockAt = 0, ResolveAt = toll, Damage = SiltCarillonRules.BellTotal(needs[i]),
                    ShareTotal = SiltCarillonRules.BellTotal(needs[i]), ShareCount = needs[i], WipeIfEmpty = false, Soak = true, Knockback = 0, Stun = 0,
                };
                h.OnResolve = hz =>
                {
                    W.BossFx.ChimeResolve(hz.Origin, hz.Radius); CameraShake.Shake(0.1f, 0.15f);
                    if (hz.Hit.Count == 0) DrownedToll(hz.Origin);   // nobody rang it
                };
                W.Hazards.Spawn(h);
                HazardSystem.RaidStyle(h);
                h.Tele.Friendly(BellRim);   // a goal to stand in: the thin friendly rim, plus the big numeral (SiltCarillonHud)
                bells.Add((h, needs[i]));
            }
        }
        // an empty bell: every standing player is hit (no knockback); the tide does not drain
        void DrownedToll(Vector3 at)
        {
            var blow = new Hazard { Source = "Drowned Toll", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = 999, Damage = SiltCarillonRules.EmptyBellDmg, Knockback = 0, Stun = 6 };
            foreach (var p in W.Players.ToArray()) if (!p.Down) { blow.Hit.Add(p); p.ReceiveHit(blow); }
            W.BossFx.TollResolve(Centre, 0, CombatWorld.PlayerBound); CameraShake.Shake(0.2f, 0.3f);
        }

        // the first resolve tick of each bell, before the hit test: who is inside it
        void OnResolving(Hazard h)
        {
            if (Dead || W == null) return;
            int k = bells.FindIndex(b => b.h == h); if (k < 0) return;
            int inside = 0; foreach (var p in W.Players) if (!p.Down && h.Hits(p)) inside++;
            bellResults.Add((bells[k].need, inside));
            if (inside > 0) BellsRung++; else BellsEmpty++;
        }
        IEnumerable<int> WaitBells(int guard = 1800)
        {
            for (int i = 0; i < guard && bellResults.Count < bells.Count; i++) yield return 1;
            bool all = bellResults.Count == bells.Count && bellResults.TrueForAll(r => r.inside > 0);
            BellSetLog.Add((Tick, bells.Count, bells.ConvertAll(b => b.need).ToArray(), bellResults.ConvertAll(r => r.inside).ToArray(), all));
            bells.Clear();
            lastSetRung = all;
        }
        bool lastSetRung;

        // ================= Drained (the punish window) =================
        IEnumerable<int> DrainedWindow()
        {
            Current = "Drained (settling)";
            // the drain's cancelled pools leave this tick; on Hard the Backwash lands first, so the window opens empty
            yield return 1;
            for (int i = 0; i < 600 && W.Hazards.Count > 0; i++) yield return 1;
            Current = "Drained"; Drained = true; int from = Tick;
            yield return DrainedTicks;
            DrainedLog.Add((from, Tick)); Drained = false;
            Current = "Drained (recovery)";
        }

        // ================= tide blocks =================
        IEnumerable<int> TideBells()
        {
            Current = Phase2 ? "Crosscurrent + Sunken Bells" : "High Tide + Sunken Bells";
            float baseYaw = (float)(rng.NextDouble() * 360.0); LastBaseYaw = baseYaw;
            int warn = Beats(SiltCarillonRules.TideWarnBeats(Level, Phase2));
            int toll = Beats(SiltCarillonRules.BellTollBeats(Level, Phase2, firstBells));
            // the mud lasts past the toll: it drains when the bells ring, or ebbs by itself after a failed set
            int mudTicks = Mathf.Max(Phase2 ? Beats(SiltCarillonRules.CrossMudBeats) : Beats(SiltCarillonRules.HighTideMaxBeats), toll - warn + Beats(1));
            if (Phase2) Crosscurrent(baseYaw, 0, warn, mudTicks); else HighTide(warn, mudTicks);
            RaiseBells(baseYaw, toll);
            Windup(0.5f);
            foreach (var w in WaitBells()) yield return w;
            if (lastSetRung)
            {
                Drain(); Release(1.5f);
                foreach (var w in DrainedWindow()) yield return w;
            }
            foreach (var w in WaitMud()) yield return w;
            foreach (var w in WaitQuiet()) yield return w;
        }

        IEnumerable<int> TidePeal()
        {
            float baseYaw = (float)(rng.NextDouble() * 360.0); LastBaseYaw = baseYaw;
            int warn = Beats(SiltCarillonRules.TideWarnBeats(Level, Phase2));
            if (!Phase2)
            {
                Current = "High Tide + Drowned Peal";
                int pealLen = Beats(SiltCarillonRules.PealFirstBeats(Level)) + 2 * Beats(SiltCarillonRules.PealGapBeats(Level));
                HighTide(warn, pealLen + Beats(2));
                yield return warn;
                foreach (var w in Peal(false)) yield return w;
                foreach (var w in WaitMud()) yield return w;
            }
            else
            {
                Current = "Crosscurrent + Drowned Peal";
                int mudT = Beats(SiltCarillonRules.CrossMudBeats);
                var a = Crosscurrent(baseYaw, 0, warn, mudT);
                yield return warn;
                Peal(false).GetEnumerator().MoveNext();   // spawns the three bands now; they resolve by themselves
                for (int i = 0; i < mudT + 2 && mud.Count > 0; i++) yield return 1;
                // the first set has ebbed: the other three sectors get their warning
                Crosscurrent(baseYaw, 1, warn, mudT);
                foreach (var w in WaitMud()) yield return w;
            }
            foreach (var w in WaitQuiet()) yield return w;
        }

        // ================= 3. Silt Fall =================
        public int FallCasts { get; private set; }

        IEnumerable<int> Fall()
        {
            Current = "Silt Fall"; FallCasts++;
            int drops = Phase2 ? SiltCarillonRules.DropsLow : SiltCarillonRules.DropsHigh;
            int lead = Beats(SiltCarillonRules.DropLeadBeats(Level, Phase2)), follow = Beats(SiltCarillonRules.FollowBeats);
            FallLog.Add((Tick, drops, Standing, lead));
            for (int k = 0; k < drops; k++)
            {
                foreach (var p in W.Players) if (!p.Down) Drop(p, follow, follow + lead);
                Windup((k + 1) / (float)drops);
                yield return Beats(1);
            }
            Release(0.8f);
            for (int i = 0; i < 1800 && FallLive > 0; i++) yield return 1;   // the last mounds set
            foreach (var w in WaitQuiet()) yield return w;
        }

        void Drop(PlayerController who, int follow, int total)
        {
            var h = new Hazard
            {
                Source = "Silt Fall", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(who.transform.position),
                Radius = SiltCarillonRules.DropRadius, LockAt = follow, ResolveAt = total, Damage = DropDmg, Knockback = 0, Stun = 6,
                ClipRadius = CombatWorld.PlayerBound + 0.6f,
            };
            h.Track = hz => { if (who && !who.Down) hz.Origin = Sim.Flat(who.transform.position); };
            h.OnResolve = hz => { DropsLanded++; Mound(hz.Origin); };
            Live(h, GroundFill, GroundBorder, 0.4f); DropsSpawned++; FallLive++;
        }
        void Mound(Vector3 at)
        {
            var h = new Hazard
            {
                Source = "Silt Mound", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = SiltCarillonRules.DropRadius,
                LockAt = 0, ResolveAt = Beats(SiltCarillonRules.MoundBeats), BurnPerSecond = MoundDps, Knockback = 0, Stun = 0, ClipRadius = CombatWorld.PlayerBound + 0.6f,
            };
            h.OnResolve = hz => { MoundsSet++; FallLive--; };
            Live(h, MoundFill, MoundBorder); MoundsSpawned++;
        }

        // ================= 4. Drowned Peal =================
        public int PealCasts { get; private set; }

        IEnumerable<int> PealBlock(bool ebb)
        {
            Current = ebb ? "Drowned Peal (ebb)" : "Drowned Peal";
            foreach (var w in Peal(ebb)) yield return w;
            foreach (var w in WaitQuiet()) yield return w;
        }

        // spawns the three bands at once (the first enumeration step), then waits until the last one lands
        IEnumerable<int> Peal(bool ebb)
        {
            PealCasts++;
            int first = Beats(SiltCarillonRules.PealFirstBeats(Level)), gap = Beats(SiltCarillonRules.PealGapBeats(Level));
            PealLog.Add((Tick, ebb, first, gap));
            float clip = CombatWorld.PlayerBound + 0.6f;
            var shapes = new[] { (0f, SiltCarillonRules.BandA, "A"), (SiltCarillonRules.BandA, SiltCarillonRules.BandB, "B"), (SiltCarillonRules.BandB, 30f, "C") };
            for (int i = 0; i < 3; i++)
            {
                var (inner, outer, name) = shapes[ebb ? 2 - i : i];
                int at = first + i * gap;
                var h = new Hazard
                {
                    Source = "Drowned Peal " + name, Shape = inner > 0 ? HazardShape.Ring : HazardShape.Circle, Kind = HazardKind.Ground,
                    Origin = Centre, Inner = inner, Radius = outer, LockAt = i == 0 ? 0 : first + (i - 1) * gap, ResolveAt = at, Damage = PealDmg, Knockback = 0, Stun = 6,
                    ClipRadius = clip,
                };
                h.OnResolve = hz => { PealBands++; W.BossFx.TollResolve(Centre, hz.Inner, Mathf.Min(hz.Radius, CombatWorld.PlayerBound)); CameraShake.Shake(0.1f, 0.15f); Release(0.7f); };
                Live(h, StrikeFill, StrikeBorder, 0.45f);
            }
            yield return first + 2 * gap + 1;
        }

        // ================= the 50% transformation =================
        IEnumerable<int> Transition()
        {
            GatePending = false;
            Current = "Shift"; Log("Shift"); ShiftStart = Tick; HpAtShiftStart = Hp;
            CameraShake.Shake(0.12f, 0.3f);
            yield return ShiftTicks;   // harmless: nothing is spawned, and it stays damageable throughout
            Phase2 = true; beatAge = 0; Release(1.5f); ShiftEnd = Tick;
            Log("tempo -> " + Bpm);
        }

        // ---------------- presentation ----------------
        float slump, shiftT;
        protected override void Update()
        {
            base.Update();
            PresentLook(Drained, Dead, Current == "Shift", Phase2, glowLevel, Time.deltaTime);
        }
        // the slump in the Drained window, the shift wobble and the window/crown glow: the host from its own state, a LAN
        // client's replica from the host's (ReplicaPresent)
        void PresentLook(bool drained, bool dead, bool shift, bool phase2, float glow, float dt)
        {
            if (!body) return;
            slump = Mathf.MoveTowards(slump, drained || dead ? 1 : 0, dt * 2.5f);
            shiftT = shift ? shiftT + dt : 0;
            float wobble = shiftT > 0 ? Mathf.Sin(shiftT * 18f) * 4f : 0;
            body.localRotation = Quaternion.Euler(14f * slump + wobble, 0, 0);
            body.localPosition = new Vector3(0, -0.35f * slump, 0);
            var g = Window * (0.4f + glow * (phase2 ? 1.1f : 0.7f) * (1 - 0.8f * slump));
            if (windowsL) Prim.SetGlow(windowsL, g);
            if (windowsR) Prim.SetGlow(windowsR, g);
            if (crown) Prim.SetGlow(crown, new Color(0.95f, 0.8f, 0.45f) * (0.5f + glow * 0.5f));
        }
        // LAN looks: Glow = core glow; Look[0] = phase 2 (1/0), Look[1] = Drained (1/0). The shift wobble follows the attack name.
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(Phase2 ? 1 : 0); into.Look.Add(Drained ? 1 : 0);
        }
        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            var look = state.Look;
            bool phase2 = look.Count > 0 && look[0] > 0.5f;
            bool drained = look.Count > 1 ? look[1] > 0.5f : state.Attack == "Drained";
            PresentLook(drained, state.Dead, state.Attack == "Shift", phase2, state.Glow, dt);
        }

        protected override void OnDefeat()
        {
            scheduled.Clear(); mud.Clear(); bells.Clear(); bellResults.Clear();
            FallLive = 0; Drained = false;
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        void OnDestroy() { if (W != null) W.Hazards.Resolving -= OnResolving; }
    }
}
