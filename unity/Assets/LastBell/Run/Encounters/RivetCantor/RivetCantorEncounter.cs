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
    //                     6 beats later. Chorus = a shared soak at his feet (70 split, empty wipes). Solo = a 2.6 m circle
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
    // Placeholder visuals only: primitive stand-in body, call glyphs above his head, no audio.
    // Every warning goes through Live(), which applies the shared raid look (HazardSystem.RaidStyle, Brass Foundry motif).
    public sealed class RivetCantorEncounter : BossEncounter
    {
        public override string Id => "boss.rivet-cantor";
        public override string DisplayName => "The Rivet Cantor";
        public override string Lesson => "";
        public override float NextPhaseFraction() => 0.49f;
        protected override bool PartyScaled => true;

        // ---------------- tunables (Medium) ----------------
        public const float BpmHigh = 120f, BpmLow = 140f;
        public const int FoundryMotif = 3;   // FX_SH_Telegraph _Motif: BrassFoundry
        public const int ChorusTotal = 70, SoloDmg = 40, RivetDmg = 35, ChainSnapDmg = 45, ChainWhipDmg = 40, PulseDmg = 50;
        public const float ChorusRadius = 3.5f, SoloRadius = 2.6f, RivetRadius = 0.9f, RivetSpacing = 2f, ChainLength = 7f, WhipWidth = 1.0f;
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
        public int ChorusResolves, ChorusEmpty, SoloResolves, SoloHits, RivetPops, RivetHits, ChainSnaps, ChainWhips, PulseHits;
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

        // ---------------- placeholder body ----------------
        protected override void Build()
        {
            PhaseName = "";
            var host = new GameObject("Rivet Cantor (STAND-IN, labelled placeholder)").transform; host.SetParent(transform, false);
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
            // the call glyphs float above his head, oldest lowest
            for (int i = 0; i < glyphs.Count; i++) if (glyphs[i]) glyphs[i].position = transform.position + Vector3.up * (4.3f + 0.75f * i);
        }

        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n) { if (n >= 2 && !Phase2) { Phase2 = true; GatePending = false; } }

        IEnumerable<int> KeyChange()
        {
            GatePending = false; Current = "Key Change"; Log("Key Change"); ShiftStart = Tick; HpAtShiftStart = Hp; faceYaw = null;
            CameraShake.Shake(0.12f, 0.3f); Windup(1);
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
            for (int k = 0; k < calls.Length; k++)
            {
                Sing(calls[k], echo);
                if (mallet) mallet.localRotation = Quaternion.Euler(k % 2 == 0 ? -40 : 30, 0, 0);
                Release(0.5f);
                if (k < calls.Length - 1) for (int t = 0; t < gap; t++) { Windup((t + 1) / (float)gap); yield return 1; }
            }
            Current = "Work-Song Verse (echo)";
            foreach (var w in WaitPending()) yield return w;
            Current = "Work-Song Verse (end)";
        }

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
                    Soak = true, ShareTotal = ChorusTotal, ShareCount = standing, WipeIfEmpty = true, Knockback = 0, Stun = 6,
                };
                h.OnResolve = hz =>
                {
                    int inside = hz.Hit.Count; ChorusResolves++; if (inside == 0) ChorusEmpty++;
                    ChorusLog.Add((Tick, inside, Standing, HazardSystem.ShareOf(hz, Mathf.Max(1, inside))));
                    DropGlyph(glyph); CameraShake.Shake(0.1f, 0.15f);
                };
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
            }
        }

        Transform Glyph(Call c)
        {
            var g = new GameObject(c == Call.Chorus ? "Call glyph: Chorus" : "Call glyph: Solo").transform;
            g.SetParent(transform.parent, false);
            if (c == Call.Chorus) Prim.Make(PrimitiveKind.Cylinder, g, Vector3.zero, new Vector3(0.9f, 0.06f, 0.9f), Gold, 2.5f, Quaternion.Euler(90, 0, 0));
            else for (int i = 0; i < 4; i++) Prim.Make(PrimitiveKind.Sphere, g, new Vector3(-0.45f + 0.3f * i, 0, 0), Vector3.one * 0.22f, Ivory, 1.5f);
            g.position = transform.position + Vector3.up * (4.3f + 0.75f * glyphs.Count);
            glyphs.Add(g);
            return g;
        }
        void DropGlyph(Transform g) { glyphs.Remove(g); if (g) Destroy(g.gameObject); }
        void ClearGlyphs() { foreach (var g in glyphs) if (g) Destroy(g.gameObject); glyphs.Clear(); }
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
            h.OnResolve = hz => { RivetPops++; RivetHits += hz.Hit.Count; };
            Live(h);
        }

        // ================= 3. Rivet Chains =================
        IEnumerable<int> Chains()
        {
            foreach (var w in ToCentre()) yield return w;
            Current = "Rivet Chains";
            if (mallet) mallet.localRotation = Quaternion.Euler(0, 0, 70);
            int lockIn = Beats(ChainBeats);
            SpawnChains(lockIn);
            for (int t = 0; t < lockIn; t++) { Windup((t + 1) / (float)lockIn); yield return 1; }
            Release(1f);
            foreach (var w in WaitPending()) yield return w;
            if (mallet) mallet.localRotation = Quaternion.identity;
            Current = "Rivet Chains (end)";
        }

        void SpawnChains(int lockIn)
        {
            ChainCasts++;
            var party = Party();
            int pairs = 0, drumChained = 0, snaps0 = ChainSnaps, whips0 = ChainWhips; int left = 0;
            Action done = () => { if (--left == 0) ChainLog.Add((Tick, pairs, drumChained, ChainSnaps - snaps0, ChainWhips - whips0)); };
            for (int i = 0; i < party.Count; i += 2)
            {
                if (i + 1 < party.Count)
                {
                    var a = party[i]; var b = party[i + 1]; pairs++;
                    left += 3;
                    Partner(a, b, lockIn, done); Partner(b, a, lockIn, done);
                    Whip(a, b, lockIn, done);
                }
                else { drumChained++; left++; DrumChain(party[i], lockIn, done); }
            }
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
            h.OnResolve = hz => { ChainWhips += hz.Hit.Count; done(); };
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
            h.OnResolve = hz => { ChainSnaps += hz.Hit.Count; Unmark(who, MechanicIcons.StretchTether); done(); };
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
            if (drum) drum.localPosition = new Vector3(0, 4.0f, 0.2f);
            for (int t = 0; t < tell; t++) { Windup((t + 1) / (float)tell); yield return 1; }
            if (drum) drum.localPosition = new Vector3(0, 1.0f, 1.0f);
            Release(1.4f);
            foreach (var w in WaitPending()) yield return w;
            // Retuning: the drum skin split, he re-tensions it. Nothing is live (the major punish window).
            Current = "Retuning"; Retuning = true; int from = Tick;
            if (body) body.localScale = new Vector3(2.0f, 1.0f, 2.0f);
            yield return RetuneTicks;
            RetuneLog.Add((from, Tick, W.Hazards.Count)); Retuning = false;
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
        protected override void OnDefeat()
        {
            ClearGlyphs(); ClearMarks(); Pending = 0; Retuning = false;
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
        void OnDestroy() { ClearGlyphs(); ClearMarks(); }
    }
}
