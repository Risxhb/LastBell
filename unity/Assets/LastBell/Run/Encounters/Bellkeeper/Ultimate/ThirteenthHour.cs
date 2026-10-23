using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Thirteenth Hour (`bellkeeper.ult_thirteenth_hour`), Bellkeeper Ultimate — proposal, built 2026-10-23, not
    // human-approved. Design: ultimate/ULTIMATE.md §3.7, §5.7. P5's hard enrage: the great bell beyond the north rim counts
    // I..XIII on the void sky, one toll every 6 s; I..XII are 6-damage raidwides (UltShapes.Raidwide), and XIII ends the
    // attempt (Ult.Wipe("The Thirteenth Hour"): lethal to every standing player).
    // Cue: P5 start (plan). At[0] = XIII, seconds after launch (78 = PushSeconds[P5]); toll k rings at k/13 of it, so a
    // shortened enrage (tests) keeps the count. XIII lands exactly on TickAt(At[0]), 0.5 s before the plan's fallback wipe.
    // Runs until XIII or the boss dies (the encounter cancels it; Count stays where the bell stopped). Same for any N.
    // Look: brass pips on an arc above the great bell light ivory one per toll (stand-in for the sky numerals; the HUD's
    // "XIII in Ns" and Numeral carry the count). Coral only on the live damage: the pips never hurt.
    public sealed class ThirteenthHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ThirteenthHour;
        public override string Name => "The Thirteenth Hour";

        public const string Source = "The Thirteenth Hour";
        public const int Hours = 13, TollDamage = 6;
        public const float DefaultEnrage = 78f;

        public int Count { get; private set; }                         // tolls rung so far: 0 .. 13 (HUD)
        public string Numeral => UltimateState.Roman[Mathf.Clamp(Count, 0, Hours)];
        public int EnrageTick => Running || LastCastTick >= 0 ? TickAt(Cue.At0(DefaultEnrage)) : -1;
        public int TollTick(int hour) => TickAt(Cue.At0(DefaultEnrage) * hour / Hours);   // hour 1..13
        SkyCount sky;

        public override UltCue DefaultCue() => new UltCue { Phase = 5, At = new[] { DefaultEnrage } };

        public override IEnumerable<int> Cast()
        {
            Count = 0;
            sky?.Clear(); sky = new SkyCount(W);
            for (int hour = 1; hour <= Hours; hour++)
            {
                int due = TollTick(hour);
                while (Now < due) yield return 1;
                Count = hour; sky?.Set(hour);
                if (Boss.Presenter && hour >= Hours - 1) Boss.Presenter.PlayOnce("CallTheBell", 1f);
                if (hour < Hours)
                {
                    var h = Spawn(UltShapes.Raidwide(Source, UltShapes.ResolveIn(0), TollDamage));   // resolves on this tick
                    h.OnResolve = hz => GreatBellFx.Toll(W, 0.8f);
                    continue;
                }
                // XIII: the hard enrage, exactly on its tick
                Boss.ShowAttack(Name + " · XIII");
                GreatBellFx.Toll(W, 2f);
                Ult.Wipe(Source);
            }
            yield return 1;
        }

        // the next toll costs 6: stay above it (the bot drinks if something else left it at 6 or less)
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || Count >= Hours - 1) return;
            int t = TollTick(Count + 1);
            into.Add(new BotDemand { Tick = t, Since = t - 6 * Sim.Hz, MinHp = TollDamage + 1, Hint = p.transform.position, Why = Source + " " + UltimateState.Roman[Count + 1], From = this });
        }

        public override void Cancel()
        {
            sky?.Clear(); sky = null;
            base.Cancel();
        }

        // Thirteen brass pips on an arc in the void above the great bell; each toll lights one ivory.
        sealed class SkyCount
        {
            static readonly Color Brass = new Color(0.82f, 0.64f, 0.3f), Ivory = new Color(0.97f, 0.94f, 0.85f);
            readonly GameObject root;
            readonly Renderer[] pips = new Renderer[Hours];
            readonly Material dim, lit, last;
            public SkyCount(CombatWorld w)
            {
                root = new GameObject("Thirteenth Hour sky count (stand-in)");
                if (w.Root) root.transform.SetParent(w.Root, false);
                root.transform.position = GreatBellFx.BellPosition + new Vector3(0, 9f, -2f);
                dim = Prim.Material(Brass * 0.6f, 0.2f); lit = Prim.Material(Ivory, 2.2f); last = Prim.Material(Ivory, 4f);
                for (int i = 0; i < Hours; i++)
                {
                    float a = Mathf.Lerp(-72f, 72f, i / (float)(Hours - 1)) * Mathf.Deg2Rad;   // I on the left, XIII on the right
                    var pos = new Vector3(-Mathf.Sin(a) * 9f, Mathf.Cos(a) * 4f, 0);         // east is −X: I sits west, as read from the arena
                    float s = i == Hours - 1 ? 1.6f : 0.9f;
                    var t = Prim.Make(PrimitiveKind.Cylinder, root.transform, pos, new Vector3(s, 0.12f, s), Brass, 0.2f, Quaternion.Euler(90, 0, 0));
                    pips[i] = t.GetComponent<Renderer>();
                    if (pips[i]) { Object.Destroy(pips[i].sharedMaterial); pips[i].sharedMaterial = dim; pips[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                }
            }
            public void Set(int count)
            {
                for (int i = 0; i < Hours; i++) if (pips[i]) pips[i].sharedMaterial = i < count ? (i == Hours - 1 ? last : lit) : dim;
            }
            public void Clear()
            {
                if (root) Object.Destroy(root);
                foreach (var m in new[] { dim, lit, last }) if (m) Object.Destroy(m);
            }
        }
    }
}
