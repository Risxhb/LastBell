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
        public const float PlayerBound = 11.5f;   // the dais floor the player can stand on (key-art arena)
        public const float BossBound = 9.5f;      // boss positions and charge endpoints

        public readonly List<PlayerController> Players = new List<PlayerController>();
        public BossEncounter Boss;                // null between encounters; Solid is false once defeated
        public readonly HazardSystem Hazards;
        public readonly ProjectileSystem Projectiles;
        public readonly BellkeeperFx BossFx;
        public readonly PlayerFx Fx;
        public readonly Transform Root;
        public int Tick { get; private set; }
        public bool CombatActive;                  // abilities, boss scripts and hazards only run while true

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
            var b = Boss; if (b == null || !b.Solid) return 0;
            forward = Sim.Flat(forward);
            // the part of the body the swing points at (the centre for a circular body)
            var core = b.Core(origin + forward.normalized * Mathf.Max(0.5f, reach * 0.5f));
            var to = Sim.Flat(core - origin);
            if (to.magnitude > reach + b.BodyRadius) return 0;
            if (arcDeg < 360 && to.magnitude > b.BodyRadius && Vector3.Angle(forward, to) > arcDeg / 2 + Mathf.Rad2Deg * Mathf.Atan2(b.BodyRadius, to.magnitude)) return 0;
            var contact = core - to.normalized * b.BodyRadius + Vector3.up * b.HitHeight;
            return DamageBoss(damage, contact, ability);
        }

        // A straight line (beam / hitscan) of the given width.
        public int LineBoss(Vector3 origin, Vector3 forward, float length, float width, int damage, string ability, out Vector3 contact)
        {
            contact = origin + Sim.Flat(forward).normalized * length;
            var b = Boss; if (b == null || !b.Solid) return 0;
            var f = Sim.Flat(forward).normalized;
            // nearest core point to the line (two refinements settle it for a segment body)
            var core = b.transform.position;
            for (int i = 0; i < 2; i++) core = b.Core(origin + f * Mathf.Max(0, Vector3.Dot(Sim.Flat(core - origin), f)));
            var to = Sim.Flat(core - origin);
            float along = Vector3.Dot(to, f); float side = (to - f * along).magnitude;
            if (along < -b.BodyRadius || along > length + b.BodyRadius || side > width / 2 + b.BodyRadius) return 0;
            contact = origin + f * Mathf.Max(0, along - b.BodyRadius) + Vector3.up * b.HitHeight;
            return DamageBoss(damage, contact, ability);
        }

        public int DamageBoss(int damage, Vector3 contact, string ability)
        {
            var b = Boss; if (b == null || !b.Solid || damage <= 0) return 0;
            int dealt = b.TakeHit(damage, contact);
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
            var flat = new Vector2(pos.x, pos.z);
            if (flat.magnitude > PlayerBound) { flat = flat.normalized * PlayerBound; pos.x = flat.x; pos.z = flat.y; }
            var b = Boss;
            if (b != null && b.Solid)
            {
                var core = b.Core(pos);
                var d = Sim.Flat(pos - core); float min = b.BodyRadius + bodyRadius;
                if (d.magnitude < min)
                {
                    var n = d.sqrMagnitude > 1e-4f ? d.normalized : -b.transform.forward;
                    var p = core + n * min; pos.x = p.x; pos.z = p.z;
                }
            }
            return pos;
        }
    }

    // Player projectiles (Chimesinger's Resonant Note). Moved on the combat tick; a hit resolves exactly once.
    public sealed class ProjectileSystem
    {
        public sealed class Projectile
        {
            public Vector3 Pos, Vel; public float Range, Travelled, Radius; public int Damage; public string Ability;
            public Action<int, Vector3> OnHit; public Color Color;
        }
        readonly CombatWorld w; readonly List<Projectile> live = new List<Projectile>();
        public int Count => live.Count;
        public ProjectileSystem(CombatWorld world) { w = world; }

        public void Fire(Projectile p) { live.Add(p); }

        public void Step()
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i]; var step = p.Vel * Sim.Dt;
                p.Pos += step; p.Travelled += step.magnitude;
                w.Fx.ProjectileHead(p.Pos, p.Color);
                var b = w.Boss;
                if (b != null && b.Solid && Sim.Flat(p.Pos - b.Core(p.Pos)).magnitude <= b.BodyRadius + p.Radius)
                {
                    var core = b.Core(p.Pos);
                    var contact = core + Sim.Flat(p.Pos - core).normalized * b.BodyRadius + Vector3.up * b.HitHeight;
                    int dealt = w.DamageBoss(p.Damage, contact, p.Ability);
                    w.Fx.NoteImpact(contact, p.Color);
                    p.OnHit?.Invoke(dealt, contact);
                    live.RemoveAt(i); continue;
                }
                if (p.Travelled >= p.Range || new Vector2(p.Pos.x, p.Pos.z).magnitude > CombatWorld.PlayerBound + 2) live.RemoveAt(i);
            }
        }
        public void ClearAll() { live.Clear(); }
    }
}
