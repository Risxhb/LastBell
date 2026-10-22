using System;
using System.Collections.Generic;
using System.Reflection;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Tether: a player-to-player (or player-to-post) band check with no floor shape. Includes decides who is "inside"
    // (partner distance outside the band) at resolve; presentation draws the chain.
    public enum HazardShape { Circle, Ring, Cone, Lane, Tether }

    // Strike = a directed physical blow (Vanguard's Brace can mitigate it from the front).
    // Ground = an eruption under the player (Brace never helps). Bellkeeper spec v0.2 §3, Vanguard #31 Brace rule.
    public enum HazardKind { Strike, Ground }

    // One boss attack instance. Its geometry is the telegraph and the hit test (spec: "damage geometry is the
    // telegraph"). Warns from tick 0, may track until LockAt, resolves exactly once at ResolveAt. A player can be hit
    // by an instance at most once (R16), which replaces blanket post-hit immunity.
    public sealed class Hazard
    {
        static int nextId;
        public readonly int Id = ++nextId;
        public string Source = "Hazard";
        public HazardShape Shape;
        public HazardKind Kind = HazardKind.Strike;
        public Vector3 Origin;
        public float Yaw;
        public float Radius, Inner, HalfAngle;   // circle/ring/cone
        public float Length, Width;              // lane
        public int LockAt, ResolveAt;
        public int ActiveTicks;                  // 0 or 1 = resolves on one tick; otherwise hits anyone inside for this many ticks from ResolveAt (R22)
        public int Damage;
        public bool JumpClearable; public float ClearHeight = 0.35f;
        public float Knockback = 1.5f;
        public int Stun = 15;
        // > 0: a standing hazard (Crucivar's cooling slag). It deals this much per second to players inside while it
        // lives, with no knockback, and simply expires at ResolveAt (no resolve hit). Overlapping ones never multiply.
        public float BurnPerSecond;
        // Shared soak / line split (settled rule §5). ShareTotal > 0: the players inside at resolve split ShareTotal evenly
        // (missing players' shares fall on those inside; over-soaking lowers each share). ShareCount is the numeral shown
        // on the telegraph. WipeIfEmpty: nobody inside downs every standing player (an empty soak wipes the party).
        public int ShareTotal, ShareCount;
        public bool WipeIfEmpty;
        // per-player hit test, used instead of Contains when set (Tether needs it)
        public Func<PlayerController, bool> Includes;
        public Action<Hazard> Track;            // called every tick until locked (follow a target / the boss facing)
        public Action<Hazard> OnResolve;         // presentation of the resolve (effects), after the hit test
        public Action<Hazard> OnSpent;           // the active period is over (same tick as OnResolve when ActiveTicks is 0)
        public Func<Vector3, Vector3> Push;      // knockback direction override (Second Press pushes toward the arena centre)

        public int Age { get; internal set; }
        public bool Locked => Age >= LockAt;
        internal BellkeeperFx.Telegraph Tele;
        internal readonly HashSet<PlayerController> Hit = new HashSet<PlayerController>();

        public const float Margin = 0.3f;        // player body radius: touching the border counts

        public bool Hits(PlayerController p) => Includes != null ? Includes(p) : Contains(p.transform.position);

        public bool Contains(Vector3 p)
        {
            if (Shape == HazardShape.Tether) return false;
            var v = Sim.Flat(p - Origin); float d = v.magnitude;
            switch (Shape)
            {
                case HazardShape.Circle: return d <= Radius + Margin;
                case HazardShape.Ring: return d >= Inner - Margin && d <= Radius + Margin;
                case HazardShape.Cone:
                    if (d > Radius + Margin) return false;
                    if (d < 0.6f) return true;
                    return Vector3.Angle(Sim.Dir(Yaw), v) <= HalfAngle + Mathf.Rad2Deg * Mathf.Atan2(Margin, d);
                default:
                {
                    var l = Quaternion.Euler(0, -Yaw, 0) * v;
                    return Mathf.Abs(l.x) <= Width / 2 + Margin && l.z >= -Margin && l.z <= Length + Margin;
                }
            }
        }

        // direction a hit pushes the player: Push when set, else away from the origin, or sideways out of a lane
        public Vector3 PushDir(Vector3 p)
        {
            if (Push != null) { var o = Sim.Flat(Push(p)); if (o.sqrMagnitude > 1e-4f) return o.normalized; }
            if (Shape == HazardShape.Lane)
            {
                var l = Quaternion.Euler(0, -Yaw, 0) * Sim.Flat(p - Origin);
                return Quaternion.Euler(0, Yaw, 0) * new Vector3(l.x >= 0 ? 1 : -1, 0, 0);
            }
            var d = Sim.Flat(p - Origin); return d.sqrMagnitude > 1e-4f ? d.normalized : Sim.Dir(Yaw);
        }

        // where the blow comes from, for Brace's frontal test
        public Vector3 IncomingFrom(Vector3 p) => Shape == HazardShape.Lane ? Origin + Sim.Dir(Yaw) * Mathf.Max(0, Vector3.Dot(Sim.Flat(p - Origin), Sim.Dir(Yaw)) - 1f) : Origin;
    }

    public sealed class HazardSystem
    {
        readonly CombatWorld w;
        readonly List<Hazard> live = new List<Hazard>();
        public int Count => live.Count;
        public IReadOnlyList<Hazard> Live => live;
        public int Resolved { get; private set; }
        public HazardSystem(CombatWorld world) { w = world; }
        public event Action<Hazard> Spawned;   // tests record exact spawn ticks

        static readonly Color JumpFill = new Color(0.30f, 0.85f, 0.95f), JumpBorder = new Color(0.55f, 1.6f, 1.8f);
        static readonly Color SlagFill = new Color(0.95f, 0.36f, 0.08f), SlagBorder = new Color(2.4f, 1.2f, 0.35f);
        readonly Dictionary<PlayerController, float> burn = new Dictionary<PlayerController, float>();

        public Hazard Spawn(Hazard h)
        {
            var fx = w.BossFx;
            switch (h.Shape)
            {
                case HazardShape.Circle: h.Tele = fx.Circle(h.Radius); break;
                case HazardShape.Ring: h.Tele = fx.Ring(h.Inner, h.Radius); break;
                case HazardShape.Cone: h.Tele = fx.Cone(h.Radius, h.HalfAngle); break;
                case HazardShape.Tether: h.Tele = fx.Circle(0.01f); h.Tele.Tint(Color.clear, Color.clear); break;   // the ability draws the chain
                default: h.Tele = fx.Lane(h.Length, h.Width); break;
            }
            // jump-clearable bars read differently from everything else (teal, plus the HUD's JUMP label)
            if (h.JumpClearable) h.Tele.Tint(JumpFill, JumpBorder);
            else if (h.BurnPerSecond > 0) h.Tele.Tint(SlagFill, SlagBorder);   // standing slag: molten, not the strike coral
            h.Tele.Place(h.Origin, h.Yaw);
            if (h.LockAt <= 0) h.Tele.Lock();
            live.Add(h);
            Spawned?.Invoke(h);
            return h;
        }

        public void Step()
        {
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (!h.Locked) h.Track?.Invoke(h);
                h.Age++;
                if (h.Age == h.LockAt) h.Tele.Lock();
                h.Tele.Place(h.Origin, h.Yaw);
                h.Tele.Progress((float)h.Age / Mathf.Max(1, h.ResolveAt));
            }
            StepBurn();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var h = live[i];
                if (h.Age < h.ResolveAt) continue;
                bool first = h.Age == h.ResolveAt, last = h.Age >= h.ResolveAt + Mathf.Max(0, h.ActiveTicks - 1);
                if (last && h.BurnPerSecond > 0) { live.RemoveAt(i); h.Tele.Expire(); h.OnResolve?.Invoke(h); continue; }   // cooled slag: harmless, no hit
                if (h.BurnPerSecond > 0) continue;
                if (last) { live.RemoveAt(i); h.Tele.Resolve(); }
                if (first) Resolved++;
                if (h.ShareTotal > 0) { if (first) ResolveShared(h); }
                else
                    foreach (var p in w.Players)
                    {
                        if (p.Down || h.Hit.Contains(p) || !h.Hits(p)) continue;
                        if (h.JumpClearable && p.Height >= h.ClearHeight) continue;   // the only height rule (R19)
                        h.Hit.Add(p);
                        p.ReceiveHit(h);
                    }
                if (first) h.OnResolve?.Invoke(h);
                if (last) h.OnSpent?.Invoke(h);
            }
            SetQuiet(live.Count > 0);
        }

        // Shared soak / line split, once at resolve: the total is cut into one share per player inside. Empty with
        // WipeIfEmpty downs every standing player; empty without it does nothing.
        void ResolveShared(Hazard h)
        {
            var inside = new List<PlayerController>();
            foreach (var p in w.Players)
                if (!p.Down && !h.Hit.Contains(p) && h.Hits(p) && !(h.JumpClearable && p.Height >= h.ClearHeight)) inside.Add(p);
            if (inside.Count == 0)
            {
                if (h.WipeIfEmpty) foreach (var p in w.Players.ToArray()) if (!p.Down) { h.Hit.Add(p); p.ForceDown(h); }
                return;
            }
            int each = ShareOf(h, inside.Count);
            foreach (var p in inside) { h.Hit.Add(p); p.ReceiveHit(h, each); }
        }
        public static int ShareOf(Hazard h, int inside) => inside <= 0 ? 0 : Mathf.CeilToInt(h.ShareTotal / (float)inside);

        // Standing hazards: each player burns at the highest rate of the locked ones they stand in (overlaps never add up),
        // in whole HP as the fraction accumulates. Leaving every pool drops the carried fraction.
        void StepBurn()
        {
            foreach (var p in w.Players)
            {
                Hazard src = null;
                if (!p.Down)
                    foreach (var h in live)
                        if (h.BurnPerSecond > 0 && h.Locked && (src == null || h.BurnPerSecond > src.BurnPerSecond) && h.Hits(p)) src = h;
                if (src == null) { burn.Remove(p); continue; }
                burn.TryGetValue(p, out float acc); acc += src.BurnPerSecond * Sim.Dt;
                int dmg = Mathf.FloorToInt(acc); acc -= dmg; burn[p] = acc;
                if (dmg > 0) p.ReceiveBurn(src, dmg);
            }
        }

        // Cancels every pending hazard: none of them can resolve or deal damage afterwards.
        public void ClearAll()
        {
            live.Clear(); burn.Clear();
            w.BossFx.ClearTelegraphs();
            SetQuiet(false);
        }

        // Every arena script (the key-art dais and Areas 1-4) has a public bool "quiet" that dims its floor decoration while
        // hazards are up (boss design §8.1). They share no base class, so they're found by that field.
        static readonly List<(MonoBehaviour arena, FieldInfo quiet)> arenas = new List<(MonoBehaviour, FieldInfo)>();
        public static void FindArena()
        {
            arenas.Clear();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var ns = mb.GetType().Namespace ?? "";
                if (!ns.StartsWith("LastBell.Arenas") && !ns.StartsWith("LastBell.Areas")) continue;
                var f = mb.GetType().GetField("quiet", BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(bool)) arenas.Add((mb, f));
            }
        }
        public static int ArenaCount => arenas.Count;
        static void SetQuiet(bool q) { foreach (var (arena, f) in arenas) if (arena) f.SetValue(arena, q); }
    }
}
