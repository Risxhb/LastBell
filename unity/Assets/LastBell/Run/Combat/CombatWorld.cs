using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // 60 Hz combat clock shared by the run. Gameplay (movement, cooldowns, hits, hazards, encounter scripts) advances
    // only in whole ticks; animation and effects read the resulting state and never decide outcomes.
    public static class Sim
    {
        public const int Hz = 60;
        public const float Dt = 1f / Hz;
        public static int Ticks(float seconds) => Mathf.RoundToInt(seconds * Hz);
        public static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        public static float YawTo(Vector3 from, Vector3 to, float fallback)
        {
            var d = Flat(to - from); return d.sqrMagnitude < 1e-4f ? fallback : Quaternion.LookRotation(d).eulerAngles.y;
        }
        public static Vector3 Dir(float yaw) => Quaternion.Euler(0, yaw, 0) * Vector3.forward;
    }

    // Everything one encounter's combat touches: the players, the current boss, hazards, projectiles and effects.
    // Owned by the RunDirector and rebuilt from scratch for every fresh run, so nothing survives a retry.
    public sealed class CombatWorld
    {
        // The floor the player can stand on: set per map from RunMaps (Map.Bound / Map.Square) when its director wakes.
        // 11.5 m circle is the fallback for a scene that isn't a run map.
        public static float PlayerBound { get; private set; } = 11.5f;
        public static bool SquareBound { get; private set; }
        public static void SetPlayerBound(float bound, bool square) { PlayerBound = bound; SquareBound = square; }
        public static bool InPlayerBound(Vector3 pos, float margin = 0)
        {
            float b = PlayerBound + margin;
            return SquareBound ? Mathf.Abs(pos.x) <= b && Mathf.Abs(pos.z) <= b : new Vector2(pos.x, pos.z).magnitude <= b;
        }
        public const float BossBound = 9.5f;      // boss positions and charge endpoints

        public readonly List<PlayerController> Players = new List<PlayerController>();
        public BossEncounter Boss;                // null between encounters; Solid is false once defeated
        // Extra bodies an encounter can add (Crucivar's Molten Castings): player strikes, lines and projectiles hit the
        // nearest of the boss and its live parts. Cleared with the encounter's transient state.
        public readonly List<BossPart> Parts = new List<BossPart>();
        public readonly HazardSystem Hazards;
        public readonly ProjectileSystem Projectiles;
        public readonly BellkeeperFx BossFx;
        public readonly PlayerFx Fx;
        public readonly Transform Root;
        public int Tick { get; private set; }
        public bool CombatActive;                  // abilities, boss scripts and hazards only run while true
        public Difficulty Difficulty = Difficulty.Medium;   // fixed for the run (RunDirector); bosses may author per level
        public float DamageTakenScale => RunDifficulty.DamageTakenScale(Difficulty);

        // statistics for the results screen
        public int DamageDealt, DamageTaken;
        public string LastDamageSource;
        // PF-06 death recap and PF-07 meter (first pass): every resolved hit, once, from the authoritative tick
        public readonly List<(int tick, string source, int damage, int hpAfter)> TakenLog = new List<(int, string, int, int)>();
        public sealed class MeterRow { public string Ability; public int Damage, Hits, Largest; }
        public readonly Dictionary<string, MeterRow> RunMeter = new Dictionary<string, MeterRow>(), EncounterMeter = new Dictionary<string, MeterRow>();
        public int SegmentStartTick { get; private set; }
        public void BeginSegment() { EncounterMeter.Clear(); SegmentStartTick = Tick; }
        static void Add(Dictionary<string, MeterRow> m, string ability, int dmg)
        {
            if (!m.TryGetValue(ability, out var r)) m[ability] = r = new MeterRow { Ability = ability };
            r.Damage += dmg; r.Hits++; r.Largest = Mathf.Max(r.Largest, dmg);
        }

        public event Action<PlayerController, Hazard, int> PlayerHit;   // tests and HUD listen

        // The effect layers are created once by the director and reset between runs (re-creating them crashed the
        // player natively in Mesh.set_colors on retry, see handoff).
        public CombatWorld(Transform root, BellkeeperFx bossFx, PlayerFx fx)
        {
            Root = root;
            BossFx = bossFx; Fx = fx;
            BossFx.ResetAll(); Fx.ResetAll();
            Hazards = new HazardSystem(this);
            Projectiles = new ProjectileSystem(this);
        }

        public void Step()
        {
            Tick++;
            foreach (var p in Players) p.Step();
            if (!CombatActive) return;
            if (Boss != null && !Boss.Dead) Boss.Step();
            Projectiles.Step();
            Hazards.Step();
        }

        // Clears everything an encounter can leave behind: hazards, projectiles, boss-owned effects. Called at every
        // transition (defeat, victory, clear, retry), so nothing from one state leaks into the next.
        public void ClearTransient()
        {
            Hazards.ClearAll();
            Projectiles.ClearAll();
            BossFx.ClearTelegraphs();
            BossFx.ChargeTrail(Root, false);
            foreach (var p in Players) p.Kit?.ClearTargetState();
            foreach (var part in Parts) part.OnCleared?.Invoke(part);
            Parts.Clear();
        }

        public PlayerController NearestStanding(Vector3 from)
        {
            PlayerController best = null; float bd = float.MaxValue;
            foreach (var p in Players)
            {
                if (p.Down) continue;
                float d = Sim.Flat(p.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }
        public bool AnyStanding { get { foreach (var p in Players) if (!p.Down) return true; return false; } }

        // A melee strike from a player: hits the boss if its body is within reach and inside the arc. Resolved on the
        // kit's contact tick; returns the damage dealt (0 = miss).
        public int StrikeBoss(PlayerController from, Vector3 origin, Vector3 forward, float reach, float arcDeg, int damage, string ability)
        {
            forward = Sim.Flat(forward);
            bool In(Vector3 pos, float r, out float dist)
            {
                var to = Sim.Flat(pos - origin); dist = to.magnitude;
                if (dist > reach + r) return false;
                return arcDeg >= 360 || dist <= r || Vector3.Angle(forward, to) <= arcDeg / 2 + Mathf.Rad2Deg * Mathf.Atan2(r, dist);
            }
            var b = Boss; float best = float.MaxValue; BossPart hit = null;
            bool boss = b != null && b.Solid && In(b.transform.position, b.BodyRadius, out best);
            if (!boss) best = float.MaxValue;
            foreach (var part in Parts) if (part.Alive && In(part.Pos, part.BodyRadius, out float d) && d < best) { best = d; hit = part; }
            if (hit != null) return DamagePart(hit, damage, hit.Pos - Sim.Flat(hit.Pos - origin).normalized * hit.BodyRadius + Vector3.up * hit.HitHeight, ability);
            if (!boss) return 0;
            var contact = b.transform.position - Sim.Flat(b.transform.position - origin).normalized * b.BodyRadius + Vector3.up * b.HitHeight;
            return DamageBoss(damage, contact, ability);
        }

        // A straight line (beam / hitscan) of the given width.
        public int LineBoss(Vector3 origin, Vector3 forward, float length, float width, int damage, string ability, out Vector3 contact, bool hitFx = true)
        {
            contact = origin + Sim.Flat(forward).normalized * length;
            var f = Sim.Flat(forward).normalized;
            bool On(Vector3 pos, float r, out float along)
            {
                var to = Sim.Flat(pos - origin); along = Vector3.Dot(to, f); float side = (to - f * along).magnitude;
                return along >= -r && along <= length + r && side <= width / 2 + r;
            }
            var b = Boss; float best = float.MaxValue; BossPart hit = null;
            bool boss = b != null && b.Solid && On(b.transform.position, b.BodyRadius, out best);
            if (!boss) best = float.MaxValue;
            foreach (var part in Parts) if (part.Alive && On(part.Pos, part.BodyRadius, out float a) && a < best) { best = a; hit = part; }
            if (hit != null) { contact = origin + f * Mathf.Max(0, best - hit.BodyRadius) + Vector3.up * hit.HitHeight; return DamagePart(hit, damage, contact, ability); }
            if (!boss) return 0;
            contact = origin + f * Mathf.Max(0, best - b.BodyRadius) + Vector3.up * b.HitHeight;
            return DamageBoss(damage, contact, ability, hitFx);
        }

        public int DamageBoss(int damage, Vector3 contact, string ability, bool hitFx = true)
        {
            var b = Boss; if (b == null || !b.Solid || damage <= 0) return 0;
            int dealt = b.TakeHit(damage, contact, hitFx);
            DamageDealt += dealt;
            if (dealt > 0) { Add(RunMeter, ability, dealt); Add(EncounterMeter, ability, dealt); }
            return dealt;
        }

        public int DamagePart(BossPart part, int damage, Vector3 contact, string ability)
        {
            if (part == null || !part.Alive || damage <= 0 || part.TakeHit == null) return 0;
            int dealt = part.TakeHit(damage, contact);
            DamageDealt += dealt;
            if (dealt > 0) { Add(RunMeter, ability, dealt); Add(EncounterMeter, ability, dealt); }
            return dealt;
        }

        internal void ReportPlayerHit(PlayerController p, Hazard h, int dmg)
        {
            DamageTaken += dmg; LastDamageSource = h.Source;
            TakenLog.Add((Tick, h.Source, dmg, p.Hp));
            PlayerHit?.Invoke(p, h, dmg);
        }

        // Push a planar position back inside the floor and out of the boss body.
        public Vector3 Constrain(Vector3 pos, float bodyRadius)
        {
            pos.y = 0;
            if (SquareBound) { pos.x = Mathf.Clamp(pos.x, -PlayerBound, PlayerBound); pos.z = Mathf.Clamp(pos.z, -PlayerBound, PlayerBound); }
            else
            {
                var flat = new Vector2(pos.x, pos.z);
                if (flat.magnitude > PlayerBound) { flat = flat.normalized * PlayerBound; pos.x = flat.x; pos.z = flat.y; }
            }
            var b = Boss;
            if (b != null && b.Solid)
            {
                var d = Sim.Flat(pos - b.transform.position); float min = b.BodyRadius + bodyRadius;
                if (d.magnitude < min)
                {
                    var n = d.sqrMagnitude > 1e-4f ? d.normalized : -b.transform.forward;
                    var p = b.transform.position + n * min; pos.x = p.x; pos.z = p.z;
                }
            }
            return pos;
        }
    }

    // A damageable body that isn't the boss (CombatWorld.Parts). The encounter owns its position, look and what a hit does.
    public sealed class BossPart
    {
        public string Name = "part";
        public Vector3 Pos; public float BodyRadius = 1f, HitHeight = 1f;
        public bool Alive = true;
        public Func<int, Vector3, int> TakeHit;   // returns the damage dealt
        public Action<BossPart> OnCleared;        // encounter end / transition: remove its look
    }

    // Player projectiles (Chimesinger's Resonant Note). Moved on the combat tick; a hit resolves exactly once.
    public sealed class ProjectileSystem
    {
        public sealed class Projectile
        {
            public Vector3 Pos, Vel; public float Range, Travelled, Radius; public int Damage; public string Ability;
            public Action<int, Vector3> OnHit; public Color Color;
            // optional: turn toward the boss by up to Homing degrees per tick, after HomingDelay ticks of straight flight
            public float Homing; public int HomingDelay, Age;
            // optional custom presentation (Arcanist): OnMove replaces the shared head each tick, OnEnd(hit, at) replaces
            // the shared impact and also runs when the projectile expires or is cleared (hit false)
            public Action<Projectile> OnMove; public Action<Projectile, bool, Vector3> OnEnd;
        }
        readonly CombatWorld w; readonly List<Projectile> live = new List<Projectile>(), batch = new List<Projectile>();
        int clears;   // bumped by ClearAll, so a step can tell its batch was cancelled mid-dispatch
        public int Count => live.Count;
        public ProjectileSystem(CombatWorld world) { w = world; }

        public void Fire(Projectile p) { live.Add(p); }

        // Steps a snapshot: a killing blow runs BossEncounter.Die -> ClearTransient -> ClearAll, which empties live (and
        // cancels pending hazards on the same tick) mid-loop. A projectile leaves live before its hit is dispatched, and
        // once the list has been cleared the rest of the batch goes with it.
        public void Step()
        {
            batch.Clear(); batch.AddRange(live);
            int gen = clears;
            for (int i = batch.Count - 1; i >= 0 && clears == gen; i--)
            {
                var p = batch[i]; var b = w.Boss;
                p.Age++;
                if (p.Homing > 0 && p.Age > p.HomingDelay && b != null && b.Solid)
                {
                    var want = b.transform.position + Vector3.up * b.HitHeight - p.Pos;
                    if (want.sqrMagnitude > 1e-4f) p.Vel = Vector3.RotateTowards(p.Vel, want.normalized * p.Vel.magnitude, p.Homing * Mathf.Deg2Rad, 0);
                }
                var step = p.Vel * Sim.Dt;
                p.Pos += step; p.Travelled += step.magnitude;
                if (p.OnMove != null) p.OnMove(p); else w.Fx.ProjectileHead(p.Pos, p.Color);
                BossPart part = null;
                foreach (var q in w.Parts) if (q.Alive && Sim.Flat(p.Pos - q.Pos).magnitude <= q.BodyRadius + p.Radius) { part = q; break; }
                if (part != null)
                {
                    var contact = part.Pos + Sim.Flat(p.Pos - part.Pos).normalized * part.BodyRadius + Vector3.up * part.HitHeight;
                    live.Remove(p);
                    int dealt = w.DamagePart(part, p.Damage, contact, p.Ability);
                    if (p.OnEnd != null) p.OnEnd(p, true, contact); else w.Fx.NoteImpact(contact, p.Color);
                    p.OnHit?.Invoke(dealt, contact);
                    continue;
                }
                if (b != null && b.Solid && Sim.Flat(p.Pos - b.transform.position).magnitude <= b.BodyRadius + p.Radius)
                {
                    var contact = b.transform.position + Sim.Flat(p.Pos - b.transform.position).normalized * b.BodyRadius + Vector3.up * b.HitHeight;
                    live.Remove(p);
                    int dealt = w.DamageBoss(p.Damage, contact, p.Ability, p.OnEnd == null);   // custom projectiles draw their own impact
                    if (p.OnEnd != null) p.OnEnd(p, true, contact); else w.Fx.NoteImpact(contact, p.Color);
                    p.OnHit?.Invoke(dealt, contact);
                    continue;
                }
                if (p.Travelled >= p.Range || !CombatWorld.InPlayerBound(p.Pos, 2)) { live.Remove(p); p.OnEnd?.Invoke(p, false, p.Pos); }
            }
        }
        public void ClearAll()
        {
            var ending = live.ToArray(); live.Clear(); clears++;
            foreach (var p in ending) p.OnEnd?.Invoke(p, false, p.Pos);
        }
    }
}
