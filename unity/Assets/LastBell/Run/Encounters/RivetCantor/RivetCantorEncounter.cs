using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Rivet Cantor: a practice-only Brass Foundry miniboss (docs/bosses/rivet-cantor.md, a proposal). A foundry
    // choirmaster with a drum: he sings a line and the floor answers it as rivets. Four mechanics:
    //
    //   Work-Song Verse   3 calls (4 below 50%), 3 beats apart; each call's telegraph appears as he sings it and resolves
    //                     6 beats later. Chorus = a shared soak at his feet (70 split; empty: 80 to all). Solo = a 2.6 m circle
    //                     on every player that hurts only the others (spread). Hard: Encore (the last call sung twice).
    //   Rivet Run         an X of rivet lines popping outward from his feet, aimed at the nearest player, then a second X
    //                     aimed at the farthest player popping inward from the edge (after the first, or halfway below 50%).
    //   Rivet Chains      players chained in pairs in party order (an odd player to the drum): a chain shorter than 7 m
    //                     at lock hurts both ends, and the locked chain whips anyone else standing on it.
    //   Anvil Hymn        a disc at his feet, then three rings running outward 1.5 beats apart: step in through them.
    //                     Then Retuning: 3.5 s with nothing live (the major punish window).
    //
    // 50%: he finishes the current mechanic, then a 2 s harmless Key Change (damageable, no heal); 120 -> 140 BPM.
    // Below 50% the chains lock under a Rivet Run (the one two-pattern overlap). No enrage (content rules §5).
    // Visuals: the rigged model (Characters/RivetCantor, 9 Nov clips) when present, else a labelled primitive stand-in;
    // call glyphs above his head, no audio. Strike clips land their strike frame on the gameplay tick (BossClipCues).
    // Every warning goes through Live(), which applies the shared raid look (HazardSystem.RaidStyle, Brass Foundry motif).
    public sealed class RivetCantorEncounter : BossEncounter
    {
        public override string Id => "boss.rivet-cantor";
        public override string DisplayName => "The Rivet Cantor";
        public override string Lesson => "";
        public override float NextPhaseFraction() => 0.49f;
        protected override bool PartyScaled => true;
        // the rigged model when Resources/Characters/RivetCantor(.glb) is in the project, else the labelled stand-in
        public override bool Placeholder => !presenter || !presenter.HasModel;
        CharacterPresenter presenter; BossClipCues cues; public CharacterPresenter Presenter => presenter; public BossClipCues Cues => cues;
        public static readonly string[] ClipNames = { "Idle", "Walk", "ChorusCall", "SoloCall", "VerseLoop", "HammerRaise", "HammerLoop",
            "ForkPlant", "ForkLoop", "ForkStrike", "DrumSlam", "DialLoop", "PhaseShift", "Defeat" };
        // clip lengths and strike frames (seconds), as keyed in rivet_cantor_rig.py
        const float CallLen = 1.2f, CallStrike = 0.35f, RaiseLen = 0.8f, PlantLen = 1.2f, YankLen = 1.0f, YankStrike = 0.55f, SlamLen = 2.2f, SlamStrike = 1.4f;
        float BeatSpeed => Bpm / 120f;   // VerseLoop and HammerLoop are keyed at 120 BPM (one tap or blow per beat)

        // ---------------- tunables (Medium) ----------------
        public const float BpmHigh = 120f, BpmLow = 140f;
        public const int FoundryMotif = 3;   // FX_SH_Telegraph _Motif: BrassFoundry
        public const int ChorusTotal = 70, EmptyChorusDmg = 80, SoloDmg = 40, RivetDmg = 35, ChainSnapDmg = 45, ChainWhipDmg = 40, PulseDmg = 50;
        public const float ChorusRadius = 4f, SoloRadius = 2.6f, SoloFeetRadius = 2.5f, RivetRadius = 0.9f, RivetSpacing = 2f, ChainLength = 7f, WhipWidth = 1.0f;
        public const float DiscRadius = 5f, Ring2 = 8.5f, Ring3 = 12f;
        public const float CallGapBeats = 3f, PulseGapBeats = 1.5f, RivetStepBeats = 0.5f;
        public const int RetuneTicks = 210, ShiftTicks = 120;
        public const float WalkSpeed = 3.2f;
        public int EchoBeats => Easy ? 8 : 6;
        public int RivetFirstBeats => Easy ? 4 : 3;
        public int ChainBeats => Easy ? 6 : 5;
        public int HymnTellBeats => Easy ? 4 : 3;

        public enum Call { Chorus, Solo }
        static readonly Call[][] VersesHigh = { new[] { Call.Chorus, Call.Solo, Call.Chorus }, new[] { Call.Solo, Call.Chorus, Call.Solo } };
        static readonly Call[][] VersesLow = { new[] { Call.Chorus, Call.Solo, Call.Solo, Call.Chorus }, new[] { Call.Solo, Call.Chorus, Call.Chorus, Call.Solo } };
        public static Call[] VersePattern(bool phase2, int index, bool encore)
        {
            var set = phase2 ? VersesLow : VersesHigh;
            var v = new List<Call>(set[((index % set.Length) + set.Length) % set.Length]);
            if (encore) v.Add(v[v.Count - 1]);
            return v.ToArray();
        }

        enum Atk { Verse, Rivets, Chains, Hymn, RivetChains }
        static readonly Atk[] RotationHigh = { Atk.Verse, Atk.Rivets, Atk.Chains, Atk.Hymn };
        static readonly Atk[] RotationLow = { Atk.Verse, Atk.Rivets, Atk.RivetChains, Atk.Hymn };
        public static string[] Rotation(bool phase2) => Array.ConvertAll(phase2 ? RotationLow : RotationHigh, a => a.ToString());

        // placeholder palette (HDR): the Chorus soak gold-amber, the chain partner circles a dimmer coral
        static readonly Color ChorusFill = new Color(1.3f, 0.85f, 0.2f), ChorusBorder = new Color(2.8f, 2.0f, 0.6f),
            ChainFill = new Color(0.7f, 0.22f, 0.12f), ChainBorder = new Color(1.8f, 0.7f, 0.35f),
            Brass = new Color(0.78f, 0.6f, 0.3f), Ivory = new Color(0.93f, 0.9f, 0.82f), Cobalt = new Color(0.16f, 0.22f, 0.5f), Gold = new Color(1f, 0.8f, 0.35f);

        // ---------------- state (tests and diagnostics) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;                       // dev/test hook: replaces the next rotation entry once
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        bool Easy => Level == Difficulty.Easy; bool Hard => Level == Difficulty.Hard;
        public float Bpm => Phase2 ? BpmLow : BpmHigh;
        public int BeatTicks => Mathf.RoundToInt(60f * Sim.Hz / Bpm);
        public int Tick => W != null ? W.Tick : 0;
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }
        public int Styled { get; private set; }          // hazards spawned through Live (raid look applied)
        public int Pending { get; private set; }         // this mechanic's hazards not yet resolved

        public int VerseCasts, RivetCasts, ChainCasts, HymnCasts;
        public int ChorusResolves, ChorusEmpty, ChorusEmptyHits, SoloResolves, SoloHits, SoloFeetHits, RivetPops, RivetHits, ChainSnaps, ChainWhips, PulseHits;
        public readonly List<string> ChainNumerals = new List<string>();   // the order numerals shown at the last chain cast
        public readonly List<(int tick, int inside, int standing, int share)> ChorusLog = new List<(int, int, int, int)>();
        public readonly List<(int tick, Call[] calls)> VerseLog = new List<(int, Call[])>();
        public readonly List<(int tick, int pairs, int drum, int snaps, int whips)> ChainLog = new List<(int, int, int, int, int)>();
        public readonly List<(int tick, float aim1, float aim2, int set2Start, int set1End)> RivetLog = new List<(int, float, float, int, int)>();
        public readonly List<(int start, int end, int liveAtEnd)> RetuneLog = new List<(int, int, int)>();
        public bool Retuning { get; private set; }

        int rotHigh, rotLow, verseIndex;
        float? faceYaw;
        Transform body, drum, mallet, mask;
        readonly List<Transform> glyphs = new List<Transform>();
        readonly List<(PlayerController who, string mark)> marked = new List<(PlayerController, string)>();

        void Awake() { MaxHp = 3600; BodyRadius = 1.3f; HitHeight = 2.2f; MarkHeight = 4.6f; }

        // ---------------- body ----------------
        protected override void Build()
        {
            PhaseName = "";
            presenter = CharacterPresenter.Create(transform, "RivetCantor", 1.5f, 1f, 0, h => { BuildStandIn(h); return h; });
            cues = new BossClipCues(presenter);
            gameObject.AddComponent<RivetCantorReplicaNumeral>().Bind(this);   // LAN client: draws the Chorus numeral (this boss is disabled there)
        }

        // a labelled code-built stand-in, used only when the rigged model is missing
        void BuildStandIn(Transform parent)
        {
            var host = new GameObject("Rivet Cantor (STAND-IN, labelled placeholder)").transform; host.SetParent(parent, false);
            body = Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0, 1.4f, 0), new Vector3(2.0f, 1.4f, 2.0f), Brass);
            Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 1.7f, 0.85f), Vector3.one * 0.55f, Gold, 1.5f);   // the core
            mask = Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 3.1f, 0.1f), new Vector3(0.9f, 1.0f, 0.8f), Ivory);
            drum = Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(-1.25f, 1.3f, 0.3f), new Vector3(1.2f, 0.25f, 1.2f), Cobalt, 0, Quaternion.Euler(0, 0, 90));
            mallet = Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(1.25f, 1.6f, 0.4f), new Vector3(0.18f, 0.7f, 0.18f), Brass, 0, Quaternion.Euler(30, 0, 0));
            Prim.Make(PrimitiveKind.Sphere, mallet, new Vector3(0, 1.1f, 0), Vector3.one * 2.4f, Ivory);
        }

        // ---------------- helpers ----------------
        int Beats(float b) => Mathf.Max(1, Mathf.RoundToInt(b * BeatTicks));
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        int Standing { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }
        List<PlayerController> Party() { var l = new List<PlayerController>(); foreach (var p in W.Players) if (!p.Down) l.Add(p); return l; }

        // spawn + the raid look; Pending counts it until its resolve (or its cancel at defeat)
        Hazard Live(Hazard h, bool pending = true)
        {
            if (pending) { Pending++; var done = h.OnSpent; h.OnSpent = hz => { Pending--; done?.Invoke(hz); }; }
            W.Hazards.Spawn(h);
            HazardSystem.RaidStyle(h, FoundryMotif);
            Styled++;
            return h;
        }
        void Mark(PlayerController p, string m) { if (p && !p.Marks.Contains(m)) { p.Marks.Add(m); marked.Add((p, m)); } }
        void Unmark(PlayerController p, string m) { if (p) p.Marks.Remove(m); marked.RemoveAll(x => x.who == p && x.mark == m); }
        void ClearMarks() { foreach (var (p, m) in marked) if (p) p.Marks.Remove(m); marked.Clear(); }

        PlayerController Farthest()
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players) { if (p.Down) continue; float d = Sim.Flat(p.transform.position - transform.position).magnitude; if (d > bd) { bd = d; best = p; } }
            return best;
        }
        float YawAt(PlayerController p) => p ? Sim.YawTo(transform.position, p.transform.position, transform.eulerAngles.y) : transform.eulerAngles.y;

        IEnumerable<int> WaitPending(int guard = 900) { for (int g = 0; g < guard && Pending > 0; g++) yield return 1; }
        IEnumerable<int> ToCentre() { if (Sim.Flat(transform.position).magnitude > 0.3f) foreach (var w in WalkTo(Vector3.zero, WalkSpeed * 1.5f, 300)) yield return w; }

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            foreach (var w in ToCentre()) yield return w;
            while (true)
            {
                if (!Phase2 && !GatePending && ShiftStart < 0 && Hp * 2 <= MaxHp) GatePending = true;   // once only: not again during the Key Change
                if (GatePending) foreach (var w in KeyChange()) yield return w;
                Blocks++;
                Atk atk;
                if (ForcedNext != null && Enum.TryParse(ForcedNext, out Atk forced)) atk = forced;
                else atk = Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk.ToString());
                IEnumerable<int> run = atk switch
                {
                    Atk.Verse => Verse(),
                    Atk.Rivets => RivetRun(false),
                    Atk.RivetChains => RivetRun(true),
                    Atk.Chains => Chains(),
                    _ => Hymn(),
                };
                foreach (var w in run) yield return w;
                Current = "between blocks"; faceYaw = null;
                yield return Beats(1.5f);
            }
        }

        protected override void StepAlways()
        {
            var t = W.NearestStanding(transform.position);
            TurnTowards(faceYaw ?? (t ? YawAt(t) : transform.eulerAngles.y), 240);
            if (!Phase2 && !GatePending && ShiftStart < 0 && Hp * 2 <= MaxHp) GatePending = true;   // once only: not again during the Key Change
            UpdateChorusNumeral();
            // the call glyphs float above his head, oldest lowest
            for (int i = 0; i < glyphs.Count; i++) if (glyphs[i]) glyphs[i].position = transform.position + Vector3.up * (4.3f + 0.75f * i);
            cues?.Step(Current == "reposition");
        }
        protected override void Update() { base.Update(); if (presenter) presenter.GlowLevel = glowLevel; }
        protected override void OnHalt() { cues?.Clear(); if (cues != null) cues.Loop = "Idle"; }

        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n) { if (n >= 2 && !Phase2) { Phase2 = true; GatePending = false; } }

        IEnumerable<int> KeyChange()
        {
            GatePending = false; Current = "Key Change"; Log("Key Change"); ShiftStart = Tick; HpAtShiftStart = Hp; faceYaw = null;
            CameraShake.Shake(0.12f, 0.3f); Windup(1);
            cues.Clear(); cues.Play("PhaseShift", ShiftTicks); cues.Loop = "Idle";
            yield return ShiftTicks;   // harmless: nothing spawns and he stays damageable
            Phase2 = true; Release(1.5f); ShiftEnd = Tick;
            if (mask) Prim.SetGlow(mask.GetComponent<Renderer>(), Gold * 1.5f);
            Log("tempo -> " + Bpm);
        }

        // ================= 1. Work-Song Verse =================
        IEnumerable<int> Verse()
        {
            foreach (var w in ToCentre()) yield return w;
            Current = "Work-Song Verse"; VerseCasts++;
            var calls = VersePattern(Phase2, verseIndex++, Hard);
            VerseLog.Add((Tick, calls));
            int gap = Beats(CallGapBeats), echo = Beats(EchoBeats);
            // each call's strike frame (the fork on the drum / the stamp) lands on the tick its glyph and telegraph appear
            cues.LoopAt(0, "VerseLoop", BeatSpeed);
            cues.Strike(CallClip(calls[0]), 0, CallStrike, CallLen);
            for (int k = 0; k < calls.Length; k++)
            {
                Sing(calls[k], echo);
                if (k + 1 < calls.Length) cues.Strike(CallClip(calls[k + 1]), gap, CallStrike, CallLen);
                if (mallet) mallet.localRotation = Quaternion.Euler(k % 2 == 0 ? -40 : 30, 0, 0);
                Release(0.5f);
                if (k < calls.Length - 1) for (int t = 0; t < gap; t++) { Windup((t + 1) / (float)gap); yield return 1; }
            }
            Current = "Work-Song Verse (echo)";
            foreach (var w in WaitPending()) yield return w;
            cues.Loop = "Idle";
            Current = "Work-Song Verse (end)";
        }
        static string CallClip(Call c) => c == Call.Chorus ? "ChorusCall" : "SoloCall";

        void Sing(Call c, int echo)
        {
            var glyph = Glyph(c);
            if (c == Call.Chorus)
            {
                int standing = Standing;
                var h = new Hazard
                {
                    Source = "Chorus", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
                    Origin = Sim.Flat(transform.position), Radius = ChorusRadius, LockAt = 0, ResolveAt = echo,
                    Soak = true, ShareTotal = ChorusTotal, ShareCount = standing, Knockback = 0, Stun = 6,
                };
                h.OnResolve = hz =>
                {
                    int inside = hz.Hit.Count; ChorusResolves++;
                    FillChecks++; if (ChorusFilled == inside) FillMatches++;   // the slot ticks a tick earlier vs who was hit
                    // empty: no wipe (review 11-03), a heavy hit on everyone standing instead
                    if (inside == 0)
                    {
                        ChorusEmpty++;
                        foreach (var p in Party()) { hz.Hit.Add(p); p.ReceiveHit(hz, EmptyChorusDmg); ChorusEmptyHits++; }
                    }
                    ChorusLog.Add((Tick, inside, Standing, HazardSystem.ShareOf(hz, Mathf.Max(1, inside))));
                    DropGlyph(glyph); CameraShake.Shake(0.1f, 0.15f);
                };
                var spent = h.OnSpent; h.OnSpent = hz => { choruses.Remove(hz); spent?.Invoke(hz); };
                choruses.Add(h);
                Live(h);
                h.Tele.Tint(ChorusFill, ChorusBorder);
            }
            else
            {
                // one spread circle per standing player: it follows them, locks a beat before it lands, and hurts only others
                var party = Party(); int left = party.Count;
                foreach (var owner in party)
                {
                    var me = owner;
                    var h = new Hazard
                    {
                        Source = "Solo", MechanicId = MechanicIcons.Spread, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
                        Origin = Sim.Flat(me.transform.position), Radius = SoloRadius, LockAt = Mathf.Max(1, echo - Beats(1)), ResolveAt = echo,
                        Damage = SoloDmg, Knockback = 0, Stun = 6, Affects = q => q != me,
                    };
                    h.Track = hz => { if (me && !me.Down) hz.Origin = Sim.Flat(me.transform.position); };
                    h.OnResolve = hz => { SoloHits += hz.Hit.Count; Unmark(me, MechanicIcons.Spread); if (--left == 0) { SoloResolves++; DropGlyph(glyph); } };
                    Mark(me, MechanicIcons.Spread);
                    Live(h);
                }
                if (party.Count == 0) DropGlyph(glyph);
                // and off his feet: a Solo also hits everyone within 2.5 m of him (the 2.8-3.6 m band stays in the 4 m Chorus) (review 11-03: it matters solo too)
                var feet = new Hazard
                {
                    Source = "Solo (his feet)", MechanicId = MechanicIcons.Spread, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
                    Origin = Sim.Flat(transform.position), Radius = SoloFeetRadius, LockAt = 0, ResolveAt = echo,
                    Damage = SoloDmg, Knockback = 0, Stun = 6,
                };
                feet.OnResolve = hz => SoloFeetHits += hz.Hit.Count;
                Live(feet);
            }
        }

        Transform Glyph(Call c)
        {
            var g = MakeGlyph(c);
            g.position = transform.position + Vector3.up * (4.3f + 0.75f * glyphs.Count);
            glyphs.Add(g); glyphNet[g] = (NextNetPropId--, (byte)c);
            return g;
        }
        Transform MakeGlyph(Call c)
        {
            var g = new GameObject(c == Call.Chorus ? "Call glyph: Chorus" : "Call glyph: Solo").transform;
            g.SetParent(transform.parent, false);
            if (c == Call.Chorus) Prim.Make(PrimitiveKind.Cylinder, g, Vector3.zero, new Vector3(0.9f, 0.06f, 0.9f), Gold, 2.5f, Quaternion.Euler(90, 0, 0));
            else for (int i = 0; i < 4; i++) Prim.Make(PrimitiveKind.Sphere, g, new Vector3(-0.45f + 0.3f * i, 0, 0), Vector3.one * 0.22f, Ivory, 1.5f);
            return g;
        }
        void DropGlyph(Transform g) { glyphs.Remove(g); glyphNet.Remove(g); if (g) Destroy(g.gameObject); }
        void ClearGlyphs() { foreach (var g in glyphs) if (g) Destroy(g.gameObject); glyphs.Clear(); glyphNet.Clear(); }
        readonly Dictionary<Transform, (int id, byte kind)> glyphNet = new Dictionary<Transform, (int, byte)>();   // LAN prop id and kind (Call) per glyph
        public int GlyphCount => glyphs.Count;

        // ================= 2. Rivet Run =================
        IEnumerable<int> RivetRun(bool withChains)
        {
            foreach (var w in ToCentre()) yield return w;
            Current = withChains ? "Rivet Run + Rivet Chains" : "Rivet Run"; RivetCasts++;
            var c = Sim.Flat(transform.position);
            float aim1 = YawAt(W.NearestStanding(transform.position));
            int first = Beats(RivetFirstBeats), step = Beats(RivetStepBeats);
            faceYaw = aim1;
            // the drum rises overhead and comes down on the first pop, then one blow per beat while the rivets march
            cues.Strike("HammerRaise", first, RaiseLen, RaiseLen); cues.LoopAt(first, "HammerLoop", BeatSpeed);
            // the first X: outward from his feet, aimed at the nearest player
            int set1Count = 0;
            for (int l = 0; l < 4; l++)
            {
                var dir = Sim.Dir(aim1 + 90f * l); float reach = ToEdge(c, dir, CombatWorld.PlayerBound);
                int i = 0;
                for (float r = RivetSpacing; r <= reach + 0.4f; r += RivetSpacing, i++) Rivet(c + dir * r, first + i * step);
                set1Count = Mathf.Max(set1Count, i);
            }
            int set1End = first + (set1Count - 1) * step;
            if (mallet) mallet.localRotation = Quaternion.Euler(-50, 0, 0);
            for (int t = 0; t < first; t++) { Windup((t + 1) / (float)first); yield return 1; }
            Release(0.8f);
            // the counter-line appears as the first pops: a second X aimed at the farthest player, inward from the edge
            float aim2 = YawAt(Farthest());
            if (Mathf.Abs(Mathf.DeltaAngle(aim1, aim2)) % 90f < 12f || Mathf.Abs(Mathf.DeltaAngle(aim1, aim2)) % 90f > 78f) aim2 = aim1 + 45f;   // never on top of the first X
            int remaining = set1End - first;
            int start2 = Phase2 ? Mathf.Max(Beats(1.5f), remaining / 2) : remaining + Beats(1);
            for (int l = 0; l < 4; l++)
            {
                var dir = Sim.Dir(aim2 + 90f * l); float reach = ToEdge(c, dir, CombatWorld.PlayerBound);
                var pts = new List<float>(); for (float r = RivetSpacing; r <= reach + 0.4f; r += RivetSpacing) pts.Add(r);
                for (int i = 0; i < pts.Count; i++) Rivet(c + dir * pts[pts.Count - 1 - i], start2 + i * step);
            }
            RivetLog.Add((Tick, aim1, aim2, Tick + start2, Tick + remaining));
            if (withChains)
            {
                // the chains lock on the beat after the first X has finished (the one two-pattern overlap)
                int lockIn = Mathf.Max(remaining + Beats(1), Beats(ChainBeats));
                SpawnChains(lockIn);
            }
            foreach (var w in WaitPending()) yield return w;
            cues.Loop = "Idle";
            if (mallet) mallet.localRotation = Quaternion.identity;
            faceYaw = null; Current = "Rivet Run (end)";
        }

        void Rivet(Vector3 at, int resolve)
        {
            var h = new Hazard
            {
                Source = "Rivet", MechanicId = MechanicIcons.DelayedCircle, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
                Origin = at, Radius = RivetRadius, LockAt = 0, ResolveAt = resolve, Damage = RivetDmg, Knockback = 0, Stun = 6,
                ClipRadius = CombatWorld.PlayerBound + 0.6f,
            };
            h.OnResolve = hz => { RivetPops++; RivetHits += hz.Hit.Count; SpawnPop(hz.Origin); };
            Live(h);
        }

        // ---------------- props (art pass 9 Nov: RivetCantorProps.cs) ----------------
        // a rivet head punches up where a rivet warning has just resolved (never before: it must not cover a live warning)
        readonly List<(RivetPop pop, int id)> pops = new List<(RivetPop, int)>();
        void SpawnPop(Vector3 at)
        {
            pops.RemoveAll(x => !x.pop);
            pops.Add((RivetPop.Create(transform.parent, at), NextNetPropId--));
        }

        // the chains: one view per chained pair (or player and drum), ends at the players' waists and the drum
        sealed class ChainRec { public int Id; public RivetChainView View; public PlayerController A, B; public bool Drum; public Vector3 LastA, LastB; }
        readonly List<ChainRec> chains = new List<ChainRec>();
        static int chainSeq;
        public int ChainViewsMade { get; private set; }   // tests

        ChainRec StartChain(PlayerController a, PlayerController b)
        {
            var rec = new ChainRec { Id = ++chainSeq, A = a, B = b, Drum = b == null };
            rec.LastA = a ? a.transform.position : transform.position; rec.LastB = b ? b.transform.position : DrumPoint();
            Func<Vector3> rawA = () => { if (rec.A && !rec.A.Down) rec.LastA = rec.A.transform.position; return rec.LastA; };
            Func<Vector3> rawB = () => { if (rec.Drum) rec.LastB = DrumPoint(); else if (rec.B && !rec.B.Down) rec.LastB = rec.B.transform.position; return rec.LastB; };
            rec.View = RivetChainView.Create(transform.parent, () => WaistEnd(rawA(), rec.LastB), () => rec.Drum ? rawB() : WaistEnd(rawB(), rec.LastA));
            chains.RemoveAll(c => !c.View);
            chains.Add(rec); ChainViewsMade++;
            return rec;
        }
        static Vector3 WaistEnd(Vector3 self, Vector3 other)
        {
            var d = other - self; d.y = 0;
            return new Vector3(self.x, 0, self.z) + Vector3.up * RivetCantorProps.WaistHeight + (d.sqrMagnitude > 1e-4f ? d.normalized * RivetCantorProps.SideOffset : Vector3.zero);
        }

        // the drum's centre: the rig's drum hand (rivet_cantor_rig.py: the drum centre is 0.70 m along hand.R), else the
        // stand-in drum, else his hip
        Transform drumBone; bool drumSearched;
        public Vector3 DrumPoint()
        {
            if (!drumSearched) { drumSearched = true; if (presenter && presenter.HasModel) drumBone = FindDeep(presenter.Visual, "hand.R"); }
            if (drumBone) return drumBone.position + drumBone.up * 0.70f * drumBone.lossyScale.y;
            if (drum) return drum.position;
            return transform.position + Vector3.up * 1.2f;
        }
        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
            return null;
        }
        void ClearProps()
        {
            foreach (var (p, _) in pops) if (p) Destroy(p.gameObject);
            pops.Clear();
            foreach (var c in chains) if (c.View) Destroy(c.View.gameObject);
            chains.Clear();
            foreach (var v in replicaChains.Values) if (v.View) Destroy(v.View.gameObject);
            replicaChains.Clear();
        }

        // ================= 3. Rivet Chains =================
        IEnumerable<int> Chains()
        {
            foreach (var w in ToCentre()) yield return w;
            Current = "Rivet Chains";
            if (mallet) mallet.localRotation = Quaternion.Euler(0, 0, 70);
            int lockIn = Beats(ChainBeats);
            SpawnChains(lockIn);
            // the fork is driven into the floor as the chains appear, hums while they tighten, and is wrenched out on the lock
            cues.Play("ForkPlant", Mathf.RoundToInt(PlantLen * Sim.Hz)); cues.LoopAt(0, "ForkLoop");
            cues.Strike("ForkStrike", lockIn, YankStrike, YankLen);
            for (int t = 0; t < lockIn; t++) { Windup((t + 1) / (float)lockIn); yield return 1; }
            Release(1f); cues.Loop = "Idle";
            foreach (var w in WaitPending()) yield return w;
            if (mallet) mallet.localRotation = Quaternion.identity;
            Current = "Rivet Chains (end)";
        }

        void SpawnChains(int lockIn)
        {
            ChainCasts++;
            // pairs follow the order numerals (review 11-03): I-II and III-IV by party slot, shown over each player while
            // chained. A player whose numeral partner is missing or down (player III of 3) is chained to the drum.
            var slots = W.Players;
            int pairs = 0, drumChained = 0, snaps0 = ChainSnaps, whips0 = ChainWhips; int left = 0;
            var numbered = new List<(PlayerController p, string mark)>();
            Action done = () =>
            {
                if (--left > 0) return;
                ChainLog.Add((Tick, pairs, drumChained, ChainSnaps - snaps0, ChainWhips - whips0));
                foreach (var (p, m) in numbered) Unmark(p, m);
            };
            for (int i = 0; i < slots.Count; i += 2)
            {
                var a = slots[i]; var b = i + 1 < slots.Count ? slots[i + 1] : null;
                bool aUp = a && !a.Down, bUp = b && !b.Down;
                foreach (var (p, n) in new[] { (a, i + 1), (b, i + 2) })
                    if (p && !p.Down) { var m = MechanicIcons.Order(n); if (m != null) { Mark(p, m); numbered.Add((p, m)); } }
                if (aUp && bUp)
                {
                    pairs++; left += 3;
                    Partner(a, b, lockIn, done); Partner(b, a, lockIn, done);
                    Whip(a, b, lockIn, done);
                }
                else if (aUp || bUp) { drumChained++; left++; DrumChain(aUp ? a : b, lockIn, done); }
            }
            ChainNumerals.Clear(); foreach (var (p, m) in numbered) ChainNumerals.Add(m);
        }

        // the 7 m circle round `anchor` that hurts only `who`: the chain is too short if `who` is inside at lock
        void Partner(PlayerController who, PlayerController anchor, int lockIn, Action done)
        {
            var h = new Hazard
            {
                Source = "Rivet Chain (snap)", MechanicId = MechanicIcons.StretchTether, Shape = HazardShape.Circle, Kind = HazardKind.Strike,
                Origin = Sim.Flat(anchor.transform.position), Radius = ChainLength - Hazard.Margin, LockAt = Mathf.Max(1, lockIn - 6), ResolveAt = lockIn,
                Damage = ChainSnapDmg, Knockback = 0, Stun = 6, Affects = q => q == who,
            };
            h.Track = hz => { if (anchor && !anchor.Down) hz.Origin = Sim.Flat(anchor.transform.position); };
            h.OnResolve = hz => { ChainSnaps += hz.Hit.Count; Unmark(who, MechanicIcons.StretchTether); done(); };
            Mark(who, MechanicIcons.StretchTether);
            Live(h);
            h.Tele.Tint(ChainFill, ChainBorder);
        }

        // the chain itself: a thin lane between the partners that whips anyone else standing on it at lock
        void Whip(PlayerController a, PlayerController b, int lockIn, Action done)
        {
            var h = new Hazard
            {
                Source = "Rivet Chain (whip)", MechanicId = MechanicIcons.Lane, Shape = HazardShape.Lane, Kind = HazardKind.Strike,
                Width = WhipWidth, LockAt = Mathf.Max(1, lockIn - 6), ResolveAt = lockIn, Damage = ChainWhipDmg, Knockback = 0, Stun = 6,
                Affects = q => q != a && q != b,
            };
            Action<Hazard> place = hz =>
            {
                var pa = Sim.Flat(a.transform.position); var pb = Sim.Flat(b.transform.position);
                hz.Origin = pa; hz.Yaw = Sim.YawTo(pa, pb, hz.Yaw); hz.Length = Mathf.Max(0.1f, Sim.Flat(pb - pa).magnitude);
            };
            place(h); h.Length = Mathf.Max(1f, h.Length);
            float baseLen = h.Length;
            // the lane mesh is built at its spawn length: stretch it with the chain (presentation only; the hit test reads Length)
            h.Track = hz => { if (!a || !b) return; place(hz); if (hz.Tele != null && hz.Tele.go) hz.Tele.go.transform.localScale = new Vector3(1, 1, hz.Length / baseLen); };
            var chain = StartChain(a, b);
            h.OnResolve = hz =>
            {
                ChainWhips += hz.Hit.Count; done();
                if (chain.View) chain.View.Lock(a && b && Sim.Flat(a.transform.position - b.transform.position).magnitude < ChainLength);
            };
            Live(h);
        }

        void DrumChain(PlayerController who, int lockIn, Action done)
        {
            var h = new Hazard
            {
                Source = "Rivet Chain (drum)", MechanicId = MechanicIcons.StretchTether, Shape = HazardShape.Circle, Kind = HazardKind.Strike,
                Origin = Sim.Flat(transform.position), Radius = ChainLength - Hazard.Margin, LockAt = 0, ResolveAt = lockIn,
                Damage = ChainSnapDmg, Knockback = 0, Stun = 6, Affects = q => q == who,
            };
            var chain = StartChain(who, null);
            h.OnResolve = hz =>
            {
                ChainSnaps += hz.Hit.Count; Unmark(who, MechanicIcons.StretchTether); done();
                if (chain.View) chain.View.Lock(who && Sim.Flat(who.transform.position - transform.position).magnitude < ChainLength);
            };
            Mark(who, MechanicIcons.StretchTether);
            Live(h);
            h.Tele.Tint(ChainFill, ChainBorder);
        }

        // ================= 4. Anvil Hymn + Retuning =================
        IEnumerable<int> Hymn()
        {
            foreach (var w in ToCentre()) yield return w;
            Current = "Anvil Hymn"; HymnCasts++;
            var c = Sim.Flat(transform.position);
            int tell = Beats(HymnTellBeats), gap = Beats(PulseGapBeats);
            float edge = CombatWorld.PlayerBound + 1.5f;
            Pulse(new Hazard { Shape = HazardShape.Circle, Origin = c, Radius = DiscRadius }, tell);
            Pulse(new Hazard { Shape = HazardShape.Ring, Origin = c, Inner = DiscRadius, Radius = Ring2 }, tell + gap);
            Pulse(new Hazard { Shape = HazardShape.Ring, Origin = c, Inner = Ring2, Radius = Ring3 }, tell + 2 * gap);
            Pulse(new Hazard { Shape = HazardShape.Ring, Origin = c, Inner = Ring3, Radius = edge }, tell + 3 * gap);
            // the drum hauled overhead and slammed down on the disc, then a blow on each ring (one HammerLoop cycle = 2 rings)
            cues.Strike("DrumSlam", tell, SlamStrike, SlamLen);
            cues.At(tell + gap, "HammerLoop", 2 * gap); cues.At(tell + 3 * gap, "HammerLoop", 2 * gap);
            if (drum) drum.localPosition = new Vector3(0, 4.0f, 0.2f);
            for (int t = 0; t < tell; t++) { Windup((t + 1) / (float)tell); yield return 1; }
            if (drum) drum.localPosition = new Vector3(0, 1.0f, 1.0f);
            Release(1.4f);
            foreach (var w in WaitPending()) yield return w;
            // Retuning: the drum skin split, he re-tensions it. Nothing is live (the major punish window).
            Current = "Retuning"; Retuning = true; int from = Tick;
            cues.Clear(); cues.Loop = "DialLoop"; cues.Play("DialLoop", 2 * Sim.Hz);   // kneeling over the drum, retuning it
            if (body) body.localScale = new Vector3(2.0f, 1.0f, 2.0f);
            yield return RetuneTicks;
            RetuneLog.Add((from, Tick, W.Hazards.Count)); Retuning = false; cues.Loop = "Idle";
            if (body) body.localScale = new Vector3(2.0f, 1.4f, 2.0f);
            if (drum) drum.localPosition = new Vector3(-1.25f, 1.3f, 0.3f);
            Current = "Anvil Hymn (end)";
        }

        void Pulse(Hazard h, int resolve)
        {
            h.Source = "Anvil Hymn"; h.MechanicId = h.Shape == HazardShape.Ring ? MechanicIcons.Ring : MechanicIcons.DelayedCircle;
            h.Kind = HazardKind.Ground; h.LockAt = 0; h.ResolveAt = resolve; h.Damage = PulseDmg; h.Knockback = 0; h.Stun = 6;
            h.ClipRadius = CombatWorld.PlayerBound + 0.6f;
            h.OnResolve = hz => { PulseHits += hz.Hit.Count; W.BossFx.TollResolve(hz.Origin, hz.Inner, Mathf.Min(hz.Radius, CombatWorld.PlayerBound)); CameraShake.Shake(0.1f, 0.15f); };
            Live(h);
        }

        // ---------------- lifecycle ----------------
        // ---------------- LAN ----------------
        // props: the call glyphs over his head (kind 0 = Chorus, 1 = Solo, as Call) and the rivet pops (kind 2, the RivetHead
        // model). Looks: Look[0] = phase 2 (mask glow), Look[1..3] = mallet local euler, Look[4..6] = drum local position,
        // Look[7] = body height scale (the Retuning squash), Look[8] = chains shown (max 4), then 6 per chain: id, end A x/z,
        // end B x/z (players' feet; the drum for a drum chain), flags (1 = B is the drum, + 2 x RivetChainView.State).
        public const byte NetRivetPop = 2;
        const int MaxNetChains = 4;
        public override void CaptureNetProps(List<Networking.LanWorldFrame.Prop> into)
        {
            foreach (var g in glyphs) if (g && glyphNet.TryGetValue(g, out var n)) into.Add(NetProp(n.id, n.kind, g.position));
            foreach (var (p, id) in pops) if (p) into.Add(NetProp(id, NetRivetPop, p.transform.position));
        }
        public override Transform MakeNetProp(byte kind) => kind <= (byte)Call.Solo ? MakeGlyph((Call)kind)
            : kind == NetRivetPop ? RivetPop.Create(transform.parent, Vector3.zero).transform : null;
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(Phase2 ? 1 : 0);
            var m = mallet ? mallet.localEulerAngles : Vector3.zero; into.Look.Add(m.x); into.Look.Add(m.y); into.Look.Add(m.z);
            var d = drum ? drum.localPosition : Vector3.zero; into.Look.Add(d.x); into.Look.Add(d.y); into.Look.Add(d.z);
            into.Look.Add(body ? body.localScale.y : 1.4f);
            chains.RemoveAll(c => !c.View);
            int n = Mathf.Min(chains.Count, MaxNetChains); into.Look.Add(n);
            for (int i = 0; i < n; i++)
            {
                var c = chains[i];
                into.Look.Add(c.Id); into.Look.Add(c.LastA.x); into.Look.Add(c.LastA.z); into.Look.Add(c.LastB.x); into.Look.Add(c.LastB.z);
                into.Look.Add((c.Drum ? 1 : 0) + 2 * (int)c.View.Now);
            }
        }
        bool maskLit;
        // LAN client: chains drawn from the Look, their player ends snapped to the nearest drawn player (smooth at any net rate)
        sealed class ReplicaChain { public RivetChainView View; public Vector3 A, B; public bool Drum; }
        readonly Dictionary<int, ReplicaChain> replicaChains = new Dictionary<int, ReplicaChain>();
        readonly List<int> chainGone = new List<int>();
        public int ReplicaChainsShown { get; private set; }   // LAN test: chain views a client built
        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            var look = state.Look;
            if (look.Count > 0 && look[0] > 0.5f && !maskLit && mask) { Prim.SetGlow(mask.GetComponent<Renderer>(), Gold * 1.5f); maskLit = true; }
            if (look.Count >= 8)
            {
                if (mallet) mallet.localRotation = Quaternion.Euler(look[1], look[2], look[3]);
                if (drum) drum.localPosition = new Vector3(look[4], look[5], look[6]);
                if (body) body.localScale = new Vector3(2.0f, look[7], 2.0f);
            }
            chainGone.Clear(); chainGone.AddRange(replicaChains.Keys);
            int n = look.Count >= 9 ? Mathf.Clamp(Mathf.RoundToInt(look[8]), 0, MaxNetChains) : 0;
            for (int i = 0; i < n && look.Count >= 9 + 6 * (i + 1); i++)
            {
                int o = 9 + 6 * i, id = Mathf.RoundToInt(look[o]), flags = Mathf.RoundToInt(look[o + 5]);
                bool isDrum = (flags & 1) != 0; var st = (RivetChainView.State)(flags >> 1);
                chainGone.Remove(id);
                if (!replicaChains.TryGetValue(id, out var rc))
                {
                    if (st != RivetChainView.State.Live) continue;   // joined after the lock: nothing to show
                    rc = new ReplicaChain(); replicaChains[id] = rc;
                }
                rc.A = new Vector3(look[o + 1], 0, look[o + 2]); rc.B = new Vector3(look[o + 3], 0, look[o + 4]); rc.Drum = isDrum;
                if (!rc.View && st == RivetChainView.State.Live)
                {
                    var e = rc;   // the view reads its ends from this record for as long as it lives
                    rc.View = RivetChainView.Create(transform.parent,
                        () => WaistEnd(SnapToPlayer(e.A), e.Drum ? DrumPoint() : SnapToPlayer(e.B)),
                        () => e.Drum ? DrumPoint() : WaistEnd(SnapToPlayer(e.B), SnapToPlayer(e.A)));
                    ReplicaChainsShown++;
                }
                if (rc.View && st != RivetChainView.State.Live) rc.View.Lock(st == RivetChainView.State.Snap);
            }
            foreach (int id in chainGone)
            {
                var v = replicaChains[id].View;
                if (v && v.Now == RivetChainView.State.Live) Destroy(v.gameObject);   // a locked one finishes its snap/lash on its own
                replicaChains.Remove(id);
            }
        }
        Vector3 SnapToPlayer(Vector3 at)
        {
            Vector3 best = at; float bd = 1.5f * 1.5f;
            if (W != null) foreach (var p in W.Players) if (p) { var d = p.transform.position - at; d.y = 0; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = p.transform.position; } }
            return best;
        }

        protected override void OnDefeat()
        {
            ClearGlyphs(); ClearMarks(); ClearProps(); Pending = 0; Retuning = false; choruses.Clear(); ChorusNeed = -1;
            cues?.Clear();
            if (presenter) presenter.Action("Defeat", 150);
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
        void OnDestroy() { ClearGlyphs(); ClearMarks(); ClearProps(); choruses.Clear(); ChorusNeed = -1; }

        // ---------------- the Chorus numeral (review 11-03) ----------------
        // The soonest live Chorus shows how many players it needs (everyone standing, live, not the count at the call) as a big
        // ivory numeral with one slot tick per player, filled for each player inside now: the Mould Press soak look. It sits
        // just outside the rim on the camera's side (his body would hide it above him, and the 3-3.5 m band is where players
        // stand between a Chorus and a Solo). Drawn in OnGUI by this host only: LAN clients see the soak, not the numeral yet.
        readonly List<Hazard> choruses = new List<Hazard>();
        public int ChorusNeed { get; private set; } = -1;    // -1: no Chorus live
        public int ChorusFilled { get; private set; }
        public int NumeralTicks, NumeralMin = int.MaxValue, NumeralMax, FillMatches, FillChecks;   // tests
        Vector3 chorusAt; Vector3 camSide = Vector3.forward; GUIStyle numStyle;
        static readonly Color InkShadow = new Color(0.05f, 0.04f, 0.03f, 0.85f);

        void UpdateChorusNumeral()
        {
            Hazard next = null;
            foreach (var h in choruses) if (h.Age < h.ResolveAt && (next == null || h.ResolveAt - h.Age < next.ResolveAt - next.Age)) next = h;
            if (next == null || Dead) { ChorusNeed = -1; return; }
            int need = 0, inside = 0;
            foreach (var p in W.Players) if (p && !p.Down) { need++; if (next.Contains(p.transform.position)) inside++; }
            ChorusNeed = need; ChorusFilled = inside; chorusAt = next.Origin;
            NumeralTicks++; NumeralMin = Mathf.Min(NumeralMin, need); NumeralMax = Mathf.Max(NumeralMax, need);
        }

        void OnGUI() => DrawChorusNumeral();

        // LAN client: the replica is never stepped, so the soonest live "Chorus" stand-in gives the numeral (everyone standing,
        // and who is inside it now at the drawn time), drawn by RivetCantorReplicaNumeral
        public int ReplicaNumeralsShown { get; private set; }   // LAN test: frames a client drew the Chorus numeral
        internal void ReplicaChorusGui()
        {
            if (!IsNetReplica || W == null) return;
            if (Event.current.type == EventType.Layout)   // once per GUI frame
            {
                Hazard next = null;
                foreach (var h in W.Hazards.Live)
                    if (h != null && h.Replica && !h.Cancelled && h.Source == "Chorus" && h.Age < h.ResolveAt && (next == null || h.ResolveAt - h.Age < next.ResolveAt - next.Age)) next = h;
                if (next == null || Dead) ChorusNeed = -1;
                else
                {
                    int need = 0, inside = 0;
                    foreach (var p in W.Players) if (p && !p.Down) { need++; if (next.Contains(p.transform.position)) inside++; }
                    ChorusNeed = need; ChorusFilled = inside; chorusAt = next.Origin; ReplicaNumeralsShown++;
                }
            }
            DrawChorusNumeral();
        }

        void DrawChorusNumeral()
        {
            if (ChorusNeed < 0) return;
            var cam = Camera.main; if (cam == null) return;
            // steady side: the flattened camera forward, smoothed, so shake or follow does not slide it round the rim
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude > 1e-4f) camSide = Vector3.Slerp(camSide, -fwd.normalized, 0.15f);
            var sp = cam.WorldToScreenPoint(chorusAt + camSide.normalized * (ChorusRadius + 0.4f) + Vector3.up * 0.3f);
            if (sp.z <= 0) return;
            if (numStyle == null) numStyle = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var c = new Vector2(sp.x, Screen.height - sp.y);
            var rect = new Rect(c.x - 50, c.y - 44, 100, 80);
            string label = ChorusNeed.ToString();
            numStyle.normal.textColor = InkShadow; GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), label, numStyle);
            numStyle.normal.textColor = Ivory; GUI.Label(rect, label, numStyle);
            int slots = Mathf.Max(ChorusNeed, ChorusFilled);
            float x0 = c.x - (slots * 18 - 6) / 2f;
            for (int i = 0; i < slots; i++)
            {
                var old = GUI.color;
                GUI.color = i < ChorusFilled ? Ivory : new Color(0.1f, 0.12f, 0.16f, 0.8f);
                GUI.DrawTexture(new Rect(x0 + i * 18, c.y + 32, 12, 5), Texture2D.whiteTexture);
                GUI.color = old;
            }
        }
    }

    // LAN client: the Rivet Cantor replica is a disabled component, so its OnGUI never runs there; this enabled helper draws
    // the Chorus numeral from the hazard stand-ins instead (does nothing on the host or offline).
    public sealed class RivetCantorReplicaNumeral : MonoBehaviour
    {
        RivetCantorEncounter b;
        public void Bind(RivetCantorEncounter boss) { b = boss; }
        void OnGUI() { if (b && b.IsNetReplica) b.ReplicaChorusGui(); }
    }
}
