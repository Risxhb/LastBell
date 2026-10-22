using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 02 Gear Chain (crucivar.gear_chain), brief 02-crucivar.gear_chain.md. Gear-chains clamp the standing players in pairs
    // (nearest to the boss with the farthest, and so on); the odd one out, or a solo player, is chained to a gear-tooth post
    // at the rim point nearest them. At resolve every chain must be 6-10 m long (Easy 5-11 m); outside the band it snaps:
    // 28 Ground to both ends, 0.25 s stun, no knockback (it would pin players against the rim wall). One Tether hazard per
    // chain, so a player is hit at most once. A downed end drops its chain with no snap and the partner is re-chained to a
    // post with the remaining time; a dash breaks the dasher's chain with no snap (provisional rule §5).
    // Contract: Run() clamps the chains and returns when they have resolved.
    public sealed class GearChain : CrucivarAbility
    {
        public override string Id => "crucivar.gear_chain";

        public const int Damage = 28;
        public const float Warn = 3.0f, Stun = 0.25f;
        public float Min => Easy ? 5f : 6f;
        public float Max => Easy ? 11f : 10f;

        // one chain: A to B, or A to its rim post when B is null
        public sealed class Link
        {
            public PlayerController A, B;
            public Vector3 Post;
            public Hazard Hazard;
            public string Ended;                 // null while live; "down" or "dash" once it has ended with no snap
            public bool ToPost => B == null;
            public Vector3 Other => B != null ? B.transform.position : Post;
            public float Length => Sim.Flat(A.transform.position - Other).magnitude;
        }
        readonly List<Link> links = new List<Link>();
        public IReadOnlyList<Link> Links => links;   // tests and presentation
        public bool InBand(Link l) => l.Length >= Min && l.Length <= Max;

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Gear Chain");
            links.Clear();
            var boss = Flat(Boss.transform.position);
            // pairing is taken once, at cast: sort by distance to the boss (ties keep party order)
            var order = Standing().Select((p, i) => (p, i)).OrderBy(x => Flat(x.p.transform.position - boss).magnitude).ThenBy(x => x.i)
                .Select(x => x.p).ToList();
            int n = order.Count;
            if (n == 0) yield break;
            int warn = T(Warn);
            for (int i = 0; i < n / 2; i++) links.Add(Clamp(order[i], order[n - 1 - i], warn));
            if (n % 2 == 1) links.Add(Clamp(order[n / 2], null, warn));

            // only a dash started after the clamp breaks a chain
            var dashing = new HashSet<PlayerController>(order.Where(p => p.Dashing));
            while (links.Any(l => W.Hazards.Live.Contains(l.Hazard)))
            {
                foreach (var l in links) Watch(l, dashing);
                foreach (var p in order) if (p && p.Dashing) dashing.Add(p); else dashing.Remove(p);
                yield return 1;
            }
        }

        Link Clamp(PlayerController a, PlayerController b, int warn)
        {
            var l = new Link { A = a, B = b };
            if (b == null) l.Post = RimPost(a.transform.position);
            l.Hazard = W.Hazards.Spawn(new Hazard
            {
                Source = "Gear Chain", Shape = HazardShape.Tether, Kind = HazardKind.Ground, Damage = Damage,
                Knockback = 0, Stun = Sim.Ticks(Stun), LockAt = warn, ResolveAt = warn, Origin = Mid(l),
                Track = h => h.Origin = Mid(l),
                Includes = p => l.Ended == null && (p == l.A || (l.B != null && p == l.B)) && !InBand(l),
            });
            l.Hazard.OnResolve = h => { if (l.Ended == null && !InBand(l)) W.BossFx.SlagSplash(Mid(l), 0.8f); };
            GearChainFx.Show(W, this, l);
            return l;
        }

        // a downed end ends the chain with no snap; a standing partner is re-chained to a post with the remaining time
        void Watch(Link l, HashSet<PlayerController> wasDashing)
        {
            if (l.Ended != null || !W.Hazards.Live.Contains(l.Hazard)) return;
            bool aDown = !l.A || l.A.Down, bDown = l.B != null && (!l.B || l.B.Down);
            if (aDown && (l.ToPost || bDown)) { l.Ended = "down"; return; }
            if (aDown) { l.A = l.B; l.B = null; l.Post = RimPost(l.A.transform.position); }
            else if (bDown) { l.B = null; l.Post = RimPost(l.A.transform.position); }
            if (NewDash(l.A, wasDashing) || (l.B != null && NewDash(l.B, wasDashing))) l.Ended = "dash";
        }
        static bool NewDash(PlayerController p, HashSet<PlayerController> was) => p.Dashing && !was.Contains(p);

        // the rim point nearest the player (the arena centre is the origin); a player dead centre gets the post behind them
        // as seen from the boss
        Vector3 RimPost(Vector3 at)
        {
            var d = Flat(at);
            if (d.sqrMagnitude < 0.01f) d = Flat(at - Boss.transform.position);
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            return d.normalized * Rim;
        }
        static Vector3 Mid(Link l) => Flat((l.A.transform.position + l.Other) * 0.5f);
    }
}
