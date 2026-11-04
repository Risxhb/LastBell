using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Meridian Recall (aurelion.meridian_recall), v4 Hard only. Proposal: work/aurelion-difficulty-20261027/PROPOSAL.md,
    // "One named extra Hard mechanic" (Chzy18 accepted prototyping it, 28 Oct, Q2).
    // He replays the last Meridian Lances volley once: the same locked beams, from the boss centre they were fired from
    // (not where he stands now), with no retargeting. Each beam is the original two opposite 28 m x 2.2 m Strike lanes,
    // shown locked from the first tick, resolving 3.5 s later at every level it runs on (no Hard x0.7: the warning is the
    // reaction budget the proposal checked, 12.8 m of walking at 4.2 m/s). The halves share their hits like Lances, so a
    // player takes at most one hit per beam. 50 before the difficulty scale (Hard 65): heavy, never a one-shot.
    // The phase plan runs it as its own block after a completed volley (P2 and P3), never in Total Eclipse or P4.
    // Returns on the resolve tick; with no volley to remember it returns at once.
    public sealed class MeridianRecall : AurelionAbility
    {
        public override string Id => "aurelion.meridian_recall";
        public override string Name => "Meridian Recall";

        public const float WarnS = 3.5f;
        public const int Damage = 50, Stun = 15;

        // test evidence from the last run: the replayed half-lanes and the bearings they copied (origin, yaw)
        public readonly List<Hazard> Replay = new List<Hazard>();
        public readonly List<(Vector3 origin, float yaw)> Remembered = new List<(Vector3, float)>();

        // the volley to replay: Lances' last one, read once it has resolved (its Origin and Yaw are then final)
        public void Remember()
        {
            Remembered.Clear();
            var lances = Boss.Lances;
            if (lances == null || lances.Volleys.Count == 0) return;
            foreach (var h in lances.Volleys[lances.Volleys.Count - 1]) Remembered.Add((h.Origin, h.Yaw));
        }

        public int Duration => Sim.Ticks(WarnS);

        public IEnumerable<int> Run()
        {
            Remember();
            Replay.Clear();
            if (Remembered.Count == 0) yield break;
            Say(Name);
            int res = Duration;
            Glow(1.4f); Clip("MeridianAlignment", res);
            for (int i = 0; i + 1 < Remembered.Count; i += 2)
            {
                var a = Lane(Remembered[i], res); var o = Lane(Remembered[i + 1], res); bool flare = i == 0;
                a.OnResolve = hz => { if (flare) Flare(0.8f); foreach (var p in a.Hit) o.Hit.Add(p); };
                o.OnResolve = hz => { foreach (var p in o.Hit) a.Hit.Add(p); };
                Replay.Add(Spawn(a)); Replay.Add(Spawn(o));
            }
            yield return res - 1;                                    // resumes on the resolve tick
        }

        Hazard Lane((Vector3 origin, float yaw) b, int resolveAt) => new Hazard
        {
            Source = Name, Shape = HazardShape.Lane, Kind = HazardKind.Strike,
            Origin = b.origin, Yaw = b.yaw, Length = BeyondRim, Width = MeridianLances.Width,
            LockAt = 0, ResolveAt = resolveAt, Damage = Damage, Stun = Stun, Knockback = 1.5f,
        };
    }
}
