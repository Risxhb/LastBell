using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Ability set v4, Phase 3 "Molten Crown" (proposal, abilities-v4/P3-MOLTEN-CROWN.md, the harder cut Chzy18 asked for on
    // 2026-10-28: every layer of the browser mock-up on). One Run is one cycle of 21.5 s; the encounter adds the 3.5 s punish.
    //   0.0  Mould Towers set A rise (5.0 s, count over each); set B rises 1.6 s later on the opposite side. Only who is
    //        inside when a tower lands counts: too few = Molten Overflow (party wipe), extras farthest out take 50. Every
    //        landed tower leaves slag (20/s) for the phase. Even cycles hide the counts ("?"): remember the last cycle's.
    //        Solo: three one-player towers 1.6 s apart.
    //  B+3.4 Casting Order, after the towers and Backdraft (was 3.5 s, through the soakers; moved 11-01)
    //  B+0.2 Backdraft right after the last set lands (pull, then the 5 m blast)
    //  9.5  Ember Brand (pools last the phase)
    // 10.0  Crown Rise: the rim from the new edge out turns to molten crown (50/s) for good, 3.0 s warning:
    //        13.7 -> 11 -> 9 -> 7 m. The next crown is Furnace Eruption (5 s warning, the whole floor: party wipe).
    // 13.5  Furnace Spin (from the second cycle)    18.0  Anvil Quake
    // Hard (Second Press returns): 2.0 s after each set lands, the hammer re-stamps its fullest tower (r 2.2, 35, knocked
    // toward the centre), so whoever just soaked it has to step out.
    // all phase: one lava turret on the rim fires a Lane Stream every 4 s; heat adds 15 % to every hit and burn per crown.
    // Hard (Crown Ring): the turret's volleys alternate Lane Stream and Ring Wave (one wave, the Phase 2 gaps and 2.0 s path).
    public sealed class MoltenCrown : CrucivarAbility
    {
        public override string Id => "crucivar.molten_crown";

        public const float Cycle = 21.5f, Rise = 5f, SetGap = 1.6f, TowerRadius = 2f, ExtraDmg = 50, CrownWarn = 3f, EruptWarn = 5f;
        public const float CrownBurn = 50f, SlagBurn = 20f, HeatPerCrown = 0.15f, TurretEvery = 4f;
        public const float PressDelay = 2.0f, PressRadius = 2.2f, PressKnock = 1.5f;
        public const int PressDamage = 35;   // Hard: Second Press (the v2 mechanic, 35 since v3)
        public const float PressStun = 0.25f;
        public static readonly float[] Edges = { 13.7f, 11f, 9f, 7f, 6f };

        public sealed class Tower { public Hazard H; public int Need, Set; }
        public readonly List<Tower> Towers = new List<Tower>();   // pending ones, for the watch bots and tests
        public int Cycles { get; private set; }
        public int Crowns { get; private set; }
        public int Wipes { get; private set; }
        public float Edge => Edges[Crowns];
        public float Heat => 1f + HeatPerCrown * Crowns;
        public bool CountsHidden { get; private set; }
        public int Presses { get; private set; }   // Hard: Second Press re-stamps this phase
        public int Rings { get; private set; }     // Hard: Crown Ring volleys this phase
        readonly Dictionary<int, (Hazard h, int inside)> fullest = new Dictionary<int, (Hazard, int)>();   // set -> fullest landed tower
        int Limit => Easy ? 4 : 3;   // the crown after this many is the eruption
        FoundryUprising.Turret turret; Transform turretLook; bool hooked, running, wiping;
        public Transform TurretView => turretLook;                              // LAN: the crown turret travels as a prop
        public readonly int TurretNet = CrucivarEncounter.NewNetId();

        public static int[][] Sets(int n)
        {
            if (n <= 1) return new[] { new[] { 1 }, new[] { 1 }, new[] { 1 } };
            int[] Split(int total, int parts) { var a = new int[parts]; for (int i = 0; i < total; i++) a[i % parts]++; return a.Where(x => x > 0).ToArray(); }
            return new[] { Split(n, 2), Split(n, 2) };   // 1-4 players (Chzy18 11-01): two towers per set
        }

        public IEnumerable<int> Run()
        {
            Cycles++;
            CountsHidden = Cycles % 2 == 0;
            if (!hooked) { hooked = true; W.Hazards.Spawned += ApplyHeat; }
            if (turret == null) BuildTurret();
            var sets = Sets(Mathf.Max(1, Standing().Count));
            float lastLand = Rise + SetGap * (sets.Length - 1);
            IEnumerator<int> draft = null; int draftWait = 0;
            int end = Sim.Ticks(Cycle);
            running = true; wiping = false; fullest.Clear();
            for (int t = 0; t < end; t++)
            {
                if (!Boss) { running = false; yield break; }   // the encounter is gone (a restart)
                for (int s = 0; s < sets.Length; s++)
                {
                    if (t == Sim.Ticks(SetGap * s)) RiseSet(sets[s], s, sets[0].Length);
                    if (Hard && t == Sim.Ticks(SetGap * s) + T(Rise) + 2 && fullest.TryGetValue(s, out var f)) Press(f.h.Origin);   // the set has landed
                }
                // after the towers and Backdraft (11-01): at 3.5 s the pours locked onto set B soakers who had to stay put
                if (t == Sim.Ticks(lastLand + 3.4f)) { Boss.SetCurrent("Casting Order"); Boss.Rite.CastingOrder(); }
                CoolTowerSpots();
                if (t == Sim.Ticks(lastLand + 0.2f)) { Boss.SetCurrent("Backdraft"); draft = Boss.Rite.Backdraft().GetEnumerator(); draftWait = 0; }
                if (draft != null && --draftWait <= 0) { if (draft.MoveNext()) draftWait = draft.Current; else draft = null; }
                if (t == Sim.Ticks(9.5f)) { Boss.SetCurrent("Ember Brand"); Boss.Rite.EmberBrand(); }
                if (t == Sim.Ticks(10f)) { if (Crowns < Limit) CrownRise(); else Eruption(); }
                if (Cycles > 1 && t == Sim.Ticks(13.5f)) { Boss.SetCurrent("Furnace Spin"); Boss.Rite.FurnaceSpin(); }
                if (t == Sim.Ticks(18f)) { Boss.SetCurrent("Anvil Quake"); Boss.Rite.AnvilQuake(); }
                if (t % Sim.Ticks(TurretEvery) == Sim.Ticks(2f) % Sim.Ticks(TurretEvery))
                {
                    // Hard: Crown Ring (proposal, 5 Nov), every other volley is a Ring Wave through its two gaps
                    if (Hard && turret.Shots % 2 == 1) { Boss.Uprising.FireRing(turret); Rings++; } else Boss.Uprising.FireLane(turret);
                }
                if (t % Sim.Hz == 0) Boss.Label($"Molten Crown {Cycles}  ·  edge {Edge:0.#} m  ·  heat x{Heat:0.00}" + (Crowns >= Limit ? "  ·  ERUPTION NEXT" : ""));
                Towers.RemoveAll(x => !W.Hazards.Live.Contains(x.H));
                yield return 1;
            }
            running = false;
        }

        // ---------------- Mould Towers ----------------
        // set B sits halfway between set A's towers (180 / A's count off it): a straight 180 put an even count back on A's
        // spots, so the A soakers stood still for B (escape check 31 Oct); solo keeps its own 120 step
        void RiseSet(int[] needs, int set, int countA)
        {
            Boss.SetCurrent("Mould Towers");
            Boss.Presenter?.Action("MouldPress", T(Rise));   // visual only: the press comes down as the towers land
            bool solo = Standing().Count <= 1;
            float rad = Mathf.Min(5.5f, Edge - 2.6f);
            var list = new List<Hazard>();
            for (int k = 0; k < needs.Length; k++)
            {
                float yaw = k * 360f / needs.Length + (solo ? set * 300f : set * 180f / countA) + 17f + Cycles * 40f;
                int need = needs[k];
                var h = new Hazard
                {
                    Source = "Mould Tower " + (set == 0 ? "A" : set == 1 ? "B" : "C"), AttackId = "crucivar.mould_towers", Shape = HazardShape.Circle, Kind = HazardKind.Ground,
                    Origin = Sim.Dir(yaw) * rad, Radius = TowerRadius, LockAt = 0, ResolveAt = T(Rise), ShareCount = need, Knockback = 0,
                    Soak = true,   // a ring to stand in: AllyBot must not step out of it (Run loop and shop smoke run, 11-01)
                };
                h.Includes = p => h.Age < h.ResolveAt && h.Contains(p.transform.position);   // counts for the mould look; the landing is custom
                h.OnResolve = hz => Land(hz, need, set);
                W.Hazards.Spawn(h);
                Towers.Add(new Tower { H = h, Need = need, Set = set });
                list.Add(h);
            }
            var fx = MouldPressFx.Show(W, list, false);
            fx.HideCounts = CountsHidden;
            if (CrucivarEncounter.NetOn && list.Count > 0) CrucivarEncounter.Net("Crucivar.MouldTowers", list[0].Source, CountsHidden);   // the client builds its own from the stand-ins
            Boss.Rig.Pour(true);
        }

        // a rising mould cools any burning ground it touches (old slag, brand pools, pour channels), so soaking never means
        // standing in burn (Chzy18 11-01: no unavoidable damage)
        public int Cooled { get; private set; }
        void CoolTowerSpots()
        {
            foreach (var tw in Towers)
            {
                if (!W.Hazards.Live.Contains(tw.H)) continue;
                foreach (var b in W.Hazards.Live)
                {
                    if (b.BurnPerSecond <= 0 || b.Source == "Molten Crown" || b.ResolveAt <= b.Age + 1) continue;
                    bool touches = b.Contains(tw.H.Origin);
                    for (int k = 0; k < 8 && !touches; k++) touches = b.Contains(tw.H.Origin + Sim.Dir(k * 45f) * (TowerRadius + 0.5f));
                    if (touches) { b.ResolveAt = b.Age + 1; Cooled++; }
                }
            }
        }

        void Land(Hazard h, int need, int set)
        {
            var inside = Standing().Where(p => h.Contains(p.transform.position)).ToList();
            if (!fullest.TryGetValue(set, out var f) || inside.Count > f.inside) fullest[set] = (h, inside.Count);
            W.BossFx.SlagSplash(h.Origin, h.Radius);
            if (inside.Count < need) { Wipe("Molten Overflow"); return; }
            foreach (var p in inside.OrderByDescending(p => Flat(p.transform.position).magnitude).Take(inside.Count - need)) p.ReceiveHit(h, Mathf.RoundToInt(ExtraDmg * Heat));
            Boss.Rite.Pools.Add(W.Hazards.Spawn(new Hazard
            {
                Source = "Tower Slag", AttackId = "crucivar.tower_slag", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = h.Origin, Radius = TowerRadius,
                LockAt = T(ForgeRite.PoolArm), ResolveAt = Sim.Ticks(3600), BurnPerSecond = SlagBurn,   // heats first: the soakers step off
            }));
        }

        void Wipe(string why)
        {
            if (wiping) return;   // two short towers landing together are one wipe
            wiping = true; Wipes++;
            W.Hazards.Spawn(new Hazard
            {
                Source = why, AttackId = "crucivar.molten_overflow", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Vector3.zero, Radius = Far + 1f,
                LockAt = 0, ResolveAt = 1, ShareTotal = 1, WipeIfEmpty = true, Includes = _ => false,
            });
        }

        // ---------------- Hard: Second Press ----------------
        void Press(Vector3 centre)
        {
            Boss.SetCurrent("Second Press"); Presses++;
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = "Second Press", AttackId = "crucivar.second_press", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = Flat(centre), Radius = PressRadius,
                LockAt = 0, ResolveAt = T(PressDelay), Damage = PressDamage, Knockback = PressKnock, Stun = Sim.Ticks(PressStun),
                Push = p => -Flat(p),   // toward the centre, away from the crown
            });
            h.OnResolve = hz => { W.BossFx.DiebreakerResolve(hz.Origin, hz.Radius); Boss.Flare(1.2f); Boss.Rig?.HammerLand(); };
        }

        // ---------------- Crown Rise / Furnace Eruption ----------------
        void CrownRise()
        {
            Boss.SetCurrent("Crown Rise");
            Boss.Presenter?.Action("CrownRise", T(CrownWarn));
            float from = Edge, to = Edges[Crowns + 1];
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = "Crown Rise", AttackId = "crucivar.molten_crown", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Vector3.zero, Inner = to, Radius = from + Hazard.Margin,
                LockAt = 0, ResolveAt = T(CrownWarn), Damage = 0, Includes = _ => false,
            });
            h.OnResolve = _ =>
            {
                Crowns++;
                W.BossFx.BellowsBlast(Vector3.zero, to);
                W.Hazards.Spawn(new Hazard
                {
                    Source = "Molten Crown", AttackId = "crucivar.molten_crown", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Vector3.zero, Inner = to, Radius = Far + 1f,
                    LockAt = 0, ResolveAt = Sim.Ticks(3600), BurnPerSecond = CrownBurn,
                });
            };
        }

        void Eruption()
        {
            Boss.SetCurrent("Furnace Eruption");
            Boss.Presenter?.Action("Eruption", T(EruptWarn));
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = "Furnace Eruption", AttackId = "crucivar.furnace_eruption", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Vector3.zero, Radius = Far + 1f,
                LockAt = 0, ResolveAt = T(EruptWarn), ShareTotal = 1, WipeIfEmpty = true, Includes = _ => false,
            });
            h.OnResolve = _ => { Wipes++; W.BossFx.Eruption(Rim); };
        }

        // ---------------- heat and the lone turret ----------------
        void ApplyHeat(Hazard h)
        {
            if (!running || !Boss || !Boss.Solid || Crowns == 0) return;   // only while a Molten Crown cycle runs
            if (h.Damage > 0) h.Damage = Mathf.RoundToInt(h.Damage * Heat);
            if (h.BurnPerSecond > 0.001f) h.BurnPerSecond *= Heat;
        }

        void BuildTurret()
        {
            var pos = Sim.Dir(200f) * (Rim + 0.4f);
            turret = Boss.Uprising.LoneTurret(pos);
            turretLook = FoundryUprising.TurretLook(W.Root, null, "Crown turret"); turretLook.position = pos;
            var part = new BossPart { Name = "crown turret look", Alive = false, OnCleared = _ => { if (turretLook) Object.Destroy(turretLook.gameObject); } };
            W.Parts.Add(part);   // not hittable; rides the parts list so the look goes with the encounter's transient state
        }
    }
}
