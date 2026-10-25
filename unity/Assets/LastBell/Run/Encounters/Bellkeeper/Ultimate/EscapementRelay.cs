using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Escapement Relay (`bellkeeper.ult_escapement_relay`), Bellkeeper Ultimate v2 — proposal, card 01 (cards-v2), built
    // 2026-10-24 as the playable P1 slice, not human-approved.
    // Four brass plates lie on the floor (N/E/S/W, r 7 m) and the escapement's hour hand turns on the boss. Every 2.5 s the
    // hand ticks 90° clockwise and the beat resolves on the plate it points at. Each player holds an hour numeral (I..IV,
    // party order) and every beat a duty from its place in the cycle, (hour − 1 + beat) mod 4:
    //   SOAK    stand on the plate the hand points at
    //   CHAIN   hold the pendulum chain: ≥ 9 m from the boss on the far half (away from the hand)
    //   WEIGHT  stand on a counterweight ✕ 3.2 m out (in melee reach), 90° left (L) or right (R) of the hand
    // The cycle is card 01's, SOAK → WEIGHT R → WEIGHT L → CHAIN → SOAK, so each player travels round the dial with the
    // hand: two short hops (✕ to ✕ beside the boss, ✕ out to the chain) and two long ones (plate across the boss to the far
    // ✕, ~11 m; chain round to the next plate, ~12 m) that need a sprint. Nobody can stand still for a beat. (A spoke-only
    // cycle was tried on 2026-10-24 and rejected by Chzy18: players just went back and forth.)
    // Nothing is unavoidable: a perfect beat costs nobody HP. Fails, on the beat tick: plate empty = WIPE; more players on
    // the plate than soakers = 60 to each on it; a chain holder short or on the near half = the pendulum swings through the
    // plate (a 999 lane from the boss over it); a ✕ empty = a 120° cone on that side, 999.
    // Party sizes: K = min(players, 4) duties; trio SOAK → WEIGHT L → CHAIN, duo SOAK → CHAIN, solo SOAK every beat.
    // 5-8 players: players 5-8 take hours I..IV again and share that hour's duty with its first holder (two soakers).
    // Cue: At[0] = the first beat (s after launch), then one every BeatSeconds until StopAfterBeat(). The beat number (so
    // the hand and the duties) carries over between casts and starts at 2 on a fresh fight (as in the P1 paper simulation, cards-v2/p1-example.html).
    public sealed class EscapementRelay : UltimateMechanic
    {
        public override string Id => UltimateMechanics.EscapementRelay;
        public override string Name => "Escapement Relay";

        public const string Source = "Escapement Relay";
        public const float BeatSeconds = 2.5f, PlateRadius = 7f, PlateSize = 1.6f, OnPlate = 1.8f, ChainMin = 9f, ChainHint = 10.2f,
            WeightRadius = 3.2f, WeightSize = 1.2f;
        public const int OverSoakDamage = 60, FirstBeat = 2;
        public static readonly string[] DutyNames = { "SOAK", "CHAIN", "WEIGHT L", "WEIGHT R" };

        // Beat is the dial position of the next beat (its hand angle and duties). It steps +1 per beat, or −1 while the
        // escapement runs back: after each run of Runs[i] beats the hand reverses ("REWIND"), and everyone retraces their
        // route beat by beat (each hop is one they just made, backwards). The HUD warns a beat ahead.
        public int Beat { get; private set; } = FirstBeat;   // the next beat to resolve
        public int LastBeat { get; private set; } = FirstBeat - 1;   // the dial position of the beat that just resolved
        public int Dir { get; private set; } = 1;
        static readonly int[] Runs = { 7, 4, 6, 3, 8, 5 };
        int runIndex, runLeft = Runs[0];
        public int Reversals { get; private set; }
        public int NextBeatTick { get; private set; } = -1;
        public int Beats { get; private set; }               // resolved this fight (tests)
        public int BeatsClean { get; private set; }
        public readonly List<string> Fails = new List<string>();
        bool stop;

        // the dial position k beats after the next one (0 = Beat), following the rewind schedule
        public int BeatAhead(int k)
        {
            int b = Beat, dir = Dir, left = runLeft, idx = runIndex;
            for (int i = 0; i < k; i++)
            {
                if (--left == 0) { dir = -dir; idx = (idx + 1) % Runs.Length; left = Runs[idx]; }
                b += dir;
            }
            return b;
        }
        public bool RewindAfterNext => runLeft == 1;   // the beat after the next one turns back
        void Advance()
        {
            LastBeat = Beat;
            if (--runLeft == 0) { Dir = -Dir; runIndex = (runIndex + 1) % Runs.Length; runLeft = Runs[runIndex]; Reversals++; Boss.PlanLog.Add((Now, "relay rewind")); }
            Beat += Dir;
        }

        public override UltCue DefaultCue() => new UltCue { Variant = "practice", At = new[] { 4f } };
        public void StopAfterBeat() => stop = true;

        // ---------------- the rule ----------------
        // plan angle (0 = north, clockwise on screen) to a world direction: north is −Z, east is −X
        public static Vector3 PlanDir(float a) { a *= Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a)); }
        public static float HandAngle(int beat) => 90f * beat;
        public static Vector3 Plate(int beat) => PlanDir(HandAngle(beat)) * PlateRadius;
        public static Vector3 Weight(int beat, int duty) => PlanDir(HandAngle(beat) + (duty == 2 ? -90 : 90)) * WeightRadius;
        public static Vector3 ChainSpot(int beat) => PlanDir(HandAngle(beat) + 180) * ChainHint;
        public int Duties => Mathf.Clamp(Party, 1, 4);
        public int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? 0 : i % 4 + 1; }
        static readonly int[][] Cycle = { new[] { 0 }, new[] { 0, 1 }, new[] { 0, 2, 1 }, new[] { 0, 3, 2, 1 } };
        public int DutyOf(PlayerController p, int beat) { int h = HourOf(p), k = Duties; return h == 0 ? -1 : Cycle[k - 1][(((h - 1 + beat) % k) + k) % k]; }
        public static bool ChainOk(Vector3 q, int beat) => Sim.Flat(q).magnitude >= ChainMin && Vector3.Dot(Sim.Flat(q), PlanDir(HandAngle(beat))) <= 0;
        public bool Holds(PlayerController p, int beat)
        {
            var q = Sim.Flat(p.transform.position);
            switch (DutyOf(p, beat))
            {
                case 0: return (q - Plate(beat)).magnitude <= OnPlate;
                case 1: return ChainOk(q, beat);
                case 2: case 3: return (q - Weight(beat, DutyOf(p, beat))).magnitude <= WeightSize;
            }
            return true;
        }

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            stop = false;
            Build();
            NextBeatTick = TickAt(Cue.At0(BeatSeconds));
            ShowBeat(true);
            while (true)
            {
                while (Now < NextBeatTick) { Present(); yield return 1; }
                Resolve(Beat);
                Advance(); Beats++;
                NextBeatTick += Sim.Ticks(BeatSeconds);
                if (stop) break;
                ShowBeat(false);
                Present();
                yield return 1;
            }
            NextBeatTick = -1;
            ClearNotes();
            Teardown();
        }

        void Resolve(int b)
        {
            var players = W.Players; int k = Duties; bool clean = true;
            var plate = Plate(b); int soakers = 0, onPlate = 0;
            foreach (var p in players)
            {
                if (p.Down) continue;
                if (DutyOf(p, b) == 0) soakers++;
                if ((Sim.Flat(p.transform.position) - plate).magnitude <= OnPlate) onPlate++;
            }
            Boss.Face(Quaternion.LookRotation(PlanDir(HandAngle(b))).eulerAngles.y);
            W.BossFx.ChimeResolve(plate, PlateSize);
            if (onPlate == 0) { Fail(b, "empty plate"); Ult.Wipe("The escapement skips (empty plate)"); return; }
            if (onPlate > Mathf.Max(1, soakers))
            {
                clean = false; Fail(b, "too many on the plate");
                Spawn(new Hazard { Source = Source + ": crowded plate", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = plate, Radius = OnPlate, LockAt = 0, ResolveAt = UltShapes.ResolveIn(0), Damage = OverSoakDamage });
            }
            if (k >= 2)
            {
                bool chain = false, any = false;
                foreach (var p in players) if (!p.Down && DutyOf(p, b) == 1) { any = true; chain |= ChainOk(p.transform.position, b); }
                bool chainFail = false;
                foreach (var p in players) if (!p.Down && DutyOf(p, b) == 1 && !ChainOk(p.transform.position, b)) chainFail = true;
                if (!any || !chain || chainFail)
                {
                    clean = false; Fail(b, "short chain");
                    var dir = PlanDir(HandAngle(b));
                    var h = Spawn(new Hazard { Source = Source + ": the pendulum swings", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = Quaternion.LookRotation(dir).eulerAngles.y, Length = 13f, Width = 2.6f, LockAt = 0, ResolveAt = UltShapes.ResolveIn(0), Damage = UltimateState.Lethal });
                    h.OnResolve = hz => W.BossFx.SweepResolve(Vector3.zero, hz.Yaw, 13f, 12f);
                }
            }
            for (int duty = 2; duty < k; duty++)
            {
                var spot = Weight(b, duty); bool held = false;
                foreach (var p in players) if (!p.Down && (Sim.Flat(p.transform.position) - spot).magnitude <= WeightSize) held = true;
                if (held) continue;
                clean = false; Fail(b, duty == 2 ? "empty weight L" : "empty weight R");
                var dir = Sim.Flat(spot).normalized;
                var h = Spawn(new Hazard { Source = Source + ": the bell tips", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = Quaternion.LookRotation(dir).eulerAngles.y, Radius = 13f, HalfAngle = 60, LockAt = 0, ResolveAt = UltShapes.ResolveIn(0), Damage = UltimateState.Lethal });
                h.OnResolve = hz => W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle);
            }
            if (clean) BeatsClean++;
        }
        void Fail(int b, string why) { Fails.Add($"beat {b}: {why}"); Boss.PlanLog.Add((Now, "relay fail " + why)); }

        // The guide: be on your duty spot on the next beat. The hand shows the next plate from the previous beat on, so
        // the rule is known one beat ahead (the rotation is learnable, but the bot reads it like a player watching the hand).
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || NextBeatTick < 0 || p.Down) return;
            int b = Beat, d = DutyOf(p, b); if (d < 0) return;
            int tick = NextBeatTick, since = tick - Sim.Ticks(BeatSeconds);
            var plate = Plate(b); int slot = W.Players.IndexOf(p) / 4;   // the 2nd holder of an hour stands beside the 1st
            BotDemand r = new BotDemand { Tick = tick, Since = since, From = this, Why = $"relay beat {b}: {DutyNames[d]}" };
            switch (d)
            {
                case 0:
                    var side = Vector3.Cross(Vector3.up, PlanDir(HandAngle(b))) * (Party > 4 ? (slot == 0 ? -0.55f : 0.55f) : 0);
                    r.Hint = plate + side; var c = r.Hint; float tol = Party > 4 ? 0.6f : 1.2f;
                    r.Ok = q => (Sim.Flat(q) - c).magnitude <= tol; break;
                case 1:
                    r.Hint = ChainSpot(b); int bb = b;
                    r.Ok = q => Sim.Flat(q).magnitude >= ChainMin + 0.4f && Vector3.Dot(Sim.Flat(q), PlanDir(HandAngle(bb))) <= -1f && (Sim.Flat(q) - plate).magnitude > OnPlate + 0.5f; break;
                default:
                    var w = Weight(b, d); r.Hint = w;
                    r.Ok = q => (Sim.Flat(q) - w).magnitude <= WeightSize - 0.35f; break;
            }
            into.Add(r);
            // everyone who isn't soaking keeps off the plate
            if (d != 0) into.Add(new BotDemand { Tick = tick, Since = since, From = this, Hint = r.Hint, Why = "relay: off the plate", Ok = q => (Sim.Flat(q) - plate).magnitude > OnPlate + 0.5f });
        }

        // the HUD line for the run's own player: the next beat and their duty
        public string HudText(PlayerController p)
        {
            if (!Running || NextBeatTick < 0 || p == null) return null;
            float left = (NextBeatTick - Now) / (float)Sim.Hz; int d = DutyOf(p, Beat); if (d < 0) return null;
            return $"beat in {Mathf.Max(0, left):0.0}s · you ({UltimateState.Roman[HourOf(p)]}): {DutyNames[d]} · then {DutyNames[DutyOf(p, BeatAhead(1))]}" + (RewindAfterNext ? "   ·   REWIND after this beat" : Dir < 0 ? "   ·   rewinding" : "");
        }

        // ---------------- presentation (stand-ins: brass plates, the hour hand, ivory ✕, the chain band, labels) ----------------
        // Readability pass (Chzy18, 2026-10-24: "I don't see any of that"): the hand is drawn twice (a glowing line on the
        // floor to the lit plate, and a bar above the boss's head), the ✕ are large, the chain zone is a shaded band behind
        // the boss, every spot has a word over it, player labels are coloured by duty, and a REWIND warning flashes. Practice
        // aid (ShowMySpot, on by default): a beam on the run's own player's spot for this beat.
        public static bool ShowMySpot = true;
        Transform root, pivot, mySpot; readonly Renderer[] plates = new Renderer[4];
        readonly List<Transform> chains = new List<Transform>();
        static Material brassDim, brassHot, ivory, coralM, brassM, chainBand, spotM;
        float shownYaw;
        public static readonly Color[] DutyColors = { new Color(1f, 0.8f, 0.35f), new Color(0.5f, 0.75f, 1f), new Color(0.97f, 0.95f, 0.9f), new Color(0.97f, 0.95f, 0.9f) };

        void Build()
        {
            if (root) return;
            if (!brassDim)
            {
                brassDim = Prim.Material(new Color(0.55f, 0.43f, 0.2f), 0.35f); brassHot = Prim.Material(new Color(1f, 0.8f, 0.4f), 2.2f);
                ivory = Prim.Material(new Color(0.97f, 0.95f, 0.88f), 1.8f); coralM = Prim.Material(new Color(1f, 0.25f, 0.2f), 1.6f);
                brassM = Prim.Material(new Color(0.9f, 0.7f, 0.32f), 1.4f); chainBand = Prim.Material(new Color(0.2f, 0.32f, 0.6f), 0.6f);
                spotM = Prim.Material(new Color(0.3f, 1f, 0.85f), 2.5f);
            }
            root = new GameObject("Escapement Relay (stand-in)").transform; root.SetParent(W.Root, false);
            for (int i = 0; i < 4; i++)
            {
                var t = Prim.Make(PrimitiveKind.Cylinder, root, PlanDir(90 * i) * PlateRadius + Vector3.up * 0.04f, new Vector3(PlateSize * 2, 0.02f, PlateSize * 2), Color.white);
                plates[i] = t.GetComponent<Renderer>(); plates[i].sharedMaterial = brassDim;
            }
            pivot = new GameObject("hand").transform; pivot.SetParent(root, false); pivot.localPosition = Vector3.zero;
            // the hand: a glowing line on the floor out to the plate, and a bar with an arrowhead above the boss's head
            Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(0, 0.06f, 3.6f), new Vector3(0.45f, 0.03f, 4.6f), Color.white), brassHot);
            Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(0, 4.2f, 3.0f), new Vector3(0.4f, 0.2f, 6.0f), Color.white), brassM);
            Mat(Prim.Make(PrimitiveKind.Cone, pivot, new Vector3(0, 4.2f, 6.4f), new Vector3(1.1f, 0.9f, 1.1f), Color.white, 0, Quaternion.Euler(90, 0, 0)), brassM);
            foreach (float side in new[] { -1f, 1f })   // the counterweight ✕ (pivot-local: x = 90° right of the hand), 2.6 m wide
                for (int s = -1; s <= 1; s += 2)
                    Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(side * WeightRadius, 0.06f, 0), new Vector3(0.32f, 0.03f, 2.6f), Color.white, 0, Quaternion.Euler(0, 45 * s, 0)), ivory);
            for (int k = -7; k <= 7; k++)   // the chain zone: a band from 9 m to the edge on the far half
            {
                float deg = 180 + 12 * k, a = deg * Mathf.Deg2Rad, mid = (ChainMin + 11.5f) / 2;
                Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(Mathf.Sin(a) * mid, 0.03f, Mathf.Cos(a) * mid), new Vector3(2.1f, 0.02f, 11.5f - ChainMin), Color.white, 0, Quaternion.Euler(0, deg, 0)), chainBand);
                Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(Mathf.Sin(a) * ChainMin, 0.05f, Mathf.Cos(a) * ChainMin), new Vector3(2.0f, 0.03f, 0.14f), Color.white, 0, Quaternion.Euler(0, deg, 0)), ivory);
            }
            // practice aid: a beam on the run's own player's spot
            mySpot = new GameObject("my spot").transform; mySpot.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cylinder, mySpot, new Vector3(0, 0.07f, 0), new Vector3(1.1f, 0.02f, 1.1f), Color.white), spotM);
            Mat(Prim.Make(PrimitiveKind.Cylinder, mySpot, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f), Color.white), spotM);
        }
        static void Mat(Transform t, Material m) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        void ShowBeat(bool snap)
        {
            int lit = ((Beat % 4) + 4) % 4;
            for (int i = 0; i < 4; i++) if (plates[i]) plates[i].sharedMaterial = i == lit ? brassHot : brassDim;
            if (snap && pivot) { shownYaw = Quaternion.LookRotation(PlanDir(HandAngle(Beat))).eulerAngles.y; pivot.rotation = Quaternion.Euler(0, shownYaw, 0); }
        }

        // where a player should stand for the next beat (the guide's spot: the plate, the ✕, or straight across for the chain)
        public Vector3 SpotOf(PlayerController p) { int d = DutyOf(p, Beat); return d == 0 ? Plate(Beat) : d == 1 ? ChainSpot(Beat) : Weight(Beat, d); }

        void Present()
        {
            if (!pivot) return;
            float want = Quaternion.LookRotation(PlanDir(HandAngle(Beat))).eulerAngles.y;
            shownYaw = Mathf.MoveTowardsAngle(shownYaw, want, 720 * Sim.Dt);   // the tick: a quick 90° snap
            pivot.rotation = Quaternion.Euler(0, shownYaw, 0);
            // chains to this beat's holders (brass when long enough on the far half, coral when not)
            int n = 0;
            foreach (var p in W.Players)
            {
                if (p.Down || DutyOf(p, Beat) != 1) continue;
                if (chains.Count <= n) chains.Add(Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, Vector3.one, Color.white));
                var c = chains[n++]; c.gameObject.SetActive(true);
                var q = Sim.Flat(p.transform.position); float len = q.magnitude;
                c.position = q * 0.5f + Vector3.up * 0.9f; c.rotation = Quaternion.LookRotation(len > 0.01f ? q : Vector3.forward); c.localScale = new Vector3(0.1f, 0.1f, len);
                Mat(c, ChainOk(q, Beat) ? brassM : coralM);
            }
            for (int i = n; i < chains.Count; i++) chains[i].gameObject.SetActive(false);
            var me = W.Players.Count > 0 ? W.Players[0] : null;
            if (mySpot) { bool show = ShowMySpot && me && !me.Down && DutyOf(me, Beat) >= 0; mySpot.gameObject.SetActive(show); if (show) mySpot.position = SpotOf(me); }
        }
        void ClearNotes() { foreach (var p in W.Players) Ult.Of(p).Note = null; }
        void Teardown()
        {
            if (root) Object.Destroy(root.gameObject);
            root = pivot = mySpot = null; chains.Clear();
            for (int i = 0; i < 4; i++) plates[i] = null;
        }

        // Labels, drawn by the encounter's OnGUI: words on the spots, each player's hour and duty in its colour, REWIND.
        static GUIStyle spotStyle, nameStyle, bigStyle;
        public void DrawGui(Camera cam)
        {
            if (!Running || NextBeatTick < 0 || cam == null) return;
            if (spotStyle == null)
            {
                spotStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                nameStyle = new GUIStyle(spotStyle) { fontSize = 17 };
                bigStyle = new GUIStyle(spotStyle) { fontSize = 40 };
            }
            Label(cam, Plate(Beat), 0.3f, "PLATE", DutyColors[0], spotStyle);
            if (Duties >= 2) Label(cam, ChainSpot(Beat), 0.3f, "CHAIN", DutyColors[1], spotStyle);
            if (Duties >= 3) Label(cam, Weight(Beat, 2) + Sim.Flat(Weight(Beat, 2)).normalized * 1.4f, 0.3f, "L", DutyColors[2], spotStyle);
            if (Duties >= 4) Label(cam, Weight(Beat, 3) + Sim.Flat(Weight(Beat, 3)).normalized * 1.4f, 0.3f, "R", DutyColors[3], spotStyle);
            for (int i = 0; i < W.Players.Count; i++)
            {
                var p = W.Players[i]; int d = DutyOf(p, Beat); if (p.Down || d < 0) continue;
                var debt = Ult.Of(p).DebtNote;
                Label(cam, p.transform.position, 2.9f, (i == 0 ? "YOU " : "") + UltimateState.Roman[HourOf(p)] + "  " + DutyNames[d] + (debt != null ? "  · " + debt : ""), DutyColors[d], nameStyle);
            }
            if (RewindAfterNext || Dir < 0)
            {
                bool soon = RewindAfterNext; float blink = Mathf.PingPong(Time.unscaledTime * 3, 1);
                bigStyle.normal.textColor = soon ? Color.Lerp(new Color(1f, 0.45f, 0.35f), Color.white, blink) : new Color(1f, 0.6f, 0.5f, 0.7f);
                GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 60), soon ? "REWIND NEXT BEAT" : "<< REWINDING", bigStyle);
            }
        }
        static void Label(Camera cam, Vector3 world, float height, string text, Color c, GUIStyle style)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(world.x, height, world.z)); if (sp.z <= 0) return;
            var r = new Rect(sp.x - 120, Screen.height - sp.y - 13, 240, 26);
            style.normal.textColor = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c; GUI.Label(r, text, style);
        }

        // -lastbellAbility ult_escapement_relay: the relay alone, 20 beats, then 4 s off
        public override IEnumerable<int> Practice()
        {
            Boss.Launch(this);
            for (int i = 0; i < Sim.Ticks(4 + BeatSeconds * 20) && Running; i++) yield return 1;
            StopAfterBeat();
            while (Running) yield return 1;
            yield return 240;
        }

        public override void Cancel()
        {
            base.Cancel();
            Teardown(); NextBeatTick = -1; stop = false;
            if (Boss?.Ult != null) foreach (var kv in Ult.All) kv.Value.Note = null;
        }
        // a new fight starts the beat count over (the encounter calls Cancel on restart too, but keeps counters for tests)
        public void ResetBeats() { Beat = FirstBeat; LastBeat = FirstBeat - 1; Dir = 1; runIndex = 0; runLeft = Runs[0]; Reversals = 0; Beats = 0; BeatsClean = 0; Fails.Clear(); }
    }
}
