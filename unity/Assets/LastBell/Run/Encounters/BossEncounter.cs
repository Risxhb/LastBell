using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // One boss fight in the shared arena. The script is a tick coroutine: each `yield return n` waits n combat ticks.
    // Attacks are Hazards (geometry = telegraph = hit test, resolved by the HazardSystem); the script only schedules them.
    // On defeat the script is dropped and every pending hazard is cancelled in the same tick, so nothing resolves after
    // the boss dies, and the body stops colliding.
    public abstract class BossEncounter : MonoBehaviour
    {
        public abstract string Id { get; }            // stable runtime ID (encounter slot data)
        public abstract string DisplayName { get; }
        public abstract string Lesson { get; }        // one line for the intro card
        public virtual bool Placeholder => true;      // stand-in geometry, not production art
        public int MaxHp { get; protected set; } = 1000;
        public int Hp { get; protected set; }
        public float BodyRadius { get; protected set; } = 1.3f;
        public float HitHeight { get; protected set; } = 1.4f;
        public float MarkHeight { get; protected set; } = 3.4f;
        public bool Dead { get; private set; }
        public bool Solid => !Dead && W != null;
        public string PhaseName { get; protected set; } = "";
        public string Current { get; protected set; } = "";   // attack in progress, for the HUD / tests
        public event Action<BossEncounter> Defeated;
        public event Action<BossEncounter, string> PhaseChanged;

        protected CombatWorld W;
        IEnumerator<int> script; int wait;
        protected int Age;
        protected float glowTarget = 1, glowLevel = 1;

        public static T Spawn<T>(CombatWorld w, Vector3 pos, float yaw) where T : BossEncounter
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(w.Root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var b = go.AddComponent<T>(); b.W = w; b.Hp = b.MaxHp; b.Build();
            return b;
        }

        protected abstract void Build();                     // visuals (placeholder geometry or model)
        protected abstract IEnumerable<int> Script();         // the attack schedule; restarts when it ends
        protected virtual void StepAlways() { }               // presentation-adjacent state on every tick
        protected virtual void OnDefeat() { }
        protected virtual void OnHurt() { }

        public void BeginCombat() { script = Script().GetEnumerator(); wait = 0; }
        public void Halt() { script = null; Current = "idle"; }   // player defeat: stop attacking, keep standing

        public void Step()
        {
            if (Dead) return;
            Age++;
            StepAlways();
            if (script == null) return;
            if (wait > 0) { wait--; return; }
            // exactly one script step per tick: `yield return n` resumes n ticks later
            if (!script.MoveNext())
            {
                script = Script().GetEnumerator();
                if (!script.MoveNext()) { script = null; return; }
            }
            wait = Mathf.Max(1, script.Current) - 1;
        }

        public int TakeHit(int damage, Vector3 contact)
        {
            if (Dead) return 0;
            int dealt = Mathf.Min(damage, Hp);
            Hp -= dealt;
            RunHud.Float(contact + Vector3.up * 0.5f, dealt.ToString(), new Color(1f, 0.86f, 0.45f));
            W.BossFx.BossHit(contact); glowLevel = Mathf.Max(glowLevel, 2f);
            OnHurt();
            if (Hp <= 0) Die();
            return dealt;
        }

        // dev / test hook: defeat now (goes through the same path as a killing blow)
        public void DebugDefeat() { if (!Dead) { Hp = 1; TakeHit(1, transform.position + Vector3.up * HitHeight); } }
        public virtual float NextPhaseFraction() => Hp > MaxHp * 0.7f ? 0.69f : 0.34f;   // F8: just below the next threshold
        public void DebugSetHpFraction(float f){ if (!Dead) Hp = Mathf.Clamp(Mathf.RoundToInt(MaxHp * f), 1, MaxHp); }

        void Die()
        {
            Dead = true; script = null; Current = "defeated";
            W.ClearTransient();          // pending hazards / projectiles / marks: cancelled in the killing tick
            OnDefeat();
            Defeated?.Invoke(this);
        }

        protected void SetPhase(string name) { PhaseName = name; PhaseChanged?.Invoke(this, name); }

        // ---------------- helpers for scripts ----------------
        protected Vector3 Target()
        {
            var p = W.NearestStanding(transform.position);
            return p ? p.transform.position : Vector3.zero;
        }
        protected float YawToTarget() => Sim.YawTo(transform.position, Target(), transform.eulerAngles.y);
        protected void TurnTowards(float yaw, float degPerSec) =>
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0, yaw, 0), degPerSec * Sim.Dt);
        protected float DistToTarget() => Sim.Flat(Target() - transform.position).magnitude;

        // walk toward the target until within `range` (or give up after maxTicks)
        protected IEnumerable<int> Approach(float range, float speed, int maxTicks = 150)
        {
            for (int i = 0; i < maxTicks && DistToTarget() > range; i++)
            {
                Current = "approach";
                TurnTowards(YawToTarget(), 200);
                var p = transform.position + transform.forward * speed * Sim.Dt;
                if (new Vector2(p.x, p.z).magnitude <= CombatWorld.BossBound) transform.position = p;
                yield return 1;
            }
        }
        protected IEnumerable<int> WalkTo(Vector3 dest, float speed, int maxTicks = 180)
        {
            for (int i = 0; i < maxTicks && Sim.Flat(dest - transform.position).magnitude > 0.3f; i++)
            {
                Current = "reposition";
                TurnTowards(Sim.YawTo(transform.position, dest, transform.eulerAngles.y), 200);
                transform.position = Vector3.MoveTowards(transform.position, new Vector3(dest.x, 0, dest.z), speed * Sim.Dt);
                yield return 1;
            }
        }

        protected Hazard Cone(string src, float radius, float half, int lockAt, int resolveAt, int dmg, bool trackBoss = true, float turnRate = 240)
        {
            var h = new Hazard
            {
                Source = src, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Radius = radius, HalfAngle = half,
                LockAt = lockAt, ResolveAt = resolveAt, Damage = dmg, Origin = transform.position, Yaw = transform.eulerAngles.y,
            };
            if (trackBoss) h.Track = hz => { if (Dead) return; TurnTowards(YawToTarget(), turnRate); hz.Origin = transform.position; hz.Yaw = transform.eulerAngles.y; };
            return W.Hazards.Spawn(h);
        }
        protected Hazard Ring(string src, Vector3 at, float inner, float outer, int resolveAt, int dmg) =>
            W.Hazards.Spawn(new Hazard { Source = src, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = at, Inner = inner, Radius = outer, LockAt = 0, ResolveAt = resolveAt, Damage = dmg });
        protected Hazard Lane(string src, Vector3 from, float yaw, float length, float width, int resolveAt, int dmg, bool jumpClear = false) =>
            W.Hazards.Spawn(new Hazard { Source = src, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = from, Yaw = yaw, Length = length, Width = width, LockAt = 0, ResolveAt = resolveAt, Damage = dmg, JumpClearable = jumpClear });
        protected Hazard Marker(string src, float radius, int follow, int resolveAt, int dmg, float speed = 6f)
        {
            var h = new Hazard { Source = src, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = radius, LockAt = follow, ResolveAt = resolveAt, Damage = dmg, Origin = Target() };
            h.Track = hz => { var t = Target(); hz.Origin = Vector3.MoveTowards(hz.Origin, new Vector3(t.x, 0, t.z), speed * Sim.Dt); };
            return W.Hazards.Spawn(h);
        }

        // charge lane: from the boss to the target + overshoot, clamped so the boss lands inside its bound
        protected (Vector3 start, Vector3 end, float yaw, float length) ChargeLane(float overshoot)
        {
            var start = transform.position; var target = Target();
            var dir = Sim.Flat(target - start); if (dir.sqrMagnitude < 1) dir = transform.forward; dir.Normalize();
            var end = ClampToBound(start, target + dir * overshoot);
            return (start, end, Sim.YawTo(start, end, transform.eulerAngles.y), Vector3.Distance(start, end) + 1f);
        }
        protected static Vector3 ClampToBound(Vector3 from, Vector3 to)
        {
            to.y = 0;
            if (new Vector2(to.x, to.z).magnitude <= CombatWorld.BossBound) return to;
            var d = to - from; float lo = 0, hi = 1;
            for (int i = 0; i < 20; i++) { float m = (lo + hi) / 2; var p = from + d * m; if (new Vector2(p.x, p.z).magnitude <= CombatWorld.BossBound) lo = m; else hi = m; }
            return from + d * lo;
        }
        // distance from a point along dir to the floor edge
        protected static float ToEdge(Vector3 from, Vector3 dir, float radius)
        {
            var p = new Vector2(from.x, from.z); var d = new Vector2(dir.x, dir.z).normalized;
            float b = Vector2.Dot(p, d), c = p.sqrMagnitude - radius * radius;
            return Mathf.Max(0, -b + Mathf.Sqrt(Mathf.Max(0, b * b - c)));
        }

        // core glow follows the windups (presentation)
        protected void Windup(float k) { glowTarget = 1 + 1.6f * Mathf.Clamp01(k); }
        protected void Release(float flare = 1) { glowLevel = 1 + 3f * flare; glowTarget = 1; }
        protected virtual void Update()
        {
            glowLevel = Mathf.MoveTowards(glowLevel, glowTarget, Time.deltaTime * (glowLevel > glowTarget ? 6f : 3f));
        }
    }
}
