using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Last Toll Tide (`bellkeeper.toll_tide`), Bellkeeper ability set v2 — proposal, built 2026-10-22, not human-approved.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/04-toll-tide.md. The great cracked bell beyond the north rim
    // answers the Bellkeeper: a cracked shock-front rolls in from the wall and breaks at 7 m (the tide: a Ground ring on the
    // arena centre from 7.0 m out past the wall, 3.0 s, knocks 1.5 m inward). On the tick it breaks, the chest bell warns
    // point-blank (the Heart Toll: a Ground circle r 4.0 m on the boss, 2.0 s). Everyone ends in the band between.
    // Timeline (Medium, ability ticks): tide warns 0, hits 179; Heart Toll warns 179, hits 299 (Reflection's resolve in the
    // Final Toll); Cast() ends there. The 3.5 s punish window after it belongs to the encounter (PunishTicks).
    // Easy: tide 3.75 s, Heart Toll 2.5 s. Hard: unchanged (its extra mechanic is The Unfinished Hour). Same for any N.
    public sealed class TollTideAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.TollTide;
        public override string Name => "The Last Toll Tide";

        public const string TideSource = "Toll Tide", TollSource = "Heart Toll";
        public const float TideInner = 7.0f, WallReach = 2.0f, TollRadius = 4.0f, Push = 1.5f;
        public const int TideDamage = 35, TollDamage = 35;
        public int TideTicks => Level == Difficulty.Easy ? 225 : 180;
        public int TollTicks => Level == Difficulty.Easy ? 150 : 120;
        // the tide's outer edge: 2 m past the wall everywhere, the corners of a square floor included
        public static float TideOuter => CombatWorld.PlayerBound * (CombatWorld.SquareBound ? 1.4143f : 1f) + WallReach;
        const float BellYaw = 180;   // the great bell stands beyond the north rim (−Z)

        public Hazard Tide { get; private set; }
        public Hazard Toll { get; private set; }
        TideFront front;

        public override IEnumerable<int> Cast()
        {
            int tide = TideTicks, toll = TollTicks;
            Boss.ShowAttack(Name);
            if (Boss.Presenter) Boss.Presenter.PlayOnce("ShatteredChime", tide / (float)Sim.Hz);
            Tide = Spawn(new Hazard
            {
                Source = TideSource, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Vector3.zero,
                Inner = TideInner, Radius = TideOuter, LockAt = 0, ResolveAt = tide, Damage = TideDamage,
                Knockback = -Push,   // toward the centre, never into the wall
            });
            Tide.OnResolve = h =>
            {
                W.BossFx.TollResolve(h.Origin, h.Inner, h.Radius);
                front?.Clear(); front = null;
                Boss.Flare(Boss.Phase >= 3 ? 2.5f : 1.5f);
            };
            front = new TideFront(Tide.Origin, TideInner, Tide.Radius);

            // ticks 0 .. tide-2: the shock-front rolls in from the wall while the boss turns to the bell
            for (int i = 0; i < tide - 1; i++)
            {
                Boss.Face(BellYaw, 240);
                Boss.Glow(i / (float)tide);
                front?.Step(i / (float)(tide - 1), i);
                yield return 1;
            }

            // tick tide-1: the tide breaks after the boss steps; the Heart Toll warns on this same tick and keeps its full
            // warning (ResolveAt counts the spawn tick, so it rings `toll` ticks later)
            var at = Boss.transform.position;
            Toll = Spawn(new Hazard
            {
                Source = TollSource, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = new Vector3(at.x, 0, at.z),
                Radius = TollRadius, LockAt = 0, ResolveAt = toll + 1, Damage = TollDamage, Knockback = Push,
            });
            Toll.OnResolve = h =>
            {
                W.BossFx.ChimeResolve(h.Origin, h.Radius);
                W.BossFx.TollResolve(h.Origin, 0.6f, h.Radius);   // the clapper strike: a shock-front out to the edge
                Boss.Flare(Boss.Phase >= 3 ? 3f : 2f);
            };
            if (Boss.Presenter) Boss.Presenter.PlayOnce("HollowToll", (toll + 1) / (float)Sim.Hz);
            for (int i = 0; i <= toll; i++) { Boss.Glow(i / (float)toll); yield return 1; }
        }

        public override void Cancel()
        {
            front?.Clear(); front = null;
            Tide = Toll = null;
            base.Cancel();
        }

        // Stand-in presentation for the rolling shock-front (no art yet): a bright amber band that moves in from the ring's
        // outer edge and reaches the 7 m inner edge on the resolve tick, with fractured coral clock-ring arcs riding it that
        // tick round once a second. It stays inside the hazard's own footprint, so it never reads as a separate hazard; the
        // hit is the whole ring at resolve. Driven by Cast() on combat ticks; Clear() on the break, cancel or defeat.
        sealed class TideFront
        {
            static readonly Color Coral = new Color(1f, 0.36f, 0.24f), Amber = new Color(1f, 0.56f, 0.18f);
            const int Arcs = 7;
            readonly GameObject root;
            readonly Transform band;
            readonly Transform[] arcs = new Transform[Arcs];
            readonly List<Mesh> meshes = new List<Mesh>();
            readonly List<Material> mats = new List<Material>();
            readonly float inner, outer;

            public TideFront(Vector3 centre, float inner, float outer)
            {
                this.inner = inner; this.outer = outer;
                root = new GameObject("Toll Tide front");
                root.transform.position = new Vector3(centre.x, 0.07f, centre.z);
                band = Part("band", FxMeshes.Annulus(0.955f, 1f, 128), Amber, 1.1f, 0);
                for (int i = 0; i < Arcs; i++)
                    arcs[i] = Part("arc", FxMeshes.Sector(0.9f, 0.945f, 9f + 4f * (i * 3 % 4), 16), Coral, 1.3f, ArcYaw(i, 0));
            }
            static float ArcYaw(int i, float clock) => i * (360f / Arcs) + 11f * (i % 3) + (i % 2 == 0 ? clock : -clock);

            Transform Part(string name, Mesh mesh, Color color, float glow, float yaw)
            {
                mesh.RecalculateNormals(); meshes.Add(mesh);
                var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); var m = Prim.Material(color, glow); mats.Add(m); mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go.transform;
            }

            // u = 0 at the warning, 1 on the resolve tick
            public void Step(float u, int tick)
            {
                if (!root) return;
                float r = Mathf.Lerp(outer, inner, Mathf.Clamp01(u));
                band.localScale = new Vector3(r, 1, r);
                float clock = 6f * Mathf.Floor(tick / (float)Sim.Hz);   // a clock hand's tick: one 6° step per second
                for (int i = 0; i < Arcs; i++)
                {
                    arcs[i].localScale = new Vector3(r, 1, r);
                    arcs[i].localRotation = Quaternion.Euler(0, ArcYaw(i, clock), 0);
                }
            }

            public void Clear()
            {
                if (root) UnityEngine.Object.Destroy(root);
                foreach (var m in meshes) if (m) UnityEngine.Object.Destroy(m);
                foreach (var m in mats) if (m) UnityEngine.Object.Destroy(m);
                meshes.Clear(); mats.Clear();
            }
        }
    }
}
