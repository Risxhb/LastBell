using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Ability set v4, Phase 1 "Forge Rite" (proposal; Chzy18 picked it from the browser trials on 2026-10-26). One loop of
    // 20.5 s, Medium numbers; Easy stretches every warning by 1.25. Up to three things are live at once (pools included).
    //   0.0  Ember Brand        3 m brand follows each player, lands 3.5 s: 30 Ground to OTHER players inside; leaves a 2 m
    //                           slag pool (20/s) under the carrier that lasts until the phase ends (ClearPools)
    //   2.0  Furnace Spin       four flame jets to the wall (1.8 m wide) shown 1.5 s, then sweep round the boss for 4 s at 40 deg/s,
    //                           30 each, once per jet; the turn direction flips every loop
    //   5.0  Backdraft          5 m blast round the boss at 3.0 s (45, 1.5 m push); everyone pulled 2 m inward at 1.6-2.0 s
    //   8.5  Casting Order      pours I-IV (2.6 m lanes to the wall) through each numbered player, locking 1.5 s before they
    //                           land 0.5 s apart at 10.5-12.0 s, 30 each; each leaves a channel burning 20/s for 10 s.
    //                           Even loops hide the numerals and replay the same order. Solo: you are I, II and III.
    //  11.0  Anvil Quake        inner circle (0-4 m) at 1.4 s, middle band (4-9 m) at 2.2 s, outer band (9 m to the wall) at
    //                           3.0 s, 40 each, Strike, all shown at once; lands after the last pour (Chzy18 10-27 replaced
    //                           the Hammerfall cleave, which came down right after the pours and could be out-ranged)
    //  13.5  Ember Brand        again
    //  17.5  punish window      3.0 s (the encounter plays it)
    public sealed class ForgeRite : CrucivarAbility
    {
        public override string Id => "crucivar.forge_rite";

        public const float LoopLength = 20.5f, Punish = 3.0f;
        public const float BrandRadius = 3f, BrandLand = 3.5f, PoolRadius = 2f, PoolBurn = 20f;
        public const int BrandDmg = 30;
        public const float JetWidth = 1.8f, JetShow = 1.5f, JetSweep = 4f, JetTurnDeg = 40f;
        public const int JetDmg = 30;
        public const float BlastRadius = 5f, BlastAt = 3f, PullFrom = 1.6f, PullTo = 2.0f, PullDist = 2f;
        public const int BlastDmg = 45;
        public const float PourWidth = 2.6f, PourLead = 2.0f, PourGap = 0.5f, PourLockBefore = 1.5f, ChannelLife = 10f, ChannelBurn = 20f;
        public const int PourDmg = 30;
        public const float QuakeFirst = 1.4f, QuakeGap = 0.8f, QuakeInner = 4f, QuakeMiddle = 9f;
        public const int QuakeDmg = 40;

        int loops;
        public int Loops => loops;
        public readonly List<Hazard> Pools = new List<Hazard>();
        public List<PlayerController> Order { get; private set; } = new List<PlayerController>();
        public bool NumeralsShown { get; private set; }
        bool hideNext;   // shown, hidden, shown, ... per Casting Order (not per loop count, which the encounter may have advanced)

        // one loop without the punish window (the encounter adds it)
        public IEnumerable<int> Loop()
        {
            loops++;
            int t = 0;
            IEnumerable<int> Until(float s) { int at = T(s); if (at > t) { yield return at - t; t = at; } }

            Boss.SetCurrent("Ember Brand"); EmberBrand();
            foreach (var w in Until(2.0f)) yield return w;
            Boss.SetCurrent("Furnace Spin"); FurnaceSpin();
            foreach (var w in Until(5.0f)) yield return w;
            Boss.SetCurrent("Backdraft");
            foreach (var w in Backdraft()) { t += w; yield return w; }
            foreach (var w in Until(8.5f)) yield return w;
            Boss.SetCurrent("Casting Order"); CastingOrder();
            foreach (var w in Until(11.0f)) yield return w;
            Boss.SetCurrent("Anvil Quake"); AnvilQuake();
            foreach (var w in Until(13.5f)) yield return w;
            Boss.SetCurrent("Ember Brand"); EmberBrand();
            foreach (var w in Until(17.5f)) yield return w;
        }

        // ---------------- Ember Brand ----------------
        void EmberBrand()
        {
            foreach (var p in Standing())
            {
                var owner = p;
                var h = new Hazard
                {
                    Source = "Ember Brand", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Flat(p.transform.position),
                    Radius = BrandRadius, LockAt = T(BrandLand), ResolveAt = T(BrandLand), Damage = BrandDmg, Knockback = 0,
                };
                h.Track = hz => { if (!owner.Down) hz.Origin = Flat(owner.transform.position); };
                h.Includes = q => q != owner && h.Contains(q.transform.position);   // the carrier is never hit by its own brand
                h.OnResolve = hz =>
                {
                    W.BossFx.SlagSplash(hz.Origin, hz.Radius);
                    Pools.Add(W.Hazards.Spawn(new Hazard
                    {
                        Source = "Brand Pool", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = hz.Origin, Radius = PoolRadius,
                        LockAt = 0, ResolveAt = Sim.Ticks(3600), BurnPerSecond = PoolBurn,   // lasts until the phase ends
                    }));
                };
                W.Hazards.Spawn(h);
            }
        }

        // the phase ends: every brand pool cools at once
        public void ClearPools()
        {
            foreach (var p in Pools) if (W.Hazards.Live.Contains(p)) p.ResolveAt = p.Age + 1;
            Pools.Clear();
        }

        // ---------------- Furnace Spin ----------------
        void FurnaceSpin()
        {
            var origin = Flat(Boss.transform.position);
            float baseYaw = Boss.transform.eulerAngles.y + 45f, dir = loops % 2 == 1 ? 1f : -1f;
            int show = T(JetShow), sweep = Sim.Ticks(JetSweep);
            for (int k = 0; k < 4; k++)
            {
                var h = new Hazard
                {
                    Source = "Furnace Spin", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = origin, Yaw = baseYaw + 90f * k,
                    Width = JetWidth, ResolveAt = show, ActiveTicks = sweep, LockAt = show + sweep + 1,
                    Damage = JetDmg, Knockback = 1f,
                };
                ToWall(h);
                h.Track = hz => { if (hz.Age >= hz.ResolveAt) hz.Yaw += dir * JetTurnDeg * Sim.Dt; ToWall(hz); };   // turns only once it is live, always reaches the wall
                W.Hazards.Spawn(h);
            }
            Boss.Rig.VentsAll(true);
        }

        // ---------------- Backdraft ----------------
        IEnumerable<int> Backdraft()
        {
            var centre = Flat(Boss.transform.position);
            var blast = W.Hazards.Spawn(new Hazard
            {
                Source = "Backdraft", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = centre, Radius = BlastRadius,
                LockAt = 0, ResolveAt = T(BlastAt), Damage = BlastDmg, Knockback = 1.5f,
            });
            blast.OnResolve = hz => { W.BossFx.BellowsBlast(hz.Origin, hz.Radius); Boss.Flare(1.3f); Boss.Rig.VentsAll(false); };
            int from = T(PullFrom), to = T(PullTo), wave = Sim.Ticks(0.35f);
            float perTick = PullDist / Mathf.Max(1, to - from);
            for (int i = 0; i < to; i++)
            {
                if (i % wave == 0 && i >= from - Sim.Ticks(1f)) W.BossFx.InhaleChevrons(centre, PullDist, 0.7f);
                if (i >= from)
                    foreach (var p in Standing())
                    {
                        var d = Flat(centre - p.transform.position);
                        if (d.magnitude > Boss.BodyRadius + 0.4f) p.Pull(d, perTick, 1);
                    }
                yield return 1;
            }
        }

        // ---------------- Casting Order ----------------
        void CastingOrder()
        {
            var standing = Standing();
            bool memory = hideNext && Order.Count > 0;
            hideNext = !memory;
            if (!memory)
            {
                // a fixed shuffle per phase visit: rotate the party list by the loop number (readable, learnable)
                var list = standing.ToList();
                Order = list.Count == 1 ? new List<PlayerController> { list[0], list[0], list[0] }
                    : list.Skip(loops % list.Count).Concat(list.Take(loops % list.Count)).Take(4).ToList();
            }
            NumeralsShown = !memory;
            var origin = Flat(Boss.transform.position);
            for (int k = 0; k < Order.Count; k++)
            {
                var target = Order[k];
                int resolve = T(PourLead + PourGap * k), lockAt = resolve - T(PourLockBefore);
                var h = new Hazard
                {
                    Source = "Pour " + new[] { "I", "II", "III", "IV" }[k], Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = origin,
                    Width = PourWidth, LockAt = lockAt, ResolveAt = resolve, Damage = PourDmg, Knockback = 1f, Yaw = Boss.transform.eulerAngles.y,
                };
                Aim(h, target);
                h.Track = hz => { if (!target.Down) Aim(hz, target); };
                h.OnResolve = hz =>
                {
                    W.BossFx.StripStamp(hz.Origin, hz.Yaw, hz.Length, hz.Width);
                    W.Hazards.Spawn(new Hazard
                    {
                        Source = "Pour Channel", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = hz.Origin, Yaw = hz.Yaw,
                        Length = hz.Length, Width = hz.Width, LockAt = 0, ResolveAt = Sim.Ticks(ChannelLife), BurnPerSecond = ChannelBurn,
                    });
                };
                W.Hazards.Spawn(h);
                if (NumeralsShown) ForgeRiteNumeral.Show(W, h, target, k + 1);
            }
            Boss.Rig.Pour(true);
        }

        // a lane from the boss that always ends at the arena wall
        static void ToWall(Hazard h) => h.Length = GreatCasting.RayToCircle(h.Origin, Sim.Dir(h.Yaw), Rim + Hazard.Margin, out float t) ? t : Rim * 2;

        void Aim(Hazard h, PlayerController target)
        {
            var d = Flat(target.transform.position - h.Origin);
            if (d.sqrMagnitude > 0.01f) h.Yaw = Sim.YawTo(h.Origin, target.transform.position, h.Yaw);
            h.Length = GreatCasting.RayToCircle(h.Origin, Sim.Dir(h.Yaw), Rim + Hazard.Margin, out float t) ? t : Rim * 2;
        }

        // ---------------- Anvil Quake ----------------
        // three bands from the anvil out to the wall, all shown at once, landing inner -> middle -> outer: step into a band
        // that has already gone off (in toward the boss once the inner ring has landed)
        void AnvilQuake()
        {
            Boss.Rig.Pour(false);
            var origin = Flat(Boss.transform.position);
            float wall = Rim + origin.magnitude + Hazard.Margin;
            Spawn("Anvil Quake I", HazardShape.Circle, 0, QuakeInner, T(QuakeFirst));
            Spawn("Anvil Quake II", HazardShape.Ring, QuakeInner, QuakeMiddle, T(QuakeFirst + QuakeGap));
            Spawn("Anvil Quake III", HazardShape.Ring, QuakeMiddle, wall, T(QuakeFirst + 2 * QuakeGap));   // reaches the wall: no out-ranging
            Boss.Presenter.PlayOnce("Diebreaker", T(QuakeFirst) / (float)Sim.Hz);

            void Spawn(string name, HazardShape shape, float inner, float outer, int at)
            {
                var h = W.Hazards.Spawn(new Hazard
                {
                    Source = name, Shape = shape, Kind = HazardKind.Strike, Origin = origin, Inner = inner, Radius = outer,
                    LockAt = 0, ResolveAt = at, Damage = QuakeDmg, Knockback = 1f,
                });
                h.OnResolve = hz =>
                {
                    if (hz.Shape == HazardShape.Circle) W.BossFx.DiebreakerResolve(hz.Origin, hz.Radius); else W.BossFx.BellowsBlast(hz.Origin, hz.Radius);
                    Boss.Flare(1f);
                };
            }
        }
    }

    // Ivory numeral (friendly identity, never a hazard colour) over a player until their pour lands.
    public sealed class ForgeRiteNumeral : MonoBehaviour
    {
        CombatWorld w; Hazard pour; PlayerController who; int index;
        static readonly string[] Roman = { "", "I", "II", "III", "IV" };

        public static void Show(CombatWorld world, Hazard pour, PlayerController who, int index)
        {
            var go = new GameObject("Pour numeral " + index);
            go.transform.SetParent(world.Root, false);
            var fx = go.AddComponent<ForgeRiteNumeral>(); fx.w = world; fx.pour = pour; fx.who = who; fx.index = index;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.anchor = TextAnchor.MiddleCenter; text.characterSize = 0.16f; text.fontSize = 64; text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 0.95f, 0.82f);
            text.text = Roman[index];
            fx.Update();
        }

        void Update()
        {
            if (!who || !w.Hazards.Live.Contains(pour)) { Destroy(gameObject); return; }
            // solo: the three numerals stack, so only the next pour's numeral shows
            if (w.Players.Count == 1) GetComponent<MeshRenderer>().enabled = !w.Hazards.Live.Any(h => h != pour && h.Source.StartsWith("Pour ") && h.ResolveAt - h.Age < pour.ResolveAt - pour.Age);
            transform.position = who.transform.position + Vector3.up * 2.9f;
            var cam = Camera.main; if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
