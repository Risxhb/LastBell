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
        public virtual string HudLine => null;         // extra boss-bar line (e.g. a push timer); null = none
        // Extra hit bodies (the Bellkeeper Ultimate's P4 gear cores): solid like the body, and a player hit on one counts as
        // a hit on the boss (the same bar). Body 0 is always the boss itself; other bosses offer none.
        public virtual int ExtraBodies => 0;
        public virtual void ExtraBody(int i, out Vector3 center, out float radius) { center = transform.position; radius = 0; }
        public int BodyCount => 1 + ExtraBodies;
        public void Body(int i, out Vector3 center, out float radius)
        {
            if (i == 0) { center = transform.position; radius = BodyRadius; }
            else ExtraBody(i - 1, out center, out radius);
        }
        public virtual float[] HudMarks => null;       // phase thresholds drawn on the boss bar (fractions); null = the HUD's defaults
        public virtual string HudExtra => null;          // an extra boss-bar line (Aurelion v4: enrage clock, shield)
        // LAN client replica (never stepped): the host's values for the HUD. netExtra null = this boss's own HudExtra.
        string netExtra; bool netState;
        public string HudExtraShown => netState ? (string.IsNullOrEmpty(netExtra) ? null : netExtra) : HudExtra;
        public void ApplyNetworkState(int hp, int maxHp, bool dead, string phase, string attack, string extra)
        {
            MaxHp = Mathf.Max(1, maxHp); Hp = Mathf.Clamp(hp, 0, MaxHp); Dead = dead;
            PhaseName = phase ?? ""; Current = attack ?? ""; netExtra = extra; netState = true;
        }
        protected virtual bool PartyScaled => false;      // true: co-op HP 1 + PartyStep x (fighters - 1) (Aurelion v4 is tuned with it)
        protected virtual float PartyStep => 0.7f;        // the packets' provisional 0.7 per extra fighter
        public event Action<BossEncounter> Defeated;
        public event Action<BossEncounter, string> PhaseChanged;
        // Tutorial hooks (Run/Tutorial): BeforeStep runs at the start of every combat tick, before the script; HpFloor > 0
        // keeps HP from dropping below it (the tutorial boss can't die until told to). DebugDefeat ignores the floor.
        public event Action<BossEncounter> BeforeStep;
        public int HpFloor;

        protected CombatWorld W;
        IEnumerator<int> script; int wait;
        protected int Age;
        protected float glowTarget = 1, glowLevel = 1;

        public static T Spawn<T>(CombatWorld w, Vector3 pos, float yaw) where T : BossEncounter
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(w.Root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var b = go.AddComponent<T>(); b.W = w;
            // co-op HP: 1 + 0.7 x (fighting players - 1), the packets' provisional formula until the Bellkeeper D6 decides.
            // Opt-in per boss (PartyScaled): only bosses tuned with it use it, so other bosses keep their authored HP.
            int fighters = 0; foreach (var p in w.Players) if (!(p.Input is AllyBot) || AllyBot.Attacks) fighters++;   // passive bots don't count
            float party = b.PartyScaled ? 1 + b.PartyStep * Mathf.Max(0, fighters - 1) : 1;
            if (TestHpScale != 1 || party != 1) b.MaxHp = Mathf.Max(1, Mathf.RoundToInt(b.MaxHp * TestHpScale * party));
            b.Hp = b.MaxHp; b.Build();
            SimSmoother.Attach(go);   // drawn between ticks like the player
            return b;
        }

        // Playtest switch: LastBell.exe -lastbellBossHp <fraction> scales every boss's health (e.g. 0.2 = 20 %), so all
        // phases can be reached quickly. Phase thresholds are fractions of MaxHp, so the phase plan is unchanged.
        public static readonly float TestHpScale = ReadHpScale();
        static float ReadHpScale()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals("-lastbellBossHp", StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)
                ? Mathf.Clamp(f, 0.01f, 1f) : 1f;
        }

        protected abstract void Build();                     // visuals (placeholder geometry or model)
        // LAN: moving objects the boss draws itself (orbs, thrown weights). The host lists them every snapshot; a client asks
        // the replica boss for a view of each kind and moves it along the host's positions.
        public virtual void CaptureNetProps(List<Networking.LanWorldFrame.Prop> into) { }
        public virtual Transform MakeNetProp(byte kind) => null;
        // LAN looks (4 Nov 2026): what the boss shows that its own Update/LateUpdate draws (glow, phase looks, poses). The
        // host fills the snapshot's Glow and Look numbers; a client's replica boss (disabled, never stepped) gets the newest
        // state every frame and applies it. Each boss decides what its Look numbers mean.
        public virtual void CaptureNetLook(Networking.LanWorldFrame.BossState into) { }
        public virtual void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt) { }
        // LAN effects a boss replays itself: on the host, NetFx("Boss.Method", args) records a call (arguments: Vector3, float,
        // int, bool, string, Color, PlayerController, this boss); the client's replica gets it back here when its drawn time
        // reaches that tick. Return false for an unknown method. Recording is null outside a host session.
        protected static bool NetRecording => Networking.LanFx.Record != null;
        protected static void NetFx(string method, params object[] args) { var r = Networking.LanFx.Record; if (r != null) r(Networking.LanFxTarget.BossPart, method, args); }
        public virtual bool ReplayNetEffect(string method, Networking.LanWorldFrame.EffectArg[] args, System.Func<int, PlayerController> player) => false;
        protected static Vector3 NetV(Networking.LanWorldFrame.EffectArg a) => new Vector3(a.X, a.Y, a.Z);
        protected static float NetF(Networking.LanWorldFrame.EffectArg a) => a.X;
        protected static int NetI(Networking.LanWorldFrame.EffectArg a) => a.I;
        protected static bool NetB(Networking.LanWorldFrame.EffectArg a) => a.I != 0;
        protected static string NetS(Networking.LanWorldFrame.EffectArg a) => a.S ?? "";
        protected static Color NetC(Networking.LanWorldFrame.EffectArg a) => new Color(a.X, a.Y, a.Z, a.W);
        protected static PlayerController NetP(Networking.LanWorldFrame.EffectArg a, System.Func<int, PlayerController> player) => a.Kind == Networking.LanWorldFrame.EffectArg.PlayerRef ? player(a.I) : null;
        // LAN player identity, the same number on every computer (the lobby id): set by the host's capture and the client's
        // replica world, null outside a session. Use it to send per-player looks (Look numbers keyed by id).
        public static int NetDrawnTick;   // LAN client: the host tick this screen is drawing now
        public static System.Func<PlayerController, int> NetIdOf;
        public static System.Func<int, PlayerController> NetPlayerOf;
        // on a LAN client the boss is a replica: drawn from the host's snapshots, never stepped
        public bool IsNetReplica => !enabled && RunHud.LanClientView;
        protected static Networking.LanWorldFrame.Prop NetProp(int id, byte kind, Vector3 p) => new Networking.LanWorldFrame.Prop { Id = id, Kind = kind, X = p.x, Y = p.y, Z = p.z };
        protected static int NextNetPropId = -1;   // for props without a hazard of their own (negative: never a hazard Id)
        protected abstract IEnumerable<int> Script();         // the attack schedule; restarts when it ends
        protected virtual void StepAlways() { }               // presentation-adjacent state on every tick
        protected virtual void OnDefeat() { }
        protected virtual void OnHurt() { }
        protected virtual void OnHalt() { }                   // the script was dropped (Halt): stop anything it started
        protected virtual int Absorb(int damage) => damage;   // returns what gets through to HP

        public virtual void BeginCombat() { script = Script().GetEnumerator(); wait = 0; }
        public void Halt() { script = null; Current = "idle"; OnHalt(); }   // player defeat: stop attacking, keep standing

        // profiler scopes (perf agent, 2 Nov): the per-tick state and the phase script
        static readonly Unity.Profiling.ProfilerMarker PmAlways = new Unity.Profiling.ProfilerMarker("LastBell.Boss.StepAlways"),
            PmScript = new Unity.Profiling.ProfilerMarker("LastBell.Boss.Script");

        public void Step()
        {
            if (Dead) return;
            BeforeStep?.Invoke(this);
            if (Dead) return;
            Age++;
            using (PmAlways.Auto()) StepAlways();
            if (script == null) return;
            if (wait > 0) { wait--; return; }
            using (PmScript.Auto())
            {
                // exactly one script step per tick: `yield return n` resumes n ticks later
                if (!script.MoveNext())
                {
                    script = Script().GetEnumerator();
                    if (!script.MoveNext()) { script = null; return; }
                }
                if (script == null) return;   // the step itself halted or defeated the boss (a wipe, the Ultimate slice ending)
                wait = Mathf.Max(1, script.Current) - 1;
            }
        }

        public int TakeHit(int damage, Vector3 contact, bool hitFx = true)
        {
            if (Dead) return 0;
            damage = Mathf.RoundToInt(damage * DamageTakenScale(contact)); if (damage <= 0) return 0;
            damage = Absorb(damage);   // a shield (Aurelion v4 Corona Shield) takes its share first
            if (damage <= 0) { RunHud.Float(contact + Vector3.up * 0.5f, "absorbed", new Color(0.7f, 0.85f, 1f)); return 0; }
            int floor = ignoreFloor ? 0 : Mathf.Clamp(Mathf.Max(HpFloor, PhaseFloor), 0, Hp);
            int dealt = Mathf.Min(damage, Hp - floor);
            if (dealt <= 0) { RunHud.Float(contact + Vector3.up * 0.5f, "held", new Color(0.7f, 0.85f, 1f)); return 0; }
            Hp -= dealt;
            Presentation.HitFeel.BossDamage(contact + Vector3.up * 0.5f, dealt, W.Actor);   // damage numbers and hit-stop (presentation)
            if (hitFx) W.BossFx.BossHit(contact);   // abilities with their own impact effect skip the shared flash
            glowLevel = Mathf.Max(glowLevel, 2f);
            OnHurt();
            if (Hp <= 0) Die();
            return dealt;
        }

        // dev / test hook: defeat now (goes through the same path as a killing blow)
        // A boss whose OnHurt holds HP (the Chimebound Oracle at 50% until his Grand Toll ends) still falls: the hold would
        // otherwise refill him and the run tests, which now meet minibosses in runs, could never clear him.
        public void DebugDefeat() { if (!Dead) { HpFloor = 0; Hp = 1; ignoreFloor = true; TakeHit(1, transform.position + Vector3.up * HitHeight); ignoreFloor = false; if (!Dead) { Hp = 0; Die(); } } }
        // Damage can't take Hp below this until the boss lowers it (the Bellkeeper Ultimate holds each phase's threshold
        // until its transition, so a phase can't be skipped). 0 = no floor. Works with HpFloor (the tutorial's); the higher wins.
        // DebugDefeat ignores both.
        protected virtual int PhaseFloor => 0;
        // multiplies every hit this boss takes, by where it lands (the Bellkeeper Ultimate's P5: the bell resonates, only the
        // Bellkeeper that rang last takes damage, x4 in the last stand)
        protected virtual float DamageTakenScale(Vector3 contact) => 1f;
        // the run level's multiplier on this boss's damage to players; a boss may author its own table (the Bellkeeper Ultimate)
        public virtual float PlayerDamageScale(Difficulty d) => RunDifficulty.DamageTakenScale(d);
        bool ignoreFloor;
        public virtual float NextPhaseFraction() => Hp > MaxHp * 0.7f ? 0.69f : 0.34f;   // F8: just below the next threshold
        public void DebugSetHpFraction(float f){ if (!Dead) Hp = Mathf.Clamp(Mathf.RoundToInt(MaxHp * f), 1, MaxHp); }

        // ---------------- practice: start at phase N (Test Rush phase row, -lastbellPhase) ----------------
        // PhaseStarts: the HP fraction each HP phase begins at, phase 1 first (always 1). One entry = Phase 1 only: the
        // boss can't be skipped into a later phase safely yet. PhaseNumber is the 1-based HP phase the fight is in now.
        public virtual float[] PhaseStarts => new[] { 1f };
        public virtual int PhaseNumber => 1;
        public int PhaseCount => PhaseStarts.Length;
        // Puts the boss in phase n as if that phase's transition had just finished: HP at the top of the band (no heal
        // later, same bar), phase state, name, looks and per-phase setup through EnterPhase. Idempotent, so the director
        // applies it when the boss spawns (the bar reads right during the intro) and again right after BeginCombat (a boss
        // that resets its phase state there can't undo it). Phase 1 is a normal fight start.
        public bool StartAtPhase(int n)
        {
            var starts = PhaseStarts;
            if (Dead || n < 1 || n > starts.Length) return false;
            if (n == 1) return true;
            // one HP under the threshold: `Hp > MaxHp * 0.70f` can see 2519.99996 for 3600 HP, so exactly 2520 would read as
            // the phase above and shift straight back
            Hp = Mathf.Clamp(Mathf.FloorToInt(MaxHp * starts[n - 1]) - 1, 1, MaxHp);
            EnterPhase(n);
            return PhaseNumber == n;
        }
        protected virtual void EnterPhase(int n) { }

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
        protected Hazard Lane(string src, Vector3 from, float yaw, float length, float width, int resolveAt, int dmg) =>
            W.Hazards.Spawn(new Hazard { Source = src, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = from, Yaw = yaw, Length = length, Width = width, LockAt = 0, ResolveAt = resolveAt, Damage = dmg });
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
