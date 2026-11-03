using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run.Networking
{
    // Which effect family a recorded call belongs to. Values are on the wire (LanWorldFrame.Effect.Target).
    public enum LanFxTarget : byte { Player = 0, Boss = 1, Hud = 2, Shake = 3, Projectile = 4, BossPart = 5 }

    // Presentation calls for LAN clients. The effect entry points (PlayerFx, the shared BellkeeperFx hits, camera shake,
    // RunHud damage numbers, custom projectile moves and ends, and boss parts: TempoTyrantFx ribbons, Oracle and
    // Pendulum Sentinel sounds) report each call here while a host session records.
    // Record is null otherwise, so the normal game pays one null check per effect call.
    public static class LanFx
    {
        public static Action<LanFxTarget, string, object[]> Record;
        public static void Projectile(ProjectileSystem.Projectile p, string method, bool hit, Vector3 at)
        {
            if (Record != null) Record(LanFxTarget.Projectile, method, new object[] { p, hit, at });
        }
    }

    // Host: turns recorded calls into wire events tied to the host's tick and the LAN roster, until the next snapshot.
    public sealed class LanFxCapture : IDisposable
    {
        public const int MaxPending = 400;   // per snapshot; the oldest go first if the host falls behind
        readonly LanHostRoster roster;
        readonly List<LanWorldFrame.Effect> pending = new List<LanWorldFrame.Effect>();
        readonly Action<LanFxTarget, string, object[]> hook;
        public int Dropped { get; private set; }
        public LanFxCapture(LanHostRoster roster)
        {
            this.roster = roster; hook = Record; LanFx.Record = hook;
        }
        public void Dispose() { if (LanFx.Record == hook) LanFx.Record = null; pending.Clear(); }
        public void Clear() => pending.Clear();
        public void Drain(List<LanWorldFrame.Effect> into) { into.AddRange(pending); pending.Clear(); }

        void Record(LanFxTarget target, string method, object[] args)
        {
            var d = RunDirector.Instance; var w = d ? d.World : null;
            if (w == null || args == null || args.Length > LanWorldFrame.MaxEffectArgs) return;
            var e = new LanWorldFrame.Effect { Tick = Mathf.Max(0, w.Tick), Target = (byte)target, Method = method, Args = new LanWorldFrame.EffectArg[args.Length] };
            for (int i = 0; i < args.Length; i++) if (!Convert(args[i], w, d, out e.Args[i])) { Dropped++; return; }
            if (pending.Count >= MaxPending) { pending.RemoveAt(0); Dropped++; }
            pending.Add(e);
        }

        int PlayerId(CombatWorld w, PlayerController p)
        {
            int index = w.Players.IndexOf(p);
            if (index == 0) return 0;
            return index > 0 && index - 1 < roster.Remote.Count ? roster.Remote[index - 1].Id : -1;
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) <= 10000;

        bool Convert(object o, CombatWorld w, RunDirector d, out LanWorldFrame.EffectArg a)
        {
            a = default;
            switch (o)
            {
                case null: a.Kind = LanWorldFrame.EffectArg.None; return true;
                case PlayerController p: a.Kind = LanWorldFrame.EffectArg.PlayerRef; a.I = PlayerId(w, p); return a.I >= 0;
                case BossEncounter b: a.Kind = LanWorldFrame.EffectArg.BossRef; return b == d.Boss;
                case Transform t:
                    if (d.Boss && t == d.Boss.transform) { a.Kind = LanWorldFrame.EffectArg.BossTransform; return true; }
                    foreach (var p in w.Players) if (p && t == p.transform) { a.Kind = LanWorldFrame.EffectArg.PlayerTransform; a.I = PlayerId(w, p); return a.I >= 0; }
                    return false;
                case Vector3 v: a.Kind = LanWorldFrame.EffectArg.Vector; a.X = v.x; a.Y = v.y; a.Z = v.z; return Finite(v.x) && Finite(v.y) && Finite(v.z);
                case float f: a.Kind = LanWorldFrame.EffectArg.Number; a.X = f; return Finite(f);
                case int n: a.Kind = LanWorldFrame.EffectArg.Integer; a.I = n; return true;
                case bool flag: a.Kind = LanWorldFrame.EffectArg.Flag; a.I = flag ? 1 : 0; return true;
                case Color c: a.Kind = LanWorldFrame.EffectArg.Tint; a.X = c.r; a.Y = c.g; a.Z = c.b; a.W = c.a; return Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
                case string s: a.Kind = LanWorldFrame.EffectArg.Words; a.S = s.Length <= 128 ? s : s.Substring(0, 128); return true;
                case ProjectileSystem.Projectile pr:
                    a.Kind = LanWorldFrame.EffectArg.ProjectileRef; a.I = pr.NetworkId; a.X = pr.Pos.x; a.Y = pr.Pos.y; a.Z = pr.Pos.z; a.VX = pr.Vel.x; a.VY = pr.Vel.y; a.VZ = pr.Vel.z;
                    return pr.NetworkId > 0 && Finite(a.X) && Finite(a.Y) && Finite(a.Z) && Finite(a.VX) && Finite(a.VY) && Finite(a.VZ);
                default: return false;
            }
        }
    }

    // Client: replays the host's recorded calls on the replica's own effect objects when the drawn timeline reaches their
    // tick, so effects line up with the interpolated players and boss. The dispatch switch is generated
    // (LanFxReplay.Generated.cs). A call that fails is counted and skipped; it never ends the session.
    public sealed partial class LanFxReplay
    {
        readonly PlayerFx playerFx; readonly BellkeeperFx bossFx;
        readonly Func<int, PlayerController> player; readonly Func<BossEncounter> boss;
        readonly Dictionary<int, ProjectileSystem.Projectile> proxies = new Dictionary<int, ProjectileSystem.Projectile>();
        readonly Queue<LanWorldFrame.Effect> queue = new Queue<LanWorldFrame.Effect>();
        public int Played { get; private set; }
        public int Failed { get; private set; }
        public readonly HashSet<string> PlayedMethods = new HashSet<string>();   // diagnostics: which effects reached this client
        public LanFxReplay(PlayerFx playerFx, BellkeeperFx bossFx, Func<int, PlayerController> player, Func<BossEncounter> boss)
        {
            this.playerFx = playerFx; this.bossFx = bossFx; this.player = player; this.boss = boss;
        }
        public void Enqueue(List<LanWorldFrame.Effect> effects) { foreach (var e in effects) queue.Enqueue(e); }
        public void Reset() { queue.Clear(); proxies.Clear(); }
        public void PlayUntil(int tick)
        {
            while (queue.Count > 0 && queue.Peek().Tick <= tick) Play(queue.Dequeue());
        }
        void Play(LanWorldFrame.Effect e)
        {
            bool ok;
            try
            {
                switch ((LanFxTarget)e.Target)
                {
                    case LanFxTarget.Player: ok = PlayPlayer(e.Method, e.Args); break;
                    case LanFxTarget.Boss: ok = PlayBoss(e.Method, e.Args); break;
                    case LanFxTarget.Hud: ok = PlayHud(e.Method, e.Args); break;
                    case LanFxTarget.Shake: ok = PlayShake(e.Method, e.Args); break;
                    case LanFxTarget.Projectile: ok = PlayProjectile(e.Method, e.Args); break;
                    case LanFxTarget.BossPart: ok = PlayBossPart(e.Method, e.Args); break;
                    default: ok = false; break;
                }
            }
            catch (Exception ex)
            {
                ok = false;
                if (Failed < 5) Debug.LogWarning("[LAN] effect " + e.Method + " failed on the client: " + ex.Message);
            }
            if (ok) { Played++; PlayedMethods.Add(e.Method); } else Failed++;
        }
        bool PlayProjectile(string m, LanWorldFrame.EffectArg[] a)
        {
            if (a.Length != 3 || a[0].Kind != LanWorldFrame.EffectArg.ProjectileRef) return false;
            if (m == "Move")
            {
                var pr = Proj(a[0]); pr.OnMove?.Invoke(pr); return true;
            }
            if (m == "End")
            {
                if (!proxies.TryGetValue(a[0].I, out var pr)) return false;
                proxies.Remove(a[0].I); pr.Pos = V(a[2]); pr.OnEnd?.Invoke(pr, B(a[1]), V(a[2])); return true;
            }
            return false;
        }

        // argument conversion; names match the generated dispatcher
        PlayerController P(LanWorldFrame.EffectArg a) => a.Kind == LanWorldFrame.EffectArg.PlayerRef ? player(a.I) : null;
        BossEncounter Boss(LanWorldFrame.EffectArg a) { if (a.Kind != LanWorldFrame.EffectArg.BossRef) return null; var b = boss(); return b ? b : null; }
        Transform T(LanWorldFrame.EffectArg a)
        {
            if (a.Kind == LanWorldFrame.EffectArg.PlayerTransform) { var p = player(a.I); return p ? p.transform : null; }
            if (a.Kind == LanWorldFrame.EffectArg.BossTransform) { var b = boss(); return b ? b.transform : null; }
            return null;
        }
        // a boss-specific effect or sound component on the replica boss (its Build() made the same ones as the host's)
        T BossPart<T>() where T : Component { var b = boss(); return b ? b.GetComponent<T>() : null; }
        static Vector3 V(LanWorldFrame.EffectArg a) => new Vector3(a.X, a.Y, a.Z);
        static float F(LanWorldFrame.EffectArg a) => a.X;
        static int I(LanWorldFrame.EffectArg a) => a.I;
        static bool B(LanWorldFrame.EffectArg a) => a.I != 0;
        static Color C(LanWorldFrame.EffectArg a) => new Color(a.X, a.Y, a.Z, a.W);
        static string S(LanWorldFrame.EffectArg a) => a.S ?? "";
        // A stand-in for a host projectile with custom visuals (Arcanist missiles): the visual hooks OnMove/OnEnd on it,
        // and the host's recorded Move/End calls drive it.
        ProjectileSystem.Projectile Proj(LanWorldFrame.EffectArg a)
        {
            if (a.Kind != LanWorldFrame.EffectArg.ProjectileRef) return null;
            if (!proxies.TryGetValue(a.I, out var pr)) { pr = new ProjectileSystem.Projectile(); proxies[a.I] = pr; if (proxies.Count > 512) proxies.Clear(); }
            pr.Pos = new Vector3(a.X, a.Y, a.Z); pr.Vel = new Vector3(a.VX, a.VY, a.VZ);
            return pr;
        }
    }
}
