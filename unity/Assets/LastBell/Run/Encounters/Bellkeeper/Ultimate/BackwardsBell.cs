using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Backwards Bell (`bellkeeper.ult_backwards_bell`), Bellkeeper Ultimate — proposal. Design: ultimate/ULTIMATE.md §3.4,
    // §5.3; strat: GUIDE.md "Backwards Bell".
    //
    // Cue (set by the plan, BellkeeperUltimate.cs): Variant "block": At = A, B, C resolve times in seconds after launch
    // (P2: 3.5, 5, 6.5). A = Leaning Bell half (50), B = Heart Toll on the boss (40), C = Toll Tide (40), built from UltShapes
    // and spawned here (the v2 moves can't overlap themselves). Their warnings are the Ultimate ones (3.5 / 2.0 / 2.5 s,
    // shortened if the cue leaves less). Then the rewound copies resolve C', B', A' at C + 2.0, + 3.0, + 4.0 s, each drawn
    // coral 1.5 s before with an ivory rewind arrow, 40 each.
    //
    // HARDENED (Chzy18 2026-10-23: "add and replace abilities as you see fit"): a rewound copy strikes **everywhere its
    // original did not** (the bell un-strikes: ground it hit is now the only safe ground). With identical copies one spot
    // (the 4-7 m band on A's safe half) dodged all six strikes standing still; the complement forces the design's intent,
    // "the spot you just left is the next one struck":
    //   C' (tide reversed)       = the disc inside the tide, r < 7 m from the centre      -> be out past 7.3 m
    //   B' (Heart Toll reversed) = everything beyond 4 m of B's centre (to the wall)      -> be inside 3.7 m of B
    //   A' (Leaning reversed)    = the half A spared                                      -> be on the half A struck
    // Route (paper proof, Hazard.Margin 0.3 m counted): 14.5 -> 16.5 walk out on A's struck half to r 7.4 (2 s); 16.5 -> 17.5
    // walk in 3.6 m to r 3.6 (1.0 s at 4.2 m/s = 4.2 m, 0.6 m spare); 17.5 -> 18.5 stay on that half (0 m) or step to a clock
    // spot. The block is authored: A's half is the only free choice, so the proof holds for every block. A' leans toward
    // PendulumClocks.LeanDirAt(A' tick) (the pairing rule: the clock III+IV spots stay on its safe half), else a seeded
    // diagonal; A itself strikes the opposite half.
    //
    // Public API (the Conductor's Hand "right swing = the first move again, backwards"):
    //   Hazard RewindOf(Hazard resolvedOrShape, int resolveTick, bool invert = true, int damage = 40)
    //   Hazard RewindOf(UltimateState.Footprint footprint, int resolveTick, bool invert = true, int damage = 40)
    // Spawns, on this mechanic's behalf and right now, the rewound copy of a resolved hazard (or any hazard used only as
    // a shape, or a ledger footprint), resolving exactly on world tick resolveTick; call it 1.5 s before (the warning is
    // the time left, at least 1 tick). invert: true = the complement rule above (cone -> the other side, ring -> the disc
    // inside it, circle -> everything outside it); false = the same footprint played backwards (use it for moves with
    // several small circles, e.g. Reflection images, whose complements would cover the whole floor). Lanes always rewind
    // as the same lane. Static Rewound(...) builds the shape without spawning. Works whether or not this mechanic is running.
    //
    // Look (stand-ins): originals are the usual coral telegraphs; when each resolves its ivory dashed footprint stays
    // 0.6 s (a recorded shape). Copies: coral telegraph + an ivory curved arrow that spins counter-clockwise; on resolve a
    // coral shock ring contracts (the reverse of the toll's expanding ring). The boss's glow runs down during the
    // rewinds and he plays `ReverseToll` (new clip for the rig list: shell swings backwards, halo reverses; HollowToll
    // stands in until the rig has it).
    //
    // Threat: melee (hugging the boss) is hit by B and C'; ranged (8 m+) by C and B'. Solo and N: identical.
    // Strike / Ground: A, A' Strike; B, B', C, C' Ground.
    public sealed class BackwardsBell : UltimateMechanic
    {
        public override string Id => UltimateMechanics.BackwardsBell;
        public override string Name => "Backwards Bell";
        public override bool Built => true;

        public const int CopyWarnTicks = 90;                              // 1.5 s coral before each copy
        public const int FirstRewindTicks = 120, RewindStepTicks = 60;    // C' at C + 2.0 s, then 1.0 s apart
        public const int FootprintTicks = 36;                             // ivory footprint after each original
        public const string RewindPrefix = "Rewound ";

        public override UltCue DefaultCue() => new UltCue { Variant = "block", At = new[] { 3.5f, 5f, 6.5f }, Phase = 2 };
        // -lastbellAbility loop: the cue each practice cast uses (null = DefaultCue); tests set it to pick the lean (Seed)
        public UltCue PracticeCue;
        public override IEnumerable<int> Practice()
        {
            while (Running) yield return 1;   // never two blocks at once (e.g. a RewindOf partner launched it)
            NextCue = PracticeCue;
            foreach (var w in base.Practice()) yield return w;
        }

        // ---------------- this cast (tests read these) ----------------
        public Vector3 LeanDir { get; private set; }                      // the half A strikes (A' strikes -LeanDir)
        public Hazard A { get; private set; }
        public Hazard B { get; private set; }
        public Hazard C { get; private set; }
        public readonly Hazard[] Copies = new Hazard[3];                  // C', B', A' once spawned (resolve order)
        public readonly int[] CopyTicks = new int[3];                     // world ticks C', B', A' resolve
        public readonly int[] OriginalTicks = new int[3];                 // world ticks A, B, C resolve

        sealed class Rewind { public string Why; public int Tick, Since; public Hazard Shape; public Vector3 Hint; public Func<Vector3, bool> Ok; }
        readonly List<Rewind> pending = new List<Rewind>();
        readonly List<(int until, BellkeeperFx.Telegraph t)> footprints = new List<(int, BellkeeperFx.Telegraph)>();
        BackwardsBellFx fx;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            pending.Clear(); footprints.Clear(); A = B = C = null;
            for (int i = 0; i < 3; i++) Copies[i] = null;
            int l = LaunchTick;
            int tA = TickAt(Cue.AtOr(0, 3.5f)), tB = TickAt(Cue.AtOr(1, 5f)), tC = TickAt(Cue.AtOr(2, 6.5f));
            OriginalTicks[0] = tA; OriginalTicks[1] = tB; OriginalTicks[2] = tC;
            CopyTicks[0] = tC + FirstRewindTicks; CopyTicks[1] = CopyTicks[0] + RewindStepTicks; CopyTicks[2] = CopyTicks[1] + RewindStepTicks;
            var pair = Boss.Mechanic<PendulumClocks>()?.LeanDirAt(CopyTicks[2]);
            LeanDir = pair.HasValue && Sim.Flat(pair.Value).sqrMagnitude > 1e-4f ? -Sim.Flat(pair.Value).normalized : UltimateState.StationDir(Mathf.Abs(Cue.Seed) % 4);

            var events = new List<(int tick, Action act)>
            {
                (Mathf.Max(l, tA - UltShapes.LeanTicks), () => A = Original(UltShapes.LeaningHalf(W, LeanDir, UltShapes.ResolveIn(tA - Now)), 2, tA)),
                (Mathf.Max(l, tB - UltShapes.HeartTicks), () => B = Original(UltShapes.HeartToll(W, Boss.transform.position, UltShapes.ResolveIn(tB - Now)), 1, tB)),
                (Mathf.Max(l, tC - UltShapes.TideTicks), () => C = Original(UltShapes.Tide(W, UltShapes.ResolveIn(tC - Now)), 0, tC)),
            };
            for (int k = 0; k < 3; k++)
            {
                int slot = k;
                events.Add((Mathf.Max(l, CopyTicks[slot] - CopyWarnTicks), () => SpawnCopy(slot)));
            }
            events.Sort((x, y) => x.tick.CompareTo(y.tick));

            int end = CopyTicks[2] + 30, next = 0;
            bool reversed = false;
            while (true)
            {
                while (next < events.Count && events[next].tick <= Now) events[next++].act();
                for (int i = footprints.Count - 1; i >= 0; i--) if (Now >= footprints[i].until) { footprints[i].t.Dismiss(); footprints.RemoveAt(i); }
                if (Now >= tC && Now <= CopyTicks[2])
                {
                    // the rewind: the halo runs backwards and the crack's glow drains toward A'
                    if (!reversed)
                    {
                        reversed = true;
                        float secs = (CopyTicks[2] - Now) / (float)Sim.Hz;
                        if (Boss.Presenter && !Boss.Presenter.PlayOnce("ReverseToll", secs)) Boss.Presenter.PlayOnce("HollowToll", secs);
                    }
                    Boss.Glow(1f - (Now - tC) / (float)Mathf.Max(1, CopyTicks[2] - tC));
                }
                if (Now >= end && next >= events.Count) break;
                yield return 1;
            }
            foreach (var f in footprints) f.t.Dismiss();
            footprints.Clear(); pending.Clear();
        }

        // an original strike: spawned now, its footprint shown when it resolves, its rewound copy queued (slot 0 = C', 1 = B', 2 = A')
        Hazard Original(Hazard h, int slot, int resolveTick)
        {
            Spawn(h);
            var show = h.OnResolve;
            h.OnResolve = hz =>
            {
                show?.Invoke(hz);
                footprints.Add((W.Tick + FootprintTicks, Ult.Outline(UltimateState.Footprint.Of(hz, W.Tick))));
            };
            var shape = Rewound(h, 1, UltShapes.StandardDamage);
            var (hint, spot) = GuideFor(slot, h);
            pending.Add(new Rewind { Why = RewindPrefix + h.Source, Tick = CopyTicks[slot], Since = resolveTick, Shape = shape, Hint = hint, Ok = q => !shape.Contains(q) && spot(q) });
            if (slot == 2)
            {
                // GUIDE.md: once A has struck, be on its struck half by the time C resolves (inside the tide's safe disc)
                var lean = LeanDir;
                pending.Add(new Rewind { Why = "Backwards Bell: cross to A's half", Tick = OriginalTicks[2], Since = resolveTick, Hint = lean * 5.2f,
                                         Ok = q => Vector3.Dot(Sim.Flat(q), lean) > CrossSide && Sim.Flat(q).magnitude < TollTideAbility.TideInner - 0.6f });
            }
            return h;
        }
        public const float CrossSide = 2f;   // metres past A's line when C resolves

        // GUIDE.md's route as spots: C' = just out past the tide disc on A's struck half (7.35-7.7 m, within 50° of LeanDir);
        // B' = just inside B's circle on that half (< 3.65 m of B, 1 m past the line); A' = stay 0.8 m past the line on that
        // half (or a clock spot there). The out-and-in step is 3.7-4.1 m in 1.0 s; 4.2 m at the proof speed.
        public const float OutMin = 7.35f, OutMax = 7.7f, InMax = 3.65f, OutCone = 50f;
        (Vector3 hint, Func<Vector3, bool> ok) GuideFor(int slot, Hazard h)
        {
            var lean = LeanDir; float cone = Mathf.Cos(OutCone * Mathf.Deg2Rad);
            switch (slot)
            {
                case 0:
                    return (lean * 7.45f, q => { var f = Sim.Flat(q); float r = f.magnitude; return r > OutMin && r < OutMax && Vector3.Dot(f / r, lean) > cone; });
                case 1:
                {
                    var c = Sim.Flat(h.Origin);
                    return (c + lean * 3.5f, q => Sim.Flat(q - c).magnitude < InMax && Vector3.Dot(Sim.Flat(q), lean) > 1f);
                }
                default:
                    return (lean * 3.5f, q => Vector3.Dot(Sim.Flat(q), lean) > 0.8f);
            }
        }

        void SpawnCopy(int slot)
        {
            var orig = slot == 0 ? C : slot == 1 ? B : A;
            if (orig != null) Copies[slot] = RewindOf(orig, CopyTicks[slot]);
        }

        // GUIDE.md: A, B, C are visible coral (the bot dodges them itself); each rewind is known from its original's resolve.
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || p == null || p.Down) return;
            foreach (var r in pending)
            {
                if (r.Tick < Now) continue;
                into.Add(new BotDemand { Tick = r.Tick, Since = r.Since, Ok = r.Ok, Hint = r.Hint, Memory = true, Why = r.Why, From = this });
            }
        }

        // ---------------- the rewind (public API, see the header) ----------------
        public Hazard RewindOf(Hazard resolvedOrShape, int resolveTick, bool invert = true, int damage = UltShapes.StandardDamage)
        {
            if (resolvedOrShape == null || Boss == null) return null;
            var h = Rewound(resolvedOrShape, UltShapes.ResolveIn(Mathf.Max(0, resolveTick - Now)), damage, invert);
            Spawn(h);
            Fx.Mark(h);
            h.OnResolve = hz =>
            {
                if (hz.Shape == HazardShape.Cone) W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle);
                if (fx) fx.Contract(hz);
            };
            return h;
        }
        public Hazard RewindOf(UltimateState.Footprint footprint, int resolveTick, bool invert = true, int damage = UltShapes.StandardDamage) =>
            footprint == null ? null : RewindOf(footprint.ToHazard(footprint.Source, 1, footprint.Damage), resolveTick, invert, damage);

        // The rewound shape of h (not spawned). invert: its complement (see the header); false: the same footprint.
        public static Hazard Rewound(Hazard h, int resolveAt, int damage, bool invert = true)
        {
            var r = new Hazard
            {
                Source = RewindPrefix + h.Source, Shape = h.Shape, Kind = h.Kind, Origin = Sim.Flat(h.Origin), Yaw = h.Yaw, Radius = h.Radius,
                Inner = h.Inner, HalfAngle = h.HalfAngle, Length = h.Length, Width = h.Width, LockAt = 0, ResolveAt = resolveAt,
                Damage = damage, Knockback = -h.Knockback, Stun = h.Stun,   // the push plays backwards too
            };
            switch (h.Shape)
            {
                case HazardShape.Cone:
                    r.Knockback = h.Knockback;   // away from the bell either way
                    if (invert && h.HalfAngle < 179f) { r.Yaw = h.Yaw + 180f; r.HalfAngle = 180f - h.HalfAngle; }
                    break;
                case HazardShape.Ring:
                    if (invert && h.Inner > 0.5f) { r.Shape = HazardShape.Circle; r.Radius = h.Inner; r.Inner = 0; }
                    break;
                case HazardShape.Circle:
                    if (invert) { r.Shape = HazardShape.Ring; r.Inner = h.Radius; r.Radius = TollTideAbility.TideOuter + r.Origin.magnitude; }
                    break;
                default:   // a lane plays back along itself: the same footprint, from its far end
                    r.Origin = Sim.Flat(h.Origin) + Sim.Dir(h.Yaw) * h.Length; r.Yaw = h.Yaw + 180f; r.Knockback = h.Knockback;
                    break;
            }
            return r;
        }

        BackwardsBellFx Fx
        {
            get
            {
                if (!fx) { fx = new GameObject("Backwards Bell fx").AddComponent<BackwardsBellFx>(); fx.transform.SetParent(W.Root, false); }
                return fx;
            }
        }
        public int FxCount => fx ? fx.Count : 0;   // tests: live arrows and shock rings

        public override void Cancel()
        {
            foreach (var f in footprints) f.t?.Dismiss();
            footprints.Clear(); pending.Clear();
            A = B = C = null; for (int i = 0; i < 3; i++) Copies[i] = null;
            if (fx) UnityEngine.Object.Destroy(fx.gameObject);
            fx = null;
            base.Cancel();
        }
    }

    // Backwards Bell presentation stand-ins: ivory (friendly) rewind arrows on live coral copies, contracting coral shock
    // rings on their resolve. Per frame; owns its meshes and materials; gameplay never reads it.
    public sealed class BackwardsBellFx : MonoBehaviour
    {
        static readonly Color Ivory = new Color(0.95f, 0.92f, 0.84f), Coral = new Color(1f, 0.36f, 0.24f);
        sealed class Arrow { public Hazard H; public Transform T; }
        sealed class Shrink { public Transform T; public float t, life, from, to; }
        readonly List<Arrow> arrows = new List<Arrow>();
        readonly List<Shrink> rings = new List<Shrink>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        Material ivory, coral;
        Mesh arc, annulus;
        public int Count => arrows.Count + rings.Count;

        // an ivory curved arrow on the copy, pointing (and spinning) counter-clockwise
        public void Mark(Hazard h)
        {
            if (ivory == null)
            {
                ivory = Prim.Material(Ivory, 0.9f); coral = Prim.Material(Coral, 1.4f);
                arc = FxMeshes.Sector(0.78f, 0.95f, 70f, 24); arc.RecalculateNormals();
                annulus = FxMeshes.Annulus(0.95f, 1f, 96); annulus.RecalculateNormals();
                owned.Add(ivory); owned.Add(coral); owned.Add(arc); owned.Add(annulus);
            }
            var root = new GameObject("Rewind arrow").transform; root.SetParent(transform, false);
            root.position = MarkPoint(h) + Vector3.up * 0.09f;
            Part(root, "arc", arc, ivory, Vector3.zero, Vector3.one, Quaternion.identity);
            // the head at the arc's counter-clockwise end (-70°, the -x side seen from above), along the turn
            float a = -70f * Mathf.Deg2Rad;
            Part(root, "head", Prim.MeshOf(PrimitiveKind.Cone), ivory, new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.865f, new Vector3(0.34f, 0.32f, 0.04f), Quaternion.Euler(90, -160f, 0));
            arrows.Add(new Arrow { H = h, T = root });
        }
        static Vector3 MarkPoint(Hazard h)
        {
            switch (h.Shape)
            {
                case HazardShape.Cone: return h.Origin + Sim.Dir(h.Yaw) * Mathf.Min(5f, h.Radius * 0.5f);
                case HazardShape.Ring: return h.Origin + Vector3.back * Mathf.Min(h.Inner + 2.5f, CombatWorld.PlayerBound - 1f);
                case HazardShape.Lane: return h.Origin + Sim.Dir(h.Yaw) * (h.Length * 0.5f);
                default: return h.Origin;
            }
        }

        // the reverse of a toll's shock front: a coral ring contracting onto the copy's inner edge
        public void Contract(Hazard h)
        {
            if (coral == null) return;
            float from, to;
            switch (h.Shape)
            {
                case HazardShape.Circle: from = h.Radius; to = 0.4f; break;
                case HazardShape.Ring: from = Mathf.Min(h.Radius, h.Inner + 7f); to = h.Inner; break;
                case HazardShape.Cone: from = Mathf.Min(h.Radius, 9f); to = 0.8f; break;
                default: return;
            }
            var go = new GameObject("Rewind shock"); go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(h.Origin.x, 0.07f, h.Origin.z);
            go.AddComponent<MeshFilter>().sharedMesh = annulus;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = coral; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rings.Add(new Shrink { T = go.transform, life = 0.45f, from = from, to = to });
        }

        static void Part(Transform parent, string name, Mesh mesh, Material m, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                var a = arrows[i];
                if (a.H == null || a.H.Cancelled || a.H.Age >= a.H.ResolveAt || !a.T) { if (a.T) Destroy(a.T.gameObject); arrows.RemoveAt(i); continue; }
                a.T.Rotate(0, -140f * dt, 0, Space.World);   // backwards: counter-clockwise seen from above
            }
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i]; r.t += dt;
                if (r.t >= r.life || !r.T) { if (r.T) Destroy(r.T.gameObject); rings.RemoveAt(i); continue; }
                float u = r.t / r.life, rad = Mathf.Lerp(r.from, r.to, u * (2 - u));
                r.T.localScale = new Vector3(rad, 1, rad);
            }
        }

        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); owned.Clear(); }
    }
}
