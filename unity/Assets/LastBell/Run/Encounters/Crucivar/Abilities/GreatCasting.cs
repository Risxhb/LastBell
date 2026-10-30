using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 04 The Great Casting (crucivar.great_casting), brief 04-crucivar.great_casting.md. Signature.
    //   Pour   lane 2.6 m from the boss to the wall at the farthest standing player, tracks to 1.5 s, resolves 3.5 s,
    //          Ground. Co-op: a line split of 80 among the players inside (no wipe when empty). Solo: a plain 40 lane.
    //   March  spokes from the boss at the anvil, 30 deg off the pour (the cooled channel is a gap): stamps r 1.2 on the
    //          arena rings 12.5 / 10.5 / 8.5 / 6.5 / 4.5 m, all shown at 3.0, resolving rim-first at 5.0 .. 7.0, 18 each.
    //          spokes 0 (phase 1: pour only), 3 (phase 2: every other one) or 6 (phase 3).
    // Easy stretches every time by 1.25 (T). Run returns when the last hazard has resolved (3.5 s or 7.0 s).
    public sealed class GreatCasting : CrucivarAbility
    {
        public override string Id => "crucivar.great_casting";

        public const float PourWidth = 2.6f, PourLock = 1.5f, PourResolve = 3.5f, MarchShow = 3.0f, MarchFirst = 5.0f, MarchStep = 0.5f;
        public const int PourTotal = 80, PourSolo = 40, StampDmg = 18;
        public const float StampRadius = 1.2f, SpokeOffset = 30f;
        public static readonly float[] Rings = { 12.5f, 10.5f, 8.5f, 6.5f, 4.5f };   // rim first

        public Hazard Pour { get; private set; }
        public readonly List<Hazard> Stamps = new List<Hazard>();

        // v3 (ABILITIES-v3.md): the pour locks at 1.5 and resolves at 3.0 for a 100 split; solo gets two 50 pours, the
        // second laid on the player's position as the first lands and resolving 2.0 s later. 8 spokes 22.5 deg off the
        // pour, stamps r 1.5 on 13 / 11 / 9 / 7 / 5 m, 30 each (phase 1 uses every other spoke). Phase 3 adds an outward
        // return wave on the 8 offset spokes (in line with the pour), resolving 7.5 .. 9.5 s inside-out; it is shown at
        // 5.5 s so even its first stamp has the 2.0 s floor, and resolves only after the inward wave's last ring.
        public const float PourResolveV3 = 3.0f, SoloSecondWarnV3 = 2.0f, StampRadiusV3 = 1.5f, SpokeOffsetV3 = 22.5f,
            ReturnShowV3 = 5.5f, ReturnFirstV3 = 7.5f;
        public const int PourTotalV3 = 100, PourSoloV3 = 50, StampDmgV3 = 30;
        public static readonly float[] RingsV3 = { 13f, 11f, 9f, 7f, 5f };   // rim first

        public IEnumerable<int> Run(int spokes, bool returnWave = false) => V3 ? RunV3(spokes, returnWave) : RunV2(spokes);

        IEnumerable<int> RunV3(int spokes, bool returnWave)
        {
            Boss.SetCurrent("The Great Casting");
            Stamps.Clear();
            var origin = Flat(Boss.transform.position);
            bool solo = W.Players.Count == 1;
            var target = Farthest(origin);
            int lockAt = T(PourLock), resolveAt = T(PourResolveV3), show = T(MarchShow);
            int marchEnd = T(MarchFirst + MarchStep * (RingsV3.Length - 1));
            int returnShow = T(ReturnShowV3), returnEnd = T(ReturnFirstV3 + MarchStep * (RingsV3.Length - 1));
            int soloEnd = resolveAt + T(SoloSecondWarnV3);
            int end = Mathf.Max(resolveAt, spokes > 0 ? (returnWave && !Easy ? returnEnd : marchEnd) : 0, solo ? soloEnd : 0);

            var pour = Pour = new Hazard
            {
                Source = "Pour", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = origin, Width = PourWidth,
                LockAt = lockAt, ResolveAt = resolveAt, Knockback = 1f, Yaw = Boss.transform.eulerAngles.y,
            };
            if (solo) pour.Damage = PourSoloV3;
            else { pour.ShareTotal = PourTotalV3; pour.ShareCount = Standing().Count; }   // line split, never a wipe
            Aim(pour, target);
            pour.Track = h => { if (target == null || target.Down) target = Farthest(origin); Aim(h, target); };
            pour.OnResolve = h =>
            {
                W.BossFx.StripStamp(h.Origin, h.Yaw, h.Length, h.Width); Boss.Flare(1f);
                // solo: a second pour is laid on where the player stands now, locked, and lands 2.0 s later
                if (solo && target != null && !target.Down)
                {
                    var second = new Hazard
                    {
                        Source = "Pour II", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = origin, Width = PourWidth,
                        LockAt = 0, ResolveAt = T(SoloSecondWarnV3), Knockback = 1f, Damage = PourSoloV3, Yaw = h.Yaw,
                    };
                    Aim(second, target);
                    second.OnResolve = s => { W.BossFx.StripStamp(s.Origin, s.Yaw, s.Length, s.Width); Boss.Flare(1f); };
                    SecondPour = W.Hazards.Spawn(second);
                }
            };
            SecondPour = null;
            W.Hazards.Spawn(pour);

            for (int i = 0; i < end; i++)
            {
                if (i == show && spokes > 0) MarchV3(origin, pour.Yaw, spokes, show);
                if (i == returnShow && spokes > 0 && returnWave && !Easy) ReturnV3(origin, pour.Yaw, returnShow);
                Boss.Glow(Mathf.Clamp01(i / (float)resolveAt));
                yield return 1;
            }
        }
        public Hazard SecondPour { get; private set; }

        void MarchV3(Vector3 origin, float pourYaw, int spokes, int now)
        {
            int step = spokes >= 8 ? 1 : 2;
            for (int ring = 0; ring < RingsV3.Length; ring++)
            {
                int at = T(MarchFirst + MarchStep * ring) - now;
                for (int j = 0; j < 8; j += step) StampV3("Stamp " + (ring + 1), origin, pourYaw + SpokeOffsetV3 + 45f * j, RingsV3[ring], at);
            }
        }

        // outward: the inner ring first, on the spokes the inward wave left empty (the pour channel among them)
        void ReturnV3(Vector3 origin, float pourYaw, int now)
        {
            for (int k = 0; k < RingsV3.Length; k++)
            {
                int ring = RingsV3.Length - 1 - k;
                int at = T(ReturnFirstV3 + MarchStep * k) - now;
                for (int j = 0; j < 8; j++) StampV3("Return " + (k + 1), origin, pourYaw + 45f * j, RingsV3[ring], at);
            }
        }

        void StampV3(string source, Vector3 origin, float yaw, float r, int at)
        {
            if (!StampCentre(origin, yaw, r, out var c)) return;
            Stamps.Add(W.Hazards.Spawn(new Hazard
            {
                Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = c,
                Radius = StampRadiusV3, LockAt = 0, ResolveAt = at, Damage = StampDmgV3, Knockback = 1f,
                OnResolve = hz => W.BossFx.DiebreakerResolve(hz.Origin, hz.Radius),
            }));
        }

        IEnumerable<int> RunV2(int spokes)
        {
            Boss.SetCurrent("The Great Casting");
            Stamps.Clear();
            var origin = Flat(Boss.transform.position);
            bool solo = W.Players.Count == 1;
            var target = Farthest(origin);
            int lockAt = T(PourLock), resolveAt = T(PourResolve), show = T(MarchShow);
            int end = spokes > 0 ? T(MarchFirst + MarchStep * (Rings.Length - 1)) : resolveAt;

            var pour = Pour = new Hazard
            {
                Source = "Pour", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = origin, Width = PourWidth,
                LockAt = lockAt, ResolveAt = resolveAt, Knockback = 1f,
                Yaw = Boss.transform.eulerAngles.y,
            };
            if (solo) pour.Damage = PourSolo;
            else { pour.ShareTotal = PourTotal; pour.ShareCount = Standing().Count; }   // line split, never a wipe
            Aim(pour, target);
            pour.Track = h =>
            {
                if (target == null || target.Down) target = Farthest(origin);
                Aim(h, target);
            };
            pour.OnResolve = h => { W.BossFx.StripStamp(h.Origin, h.Yaw, h.Length, h.Width); Boss.Flare(1f); };
            W.Hazards.Spawn(pour);

            for (int i = 0; i < end; i++)
            {
                if (i == show && spokes > 0) March(origin, pour.Yaw, spokes, show);
                Boss.Glow(Mathf.Clamp01(i / (float)resolveAt));
                yield return 1;
            }
        }

        void March(Vector3 origin, float pourYaw, int spokes, int now)
        {
            int step = spokes >= 6 ? 1 : 2;
            for (int ring = 0; ring < Rings.Length; ring++)
            {
                int at = T(MarchFirst + MarchStep * ring) - now;
                for (int j = 0; j < 6; j += step)
                {
                    if (!StampCentre(origin, SpokeYaw(pourYaw, j), Rings[ring], out var c)) continue;
                    var h = W.Hazards.Spawn(new Hazard
                    {
                        Source = "Stamp " + (ring + 1), Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = c,
                        Radius = StampRadius, LockAt = 0, ResolveAt = at, Damage = StampDmg, Knockback = 1f,
                        OnResolve = hz => W.BossFx.DiebreakerResolve(hz.Origin, hz.Radius),
                    });
                    Stamps.Add(h);
                }
            }
        }

        public static float SpokeYaw(float pourYaw, int j) => pourYaw + SpokeOffset + 60f * j;

        // A stamp sits where the spoke (a ray from the boss) crosses the arena ring of radius r around the anvil, so the
        // rows stay on the floor's rings and the march reads rim-first even when the boss stands off the centre.
        public static bool StampCentre(Vector3 origin, float spokeYaw, float r, out Vector3 c)
        {
            c = default;
            if (!RayToCircle(Flat(origin), Sim.Dir(spokeYaw), r, out float t) || t < 0.5f) return false;
            c = Flat(origin) + Sim.Dir(spokeYaw) * t;
            return true;
        }

        // farthest positive distance along dir from o to the circle |p| = r
        public static bool RayToCircle(Vector3 o, Vector3 dir, float r, out float t)
        {
            float b = Vector3.Dot(o, dir), disc = b * b - o.sqrMagnitude + r * r;
            t = disc < 0 ? 0 : -b + Mathf.Sqrt(disc);
            return disc >= 0 && t > 0;
        }

        // the lane runs from the boss end to the wall (plus the body margin), through the target
        void Aim(Hazard h, PlayerController target)
        {
            if (target != null)
            {
                var d = Flat(target.transform.position - h.Origin);
                if (d.sqrMagnitude > 0.01f) h.Yaw = Sim.YawTo(h.Origin, target.transform.position, h.Yaw);
            }
            h.Length = RayToCircle(h.Origin, Sim.Dir(h.Yaw), Rim + Hazard.Margin, out float t) ? t : Rim * 2;
            Boss.transform.rotation = Quaternion.Euler(0, h.Yaw, 0);
        }

        PlayerController Farthest(Vector3 from)
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in Standing())
            {
                float d = Flat(p.transform.position - from).sqrMagnitude;
                if (d > bd) { bd = d; best = p; }
            }
            return best;
        }
    }
}
