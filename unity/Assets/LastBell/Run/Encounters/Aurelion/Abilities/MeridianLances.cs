using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 01 Meridian Lances (aurelion.meridian_lances), brief abilities-v2/01-meridian_lances.md (proposal).
    // Run(beams, volleys, cascade) fires 3 or 4 full-arena beams through the boss, 180/beams degrees apart. Each beam is
    // two 28 m Strike lanes (2.2 m wide) from his centre with opposite yaws, so Brace reads every hit as coming from him.
    // The two halves share their hits, so a player is hit at most once per beam (the halves only meet inside his body,
    // which players can't enter, but the hand-over makes it exact). Beams are separate instances: a player standing where
    // two beams cross (only inside his body) would take both.
    //   Volley 1: one beam runs through the farthest player; the aim tracks that player's angle at up to 120°/s for 1.2 s,
    //             locks, and resolves 1.2 s later (2.4 s).
    //   Volley 2 (volleys == 2): warns on volley 1's resolve tick, one beam through the nearest player, tracks 1.0 s,
    //             resolves 1.2 s after the lock (2.2 s). With cascade (Phase 3) it is instead volley 1's locked aim
    //             rotated 22.5°, shown locked at once, resolving 2.2 s after it appears.
    // Easy stretches every track and window by 1.25 (T); Hard is unchanged. Returns on the tick the last volley resolves.
    public sealed class MeridianLances : AurelionAbility
    {
        public override string Id => "aurelion.meridian_lances";
        public override string Name => "Meridian Lances";

        public const float Width = 2.2f, TurnRate = 120f, CascadeTurn = 22.5f;
        public const float Track1S = 1.2f, Track2S = 1.0f, WindowS = 1.2f, CascadeS = 2.2f;
        public const int Damage = 35, Stun = 15;   // 0.25 s stun, standard 1.5 m knockback sideways out of the lane

        // test evidence from the last run: each volley's half-lanes (beam b = [2b] and [2b+1]), its aim (beam 0's yaw,
        // final once locked) and its spawn tick
        public readonly List<List<Hazard>> Volleys = new List<List<Hazard>>();
        public readonly List<int> SpawnTicks = new List<int>();
        readonly List<Aim> aims = new List<Aim>();
        public float AimOf(int volley) => aims[volley].Yaw;

        public IEnumerable<int> Run(int beams, int volleys, bool cascade)
        {
            Say(cascade ? "Cascading Lances" : Name);
            Volleys.Clear(); SpawnTicks.Clear(); aims.Clear();
            beams = Mathf.Clamp(beams, 1, 8);
            Glow(1.4f); Clip("MeridianAlignment", T(Track1S + WindowS));   // stand-in: glaive raised to the zenith

            // volley 1: through the farthest player
            int lock1 = T(Track1S), res1 = lock1 + T(WindowS);
            var v1 = Volley(beams, AimAt(Farthest(BossPos)), lock1, res1, () => Farthest(BossPos));
            yield return res1 - 1;                                   // resumes on volley 1's resolve tick
            if (volleys < 2) yield break;

            if (cascade)
            {
                // volley 2, cascade: volley 1's lines turned 22.5°, locked at once
                int res2 = T(CascadeS);
                Volley(beams, v1.Yaw + CascadeTurn, 0, res2, null);
                yield return res2 - 1;
            }
            else
            {
                // volley 2: through the nearest player
                int lock2 = T(Track2S), res2 = lock2 + T(WindowS);
                Volley(beams, AimAt(Nearest(BossPos)), lock2, res2, () => Nearest(BossPos));
                yield return res2 - 1;
            }
        }

        float AimAt(PlayerController p) => p ? Sim.YawTo(BossPos, p.transform.position, Body.eulerAngles.y) : Body.eulerAngles.y;

        // the shared aim of one volley: every half-lane reads it, and it turns once per tick while tracking
        sealed class Aim { public float Yaw; public int Turned = -1; }

        Aim Volley(int beams, float yaw, int lockAt, int resolveAt, Func<PlayerController> target)
        {
            var aim = new Aim { Yaw = yaw };
            float spacing = 180f / beams;
            var list = new List<Hazard>();
            for (int b = 0; b < beams; b++)
            {
                Hazard[] pair = new Hazard[2];
                for (int half = 0; half < 2; half++)
                {
                    float offset = b * spacing + half * 180f;
                    var h = new Hazard
                    {
                        Source = Name, Shape = HazardShape.Lane, Kind = HazardKind.Strike,
                        Origin = BossPos, Yaw = aim.Yaw + offset, Length = BeyondRim, Width = Width,
                        LockAt = lockAt, ResolveAt = resolveAt, Damage = Damage, Stun = Stun, Knockback = 1.5f,
                    };
                    h.Track = hz =>
                    {
                        if (target != null && aim.Turned != W.Tick)
                        {
                            aim.Turned = W.Tick;
                            var p = target();
                            if (p)
                            {
                                // the lines repeat every `spacing` degrees: turn the shortest way onto the nearest one
                                float want = Sim.YawTo(BossPos, p.transform.position, aim.Yaw);
                                float err = Mathf.Repeat(want - aim.Yaw + spacing / 2, spacing) - spacing / 2;
                                aim.Yaw += Mathf.Clamp(err, -TurnRate * Sim.Dt, TurnRate * Sim.Dt);
                            }
                        }
                        hz.Origin = BossPos;
                        hz.Yaw = aim.Yaw + offset;
                    };
                    pair[half] = h;
                }
                // one hit per beam: both halves resolve on the same tick, and whichever is tested first hands its hits to
                // the other in its OnResolve, which runs before the other half's hit test
                var a = pair[0]; var o = pair[1]; bool flare = b == 0;
                a.OnResolve = hz => { if (flare) Flare(0.8f); foreach (var p in a.Hit) o.Hit.Add(p); };
                o.OnResolve = hz => { foreach (var p in o.Hit) a.Hit.Add(p); };
                list.Add(Spawn(a)); list.Add(Spawn(o));
            }
            Volleys.Add(list); SpawnTicks.Add(W.Tick); aims.Add(aim);
            return aim;
        }
    }
}
