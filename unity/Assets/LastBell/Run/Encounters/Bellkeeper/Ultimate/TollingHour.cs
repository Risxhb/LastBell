using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Tolling Hour (`bellkeeper.ult_tolling_hour`), Bellkeeper Ultimate — proposal, built 2026-10-23, not human-approved.
    // Design: ultimate/ULTIMATE.md §3.7, §5. The monumental cracked bell behind the north rim (LB_ENV_FinalToll_BrokenBell)
    // tolls: each toll is a full-arena shock and a fixed, unavoidable 12-damage raidwide (UltShapes.Raidwide). It is the
    // healing check: potions (5 x +40 for the whole fight) are the only guaranteed healing, so a chain of n tolls needs
    // 12 n + 1 HP (T2's six: 73), drunk before or between the tolls.
    // Cue: Variant "tolls": one raidwide per At entry (seconds after launch; each resolves exactly on its tick; 1.0 s apart
    // in the plan: P1 0, 1 · P3 0, 1, 2 · T2 0 .. 5 from its launch at T2 + 3). The boss turns to the great bell and calls it
    // (clip CallTheBell, falling back to HollowToll). Same for any N: raidwides don't scale.
    public sealed class TollingHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.TollingHour;
        public override string Name => "Tolling Hour";

        public const string Source = "Tolling Hour";
        public const int TollDamage = UltShapes.TollDamage;    // 12
        public const float T2TollsAt = 3f;                     // the plan opens T2 with the six tolls at 3 s (BellkeeperUltimate.T2)
        public const int T2Tolls = 6;
        const float BellYaw = 180;                             // the great bell stands beyond the north rim (−Z)

        public int Tolled { get; private set; }                // tolls rung in this cast (tests)

        public override UltCue DefaultCue() => new UltCue { Variant = "tolls", At = new[] { 0f, 1, 2, 3, 4, 5 } };

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            Tolled = 0;
            var at = Cue.At ?? new float[0];
            float last = at.Length > 0 ? at[at.Length - 1] : 0;
            if (Boss.Presenter && !Boss.Presenter.PlayOnce("CallTheBell", last + 1f)) Boss.Presenter.PlayOnce("HollowToll", last + 1f);
            for (int k = 0; k < at.Length; k++)
            {
                int due = TickAt(at[k]);
                while (Now < due) { Boss.Face(BellYaw, 240); Boss.Glow(0.6f); yield return 1; }
                // resolves on this tick (a hazard ages on its spawn tick)
                var h = Spawn(UltShapes.Raidwide(Source, UltShapes.ResolveIn(0), TollDamage));
                int n = ++Tolled;
                h.OnResolve = hz => { GreatBellFx.Toll(W, n == at.Length ? 1.4f : 1f); Boss.Flare(1.2f); };
            }
            yield return 1;
        }

        // The guide (GUIDE.md "Potions are the healing"): be at 12 x (tolls still to come) + 1 HP before each toll; the bot
        // drinks. T2's chain is known from the moment T2 begins ("potion to 73+"), so its rule shows from the segment start.
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (Running)
            {
                var at = Cue.At; if (at == null) return;
                for (int k = 0; k < at.Length; k++)
                {
                    int t = TickAt(at[k]); if (t < Now) continue;
                    into.Add(new BotDemand { Tick = t, Since = LaunchTick, MinHp = TollDamage * (at.Length - k) + 1, Hint = p.transform.position, Why = $"{Source}: {at.Length - k} tolls", From = this });
                    break;
                }
                return;
            }
            if (Boss.Set == BellkeeperEncounter.AbilitySet.Ultimate && Boss.UltPlanActive && Boss.Segment == BellkeeperEncounter.UltSegment.T2 && Boss.SegmentStartTick >= 0)
            {
                int t = Boss.SegmentStartTick + Sim.Ticks(T2TollsAt);
                if (t >= Now && LastCastTick < Boss.SegmentStartTick)
                    into.Add(new BotDemand { Tick = t, Since = Boss.SegmentStartTick, MinHp = TollDamage * T2Tolls + 1, Hint = p.transform.position, Why = "T2: potion to 73+", From = this });
            }
        }

        // -lastbellAbility ult_tolling_hour: the T2 chain (six tolls, 72), then 10 s to drink before the next chain.
        public override IEnumerable<int> Practice()
        {
            Boss.Launch(this);
            while (Running) yield return 1;
            yield return 600;
        }
    }

    // Stand-in presentation of the great bell (no art yet; no arena edits): a warm light pulse at the broken bell beyond the
    // north rim, a shock-front across the floor, and a short camera shake. Shared by the Tolling Hour and the Thirteenth Hour.
    public static class GreatBellFx
    {
        static readonly Vector3 FallbackBell = new Vector3(0, 6f, -16.5f);
        public static Vector3 BellPosition
        {
            get
            {
                var go = GameObject.Find("LB_ENV_FinalToll_BrokenBell");
                if (!go) return FallbackBell;
                var body = go.transform.Find("Bell");
                return (body ? body : go.transform).position + Vector3.up * 2.5f;
            }
        }

        public static void Toll(CombatWorld w, float strength = 1f)
        {
            var go = new GameObject("Great bell toll (stand-in)");
            if (w.Root) go.transform.SetParent(w.Root, false);
            go.transform.position = BellPosition;
            go.AddComponent<Pulse>().Begin(strength);
            w.BossFx.TollResolve(Vector3.zero, 0.6f, CombatWorld.PlayerBound + 1f);   // the shock crossing the floor (decorative)
            CameraShake.Shake(0.12f * strength, 0.3f);
        }

        // a point light at the bell that flares and fades over 0.8 s, then removes itself
        sealed class Pulse : MonoBehaviour
        {
            Light l; float t, peak;
            public void Begin(float strength)
            {
                l = gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.86f, 0.62f); l.range = 45f; l.shadows = LightShadows.None;
                peak = 9f * strength; l.intensity = peak;
            }
            void Update()
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(1 - t / 0.8f);
                if (l) l.intensity = peak * k * k;
                if (t >= 0.8f) Destroy(gameObject);
            }
        }
    }
}
