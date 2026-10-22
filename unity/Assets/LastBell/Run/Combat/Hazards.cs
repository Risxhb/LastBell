using System;
using System.Collections.Generic;
using System.Reflection;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    public enum HazardShape { Circle, Ring, Cone, Lane }

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
        public int Damage;
        public bool JumpClearable; public float ClearHeight = 0.35f;
        public float Knockback = 1.5f;
        public int Stun = 15;
        // > 0: a standing hazard (Crucivar's cooling slag). It deals this much per second to players inside while it
        // lives, with no knockback, and simply expires at ResolveAt (no resolve hit). Overlapping ones never multiply.
        public float BurnPerSecond;
        public Action<Hazard> Track;            // called every tick until locked (follow a target / the boss facing)
        public Action<Hazard> OnResolve;         // presentation of the resolve (effects), after the hit test

        public int Age { get; internal set; }
        public bool Locked => Age >= LockAt;
        internal BellkeeperFx.Telegraph Tele;
        internal readonly HashSet<PlayerController> Hit = new HashSet<PlayerController>();

        public const float Margin = 0.3f;        // player body radius: touching the border counts

        public bool Contains(Vector3 p)
        {
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

        // direction a hit pushes the player: away from the origin, or sideways out of a lane
        public Vector3 PushDir(Vector3 p)
        {
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
                default: h.Tele = fx.Lane(h.Length, h.Width); break;
            }
            // jump-clearable bars read differently from everything else (teal, plus the HUD's JUMP label)
            if (h.JumpClearable) h.Tele.Tint(JumpFill, JumpBorder);
            else if (h.BurnPerSecond > 0) h.Tele.Tint(SlagFill, SlagBorder);   // standing slag: molten, not the strike coral
            h.Tele.Place(h.Origin, h.Yaw);
            if (h.LockAt <= 0) h.Tele.Lock();
            live.Add(h);
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
                live.RemoveAt(i);
                if (h.BurnPerSecond > 0) { h.Tele.Expire(); h.OnResolve?.Invoke(h); continue; }   // cooled: harmless, no hit
                h.Tele.Resolve(); Resolved++;
                foreach (var p in w.Players)
                {
                    if (p.Down || h.Hit.Contains(p) || !h.Contains(p.transform.position)) continue;
                    if (h.JumpClearable && p.Height >= h.ClearHeight) continue;   // the only height rule (R19)
                    h.Hit.Add(p);
                    p.ReceiveHit(h);
                }
                h.OnResolve?.Invoke(h);
            }
            SetQuiet(live.Count > 0);
        }

        // Cancels every pending hazard: none of them can resolve or deal damage afterwards.
        // Standing hazards: each player burns at the highest rate of the locked ones they stand in (overlaps never add up),
        // in whole HP as the fraction accumulates. Leaving every pool drops the carried fraction.
        void StepBurn()
        {
            foreach (var p in w.Players)
            {
                Hazard src = null;
                if (!p.Down)
                    foreach (var h in live)
                        if (h.BurnPerSecond > 0 && h.Locked && (src == null || h.BurnPerSecond > src.BurnPerSecond) && h.Contains(p.transform.position)) src = h;
                if (src == null) { burn.Remove(p); continue; }
                burn.TryGetValue(p, out float acc); acc += src.BurnPerSecond * Sim.Dt;
                int dmg = Mathf.FloorToInt(acc); acc -= dmg; burn[p] = acc;
                if (dmg > 0) p.ReceiveBurn(src, dmg);
            }
        }

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
