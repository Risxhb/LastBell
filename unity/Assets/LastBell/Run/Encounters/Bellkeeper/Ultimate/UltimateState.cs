using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Shared systems of the Bellkeeper Ultimate (ULTIMATE.md §10), owned by the encounter (BellkeeperEncounter.Ult) and
    // used by every mechanic: the arena's stations and fissure, personal status with ivory glyphs, Stilled, Cracked Hour,
    // first-body lanes, the ledger recorder, and wipes. Reset() on start, halt, phase change and defeat.
    public sealed class UltimateState
    {
        public const int Lethal = 999;   // one-shot (ULTIMATE.md §2)
        public const int CrackedTicks = 20 * Sim.Hz, CrackedPartyDamage = 30;

        readonly BellkeeperEncounter boss;
        CombatWorld W => boss.World;
        public UltimateState(BellkeeperEncounter b) { boss = b; }

        // ---------------- arena: the four rim ritual stations and the gold fissure ----------------
        // The Throne's stations stand on the rim at plan 45° + 90° k (NE, SE, SW, NW; ShatteredHourArenaBuilder.BuildStations),
        // r 13.2 m. North is −Z, east is −X (the camera looks north). ULTIMATE.md writes "N/E/S/W"; the build follows the
        // arena's real diagonal stations (recorded in the report).
        public static readonly string[] StationNames = { "NE", "SE", "SW", "NW" };
        public const float StationRadius = 13.2f;
        public static Vector3 StationDir(int i) { float a = (45 + 90 * (((i % 4) + 4) % 4)) * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a)); }
        public static Vector3 Station(int i) => StationDir(i) * StationRadius;
        // The fissure runs NW <-> SE through the centre (plan nw = (−1, 1) -> world (+1, 0, −1)).
        public static readonly Vector3 FissureDir = new Vector3(1, 0, -1).normalized;
        public static Vector3 FissureNormal => Vector3.Cross(Vector3.up, FissureDir);   // (−1, 0, −1)/√2 ... the NE side

        // ---------------- personal status ----------------
        public sealed class Status
        {
            public int Hour;               // 1..4 (I..IV), 0 = no clock
            public int Station = -1;       // the clock hand's station 0..3, -1 = none
            public int StrikeTick = -1;    // world tick of this clock's strike
            public int StillFrom = -1, StillUntil = -1;   // Stilled window [from, until)
            public int HourglassFrom = -1;  // the cracked-hourglass glyph shows from this tick until StillFrom
            public int CrackedUntil = -1;
            public string Note;            // extra glyph text (a mechanic's own marker), null = none
            public Transform Glyph;        // the world glyph above the head (built on demand)
        }
        readonly Dictionary<PlayerController, Status> status = new Dictionary<PlayerController, Status>();
        public Status Of(PlayerController p)
        {
            if (!status.TryGetValue(p, out var s)) status[p] = s = new Status();
            return s;
        }
        public IEnumerable<KeyValuePair<PlayerController, Status>> All => status;

        public void SetClock(PlayerController p, int hour, int station, int strikeTick)
        {
            var s = Of(p); s.Hour = hour; s.Station = station; s.StrikeTick = strikeTick;
        }
        public void ClearClock(PlayerController p) { var s = Of(p); s.Hour = 0; s.StrikeTick = -1; }   // the hand's station stays (Stopped Hour reads it)
        // the station a player's clock hand points to now (or last pointed to), -1 = never had one
        public int StationOf(PlayerController p) => status.TryGetValue(p, out var s) ? s.Station : -1;

        // Stilled: can't move, act, dash or drink from `from` for `ticks` (the hourglass glyph shows `warn` ticks before).
        public void Still(PlayerController p, int from, int ticks, int warn)
        {
            var s = Of(p); s.StillFrom = from; s.StillUntil = from + ticks; s.HourglassFrom = from - warn;
        }
        public bool IsStilled(PlayerController p, int tick) => status.TryGetValue(p, out var s) && tick >= s.StillFrom && tick < s.StillUntil;
        public bool IsStilled(PlayerController p) => IsStilled(p, W.Tick);

        // Cracked Hour (a clock that missed the boss): 30 to everyone and the failing player deals no damage for 20 s.
        public int CrackedCount { get; private set; }
        public void CrackedHour(PlayerController failing, string source = "Cracked Hour")
        {
            CrackedCount++;
            if (failing) Of(failing).CrackedUntil = W.Tick + CrackedTicks;
            var h = new Hazard { Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = 99, Damage = CrackedPartyDamage, Knockback = 0, Stun = 0 };
            foreach (var p in W.Players.ToArray()) if (!p.Down) p.ReceiveHit(h);
            boss.PlanLog.Add((W.Tick, "cracked " + (failing ? failing.name : "-")));
        }
        public bool IsCracked(PlayerController p) => status.TryGetValue(p, out var s) && W.Tick < s.CrackedUntil;

        // ---------------- wipes ----------------
        public string WipeCause { get; private set; }
        public int Wipes { get; private set; }
        // Ends the attempt for the whole party (push timer, enrage, a mechanic that wipes): a lethal hit on every standing
        // player. The director then sees nobody standing and goes to Downed, as for any defeat.
        public void Wipe(string cause)
        {
            WipeCause = cause; Wipes++;
            boss.PlanLog.Add((W.Tick, "wipe " + cause));
            var h = new Hazard { Source = cause, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = 99, Damage = Lethal * 10, Knockback = 0, Stun = 0 };
            foreach (var p in W.Players.ToArray()) if (!p.Down) p.ReceiveHit(h);
        }

        // ---------------- first-body lanes ----------------
        // A Strike lane that stops at the first body along it: the first standing player inside it (takes damageFor(p)) or
        // the Bellkeeper's body (harmless). Spawned by `owner` (so it is the owner's hazard); resolves once, at resolveAt.
        // firstHit: called with the player struck (null = the boss's body, or nobody, stopped it).
        public Hazard FirstBodyLane(UltimateMechanic owner, string source, Vector3 from, Vector3 toward, float width, int resolveAt,
                                    Func<PlayerController, int> damageFor, Action<PlayerController> firstHit = null, float length = 30f)
        {
            var dir = Sim.Flat(toward - from); if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
            var h = new Hazard
            {
                Source = source, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = Sim.Flat(from), Yaw = Quaternion.LookRotation(dir).eulerAngles.y,
                Length = length, Width = width, LockAt = 0, ResolveAt = resolveAt, HitOnResolve = false,
            };
            h.OnResolve = hz =>
            {
                var first = FirstBody(hz, out bool bossFirst);
                if (first != null && !bossFirst)
                {
                    hz.Damage = damageFor(first);
                    hz.Hit.Add(first); first.ReceiveHit(hz);
                }
                firstHit?.Invoke(bossFirst ? null : first);
            };
            return owner.SpawnShared(h);
        }
        // the first body along a lane: a standing player inside it, or the boss (bossFirst) if his body is reached first
        public PlayerController FirstBody(Hazard lane, out bool bossFirst)
        {
            var dir = Sim.Dir(lane.Yaw); PlayerController best = null; float bestAlong = float.MaxValue;
            foreach (var p in W.Players)
            {
                if (p.Down || !lane.Contains(p.transform.position)) continue;
                float along = Vector3.Dot(Sim.Flat(p.transform.position - lane.Origin), dir);
                if (along < bestAlong) { bestAlong = along; best = p; }
            }
            bossFirst = false;
            var b = W.Boss;
            if (b != null && b.Solid)
            {
                var to = Sim.Flat(b.transform.position - lane.Origin); float along = Vector3.Dot(to, dir), side = (to - dir * along).magnitude;
                if (along > 0 && side <= lane.Width / 2 + b.BodyRadius)
                {
                    float hitAt = along - Mathf.Sqrt(Mathf.Max(0, b.BodyRadius * b.BodyRadius - side * side));
                    if (hitAt < bestAlong) { bossFirst = true; return null; }
                }
            }
            return best;
        }

        // ---------------- the ledger (recorder) ----------------
        // A resolved footprint, stored exactly as it struck. ToHazard() makes a fresh live copy (the caller spawns it).
        public sealed class Footprint
        {
            public string Source, Label; public int Tick; public HazardShape Shape; public HazardKind Kind;
            public Vector3 Origin; public float Yaw, Radius, Inner, HalfAngle, Length, Width; public int Damage;
            public static Footprint Of(Hazard h, int tick, string label = null) => new Footprint
            {
                Source = h.Source, Label = label, Tick = tick, Shape = h.Shape, Kind = h.Kind, Origin = h.Origin, Yaw = h.Yaw,
                Radius = h.Radius, Inner = h.Inner, HalfAngle = h.HalfAngle, Length = h.Length, Width = h.Width, Damage = h.Damage,
            };
            public Hazard ToHazard(string source, int resolveAt, int damage) => new Hazard
            {
                Source = source, Shape = Shape, Kind = Kind, Origin = Origin, Yaw = Yaw, Radius = Radius, Inner = Inner,
                HalfAngle = HalfAngle, Length = Length, Width = Width, LockAt = 0, ResolveAt = resolveAt, Damage = damage,
            };
            public bool Contains(Vector3 p) => ToHazard(Source, 1, 0).Contains(p);
        }
        // Recording: while on, every hazard any Ultimate move spawns is stored when it resolves (in resolve order). The
        // Ledger mechanic turns it on for P1's first loop; Backwards Bell may record its own blocks the same way.
        public bool Recording;
        public readonly List<Footprint> Recorded = new List<Footprint>();
        public event Action<Footprint> FootprintRecorded;
        // The moves set OnResolve after Spawn, so the recorder wraps it on the next tick (Step runs before the hazards
        // resolve); a hazard that resolves on its own spawn tick is recorded from that list instead.
        readonly List<Hazard> toWrap = new List<Hazard>();
        internal void OnSpawned(BellkeeperAbility by, Hazard h) { if (Recording) toWrap.Add(h); }
        void WrapPending()
        {
            foreach (var h in toWrap)
            {
                if (h.Cancelled) continue;
                if (h.Age >= h.ResolveAt) { if (Recording) Record(h, W.Tick - 1); continue; }
                var show = h.OnResolve;
                h.OnResolve = hz => { show?.Invoke(hz); if (Recording) Record(hz, W.Tick); };
            }
            toWrap.Clear();
        }
        void Record(Hazard h, int tick) { var f = Footprint.Of(h, tick); Recorded.Add(f); FootprintRecorded?.Invoke(f); }
        // An ivory dashed outline of a footprint (a recorded or future shape: never coral). Dismiss() it when done.
        public BellkeeperFx.Telegraph Outline(Footprint f)
        {
            var fx = W.BossFx;
            var t = f.Shape switch
            {
                HazardShape.Cone => fx.Cone(f.Radius, f.HalfAngle),
                HazardShape.Ring => fx.Ring(f.Inner, f.Radius),
                HazardShape.Lane => fx.Lane(f.Length, f.Width),
                _ => fx.Circle(f.Radius),
            };
            t.Memory(); t.Place(f.Origin, f.Yaw);
            return t;
        }

        // ---------------- per tick and reset ----------------
        readonly HashSet<PlayerController> heldStill = new HashSet<PlayerController>();
        public void Step()
        {
            int t = W.Tick;
            if (toWrap.Count > 0) WrapPending();
            foreach (var p in W.Players)
            {
                bool still = !p.Down && IsStilled(p, t);
                if (still && !heldStill.Contains(p)) { heldStill.Add(p); p.MovementEnabled = false; }
                else if (!still && heldStill.Remove(p)) p.MovementEnabled = true;
            }
        }
        public void Reset()
        {
            foreach (var p in heldStill) if (p) p.MovementEnabled = true;
            heldStill.Clear(); toWrap.Clear();
            foreach (var kv in status) if (kv.Value.Glyph) UnityEngine.Object.Destroy(kv.Value.Glyph.gameObject);
            status.Clear();
            Recording = false;
        }
        // a new attempt forgets the ledger too (practice from P4 uses the Ledger's authored clean ledger)
        public void ResetAll() { Reset(); Recorded.Clear(); WipeCause = null; CrackedCount = 0; }

        // ---------------- glyphs (stand-in: ivory disc, brass hand, numeral on the HUD) ----------------
        static Material ivory, brass, cobalt;
        public static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII" };
        // Called every frame by the encounter: places each player's glyph and returns the HUD text over their head.
        public string GlyphText(PlayerController p, int tick)
        {
            if (!status.TryGetValue(p, out var s)) return null;
            bool clock = s.Hour > 0 && (s.StrikeTick < 0 || tick <= s.StrikeTick);
            bool glass = s.HourglassFrom >= 0 && tick >= s.HourglassFrom && tick < s.StillUntil;
            bool cracked = tick < s.CrackedUntil;
            bool any = clock || glass || cracked || s.Note != null;
            if (s.Glyph) s.Glyph.gameObject.SetActive(any && !p.Down);
            if (!any || p.Down) return null;
            if (!s.Glyph) s.Glyph = BuildGlyph(p);
            var head = p.transform.position + Vector3.up * 2.9f;
            s.Glyph.position = head; s.Glyph.rotation = Quaternion.identity;
            var hand = s.Glyph.Find("hand");
            if (hand)
            {
                hand.gameObject.SetActive(clock && s.Station >= 0);
                if (s.Station >= 0) hand.rotation = Quaternion.LookRotation(StationDir(s.Station));
            }
            var glassT = s.Glyph.Find("hourglass"); if (glassT) glassT.gameObject.SetActive(glass);
            var crackT = s.Glyph.Find("cracked"); if (crackT) crackT.gameObject.SetActive(cracked);
            var parts = new List<string>();
            if (clock) parts.Add(Roman[Mathf.Clamp(s.Hour, 0, 13)] + (s.Station >= 0 ? " " + StationNames[s.Station] : ""));
            if (glass) parts.Add(tick >= s.StillFrom ? "STILLED" : "HOURGLASS");
            if (cracked) parts.Add("CRACKED");
            if (s.Note != null) parts.Add(s.Note);
            return string.Join(" · ", parts);
        }
        static Transform BuildGlyph(PlayerController p)
        {
            if (ivory == null) { ivory = Prim.Material(new Color(0.95f, 0.92f, 0.84f), 0.6f); brass = Prim.Material(new Color(0.82f, 0.64f, 0.3f), 0.5f); cobalt = Prim.Material(new Color(0.22f, 0.32f, 0.62f), 0.4f); }
            var root = new GameObject("Ultimate glyph · " + p.name).transform;
            root.SetParent(p.World.Root, false);
            Part(PrimitiveKind.Cylinder, root, "face", Vector3.zero, new Vector3(0.9f, 0.02f, 0.9f), ivory, Quaternion.identity);
            var hand = new GameObject("hand").transform; hand.SetParent(root, false);
            Part(PrimitiveKind.Cube, hand, "pointer", new Vector3(0, 0.03f, 0.22f), new Vector3(0.07f, 0.03f, 0.44f), brass, Quaternion.identity);
            var glass = new GameObject("hourglass").transform; glass.SetParent(root, false);
            Part(PrimitiveKind.Cone, glass, "top", new Vector3(0, 0.55f, 0), new Vector3(0.36f, 0.25f, 0.36f), ivory, Quaternion.Euler(180, 0, 0));
            Part(PrimitiveKind.Cone, glass, "bottom", new Vector3(0, 0.3f, 0), new Vector3(0.36f, 0.25f, 0.36f), ivory, Quaternion.identity);
            var crack = new GameObject("cracked").transform; crack.SetParent(root, false);
            Part(PrimitiveKind.Cube, crack, "crack", new Vector3(0, 0.04f, 0), new Vector3(0.95f, 0.02f, 0.06f), cobalt, Quaternion.Euler(0, 35, 0));
            return root;
        }
        static void Part(PrimitiveKind k, Transform parent, string name, Vector3 pos, Vector3 scale, Material m, Quaternion rot)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(k);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // The Ultimate's standard hazard shapes (ULTIMATE.md §2 and §4), as fresh Hazards a mechanic spawns with its own
    // Spawn() (or SpawnShared), so several can be live at once without borrowing the v2 abilities. Resolve effects included.
    public static class UltShapes
    {
        public const int LeanDamage = 50, StandardDamage = 40, HeavyDamage = 60, TollDamage = 12;
        public const int LeanTicks = 210, TideTicks = 150, HeartTicks = 120;   // 3.5 s, 2.5 s, 2.0 s
        public const string LeanSource = "Leaning Bell", TideSource = "Toll Tide", HeartSource = "Heart Toll";

        // Leaning Bell: half the arena beyond a line through the centre, toward dir (a cone from the centre, 90° half-angle).
        public static Hazard LeaningHalf(CombatWorld w, Vector3 dir, int resolveAt, int damage = LeanDamage, string source = LeanSource)
        {
            dir = Sim.Flat(dir); if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            var h = new Hazard
            {
                Source = source, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = Quaternion.LookRotation(dir).eulerAngles.y,
                Radius = LeaningBellAbility.Reach, HalfAngle = 90, LockAt = 0, ResolveAt = resolveAt, Damage = damage, Knockback = 1.5f, Stun = 15,
            };
            h.OnResolve = hz => w.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle);
            return h;
        }
        // Toll Tide: a ring from 7 m out past the wall, pushing inward.
        public static Hazard Tide(CombatWorld w, int resolveAt, int damage = StandardDamage, string source = TideSource)
        {
            var h = new Hazard
            {
                Source = source, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Vector3.zero, Inner = TollTideAbility.TideInner,
                Radius = TollTideAbility.TideOuter, LockAt = 0, ResolveAt = resolveAt, Damage = damage, Knockback = -TollTideAbility.Push,
            };
            h.OnResolve = hz => w.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius);
            return h;
        }
        // Heart Toll: a 4 m circle on the boss.
        public static Hazard HeartToll(CombatWorld w, Vector3 at, int resolveAt, int damage = StandardDamage, string source = HeartSource)
        {
            var h = new Hazard
            {
                Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(at), Radius = TollTideAbility.TollRadius,
                LockAt = 0, ResolveAt = resolveAt, Damage = damage, Knockback = TollTideAbility.Push,
            };
            h.OnResolve = hz => { w.BossFx.ChimeResolve(hz.Origin, hz.Radius); w.BossFx.TollResolve(hz.Origin, 0.6f, hz.Radius); };
            return h;
        }
        // An unavoidable raidwide (Tolling Hour, the Thirteenth Hour): hits every standing player, no knockback.
        public static Hazard Raidwide(string source, int resolveAt, int damage) => new Hazard
        {
            Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Vector3.zero, Radius = 99,
            LockAt = 0, ResolveAt = resolveAt, Damage = damage, Knockback = 0, Stun = 0,
            Hidden = true,   // no arena-wide coral flash: the great bell's own effects show it
        };
        // ticks for ResolveAt so a hazard spawned now resolves `ticksFromNow` ticks later (a hazard resolves on its spawn tick
        // + ResolveAt − 1, because it ages on its spawn tick)
        public static int ResolveIn(int ticksFromNow) => Mathf.Max(1, ticksFromNow + 1);
    }
}
