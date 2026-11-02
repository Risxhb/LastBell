using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Crucivar, The Furnace Heart: the Brass Foundry's area boss (bosses/2-brass-foundry-crucivar/abilities-v4/). Three
    // HP-gated phases, each its own class in Crucivar/Abilities:
    //   100-65 %  Sealed Furnace   Forge Rite loop + 3.0 s punish
    //   65-30 %   Casting Heat     Foundry Uprising -> 3.5 s punish -> Casting Order + Anvil Quake
    //   30-0 %    Open Pressure    Molten Crown cycle + 3.5 s punish, until he dies or the furnace erupts
    // The shifts play between loops (1.5 s, harmless, no heal) and clear every Ember Brand pool. The older ability sets
    // (the packet's five attacks, v2 and v3) were retired on 11-01 (Chzy18); they live in git history.
    public sealed class CrucivarEncounter : BossEncounter
    {
        public override string Id => "boss.crucivar";
        public override string DisplayName => "Crucivar, The Furnace Heart";
        public override string Lesson => "Cross behind the cooled strikes; punish the lodged hammer";
        public override bool Placeholder => !presenter || !presenter.HasModel;
        public override float NextPhaseFraction() => Hp > MaxHp * P2At ? P2At - 0.01f : P3At - 0.01f;

        const float P2At = 0.65f, P3At = 0.30f, Walk = 1.4f;
        const int ShiftTicks = 90;

        int phase = 1;
        CharacterPresenter presenter;
        CrucivarRig rig;
        public CrucivarRig Rig => rig;
        public CharacterPresenter Presenter => presenter;
        public int PhaseIndex => phase;
        public int Attacks { get; private set; }

        // abilities-v4/FIGHT-SHEET.md (accepted 10-29): 8500 solo, about 4 min at the measured bot damage, plus 0.9 per
        // extra fighter through the shared rule in BossEncounter.Spawn, so passive menu bots don't raise it (Chzy18 10-31).
        public const int V4Hp = 8500;
        public const float V4PartyStep = 0.9f;
        protected override bool PartyScaled => true;
        protected override float PartyStep => V4PartyStep;
        public static int V4MaxHp(int fighters) => Mathf.RoundToInt(V4Hp * (1 + V4PartyStep * Mathf.Max(0, fighters - 1)));
        void Awake() { MaxHp = V4Hp; BodyRadius = 1.7f; HitHeight = 2.0f; MarkHeight = 4.6f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "Crucivar", 3f, 0.8f, 0, CrucivarRig.BuildPlaceholder);
            rig = presenter.Visual.gameObject.AddComponent<CrucivarRig>();
            rig.Bind(presenter.Visual);
            PhaseName = "Sealed Furnace";
            BindAbilities();
            W.Hazards.Spawned -= FoundryLook; W.Hazards.Spawned += FoundryLook;
        }

        // look pass (Reference/Telegraph-Style 02, approved 10-23): every "get out" warning wears the Brass Foundry motif
        // (square gear teeth, rivets, molten streaks) over the shared outline, fill and flash. Not on soaks (Mould Towers keep
        // their count look), standing slag (its own molten tint) or jump bands. Only while he is the live boss.
        public const int FoundryMotif = 3;
        void FoundryLook(Hazard h)
        {
            if (!this || W == null || W.Boss != this || h.Tele == null) return;
            if (h.ShareCount > 0 || h.BurnPerSecond > 0 || h.JumpClearable) return;
            h.Tele.Motif(FoundryMotif);
        }
        void OnDestroy() { if (W != null && W.Hazards != null) W.Hazards.Spawned -= FoundryLook; }

        int PhaseFor() => Hp > MaxHp * P2At ? 1 : Hp > MaxHp * P3At ? 2 : 3;

        // practice phase row: the shift's state without its animation; the rig sheds its insulation (and opens on 3)
        public override float[] PhaseStarts => new[] { 1f, P2At, P3At };
        public override int PhaseNumber => phase;
        protected override void EnterPhase(int n)
        {
            if (phase == n) return;
            phase = n; SecondForm = n >= 2; PhaseName = n == 2 ? "Casting Heat" : "Open Pressure";
            rig.BeginPhase(n);
        }

        IEnumerable<int> PhaseShift()
        {
            Current = "phase shift";
            SetPhase(phase == 2 ? "Casting Heat" : "Open Pressure");
            presenter.PlayOnce("PhaseShift", ShiftTicks / (float)Sim.Hz);
            rig.BeginPhase(phase);
            glowTarget = 2.4f;
            for (int i = 0; i < ShiftTicks; i++) { if (i == ShiftTicks / 2) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.2f); } yield return 1; }
            glowTarget = 1;
        }

        IEnumerable<int> Recover(string name, string clip, int ticks)
        {
            Current = name; presenter.PlayOnce(clip, ticks / (float)Sim.Hz); glowTarget = 0.5f;
            yield return ticks; glowTarget = 1;
        }

        public ForgeRite Rite { get; private set; }
        public FoundryUprising Uprising { get; private set; }
        public MoltenCrown Crown { get; private set; }
        public CombatWorld World => W;
        public bool SecondForm { get; private set; }
        public int Transitions { get; private set; }
        public int PhaseLoops { get; private set; }   // loops / uprisings finished in this phase (watch pacing)
        public void SetCurrent(string name) => Current = name;
        public void Glow(float k) => Windup(k);
        public void Flare(float f = 1) => Release(f);
        public void Label(string text) => PhaseName = text;
        public const float UprisingPunish = 3.5f;

        // tests: hold the schedule and run only what TestRun hands over
        public bool TestHold;
        IEnumerable<int> testBody;
        public void TestRun(IEnumerable<int> body) => testBody = body;
        public bool TestBusy => testBody != null || testRunning;
        bool testRunning;

        void BindAbilities()
        {
            if (Rite != null) return;
            Rite = new ForgeRite(); Uprising = new FoundryUprising(); Crown = new MoltenCrown();
            foreach (var a in new CrucivarAbility[] { Rite, Uprising, Crown }) a.Bind(this, W);
        }

        protected override IEnumerable<int> Script()
        {
            BindAbilities();
            if (!TestHold) foreach (var w in WalkTo(Vector3.zero, Walk)) yield return w;   // Forge Rite is built round the arena centre
            while (true)
            {
                if (TestHold)
                {
                    if (testBody == null) { Current = "test hold"; yield return 1; continue; }
                    var body = testBody; testRunning = true;
                    foreach (var w in body) yield return w;
                    testBody = null; testRunning = false;
                    continue;
                }
                Attacks++;
                if (phase == 2)
                {
                    Current = "Foundry Uprising";
                    foreach (var w in Uprising.Run()) yield return w;
                    foreach (var w in Recover("Foundry Uprising (furnace staggered)", "Vulnerable", Sim.Ticks(UprisingPunish))) yield return w;
                    Current = "Casting Order"; Rite.CastingOrder();
                    yield return Sim.Ticks(2.5f);
                    Current = "Anvil Quake"; Rite.AnvilQuake();
                    yield return Sim.Ticks(4f);
                }
                else if (phase == 3)
                {
                    Current = "Molten Crown";
                    foreach (var w in Crown.Run()) yield return w;
                    foreach (var w in Recover("Molten Crown (furnace venting)", "Vulnerable", Sim.Ticks(UprisingPunish))) yield return w;
                }
                else
                {
                    foreach (var w in Rite.Loop()) yield return w;
                    foreach (var w in Recover("Forge Rite (vents cooling)", "Vulnerable", Sim.Ticks(ForgeRite.Punish))) yield return w;
                }
                PhaseLoops++;
                int want = PhaseFor();
                if (want != phase) { phase = want; PhaseLoops = 0; Rite.ClearPools(); foreach (var w in Shift()) yield return w; }
            }
        }

        // second form at 65 % (insulation falls, glow deepens), Open Pressure at 30 %: 1.5 s, harmless, no heal
        IEnumerable<int> Shift()
        {
            Transitions++;
            if (phase >= 2) SecondForm = true;
            foreach (var w in PhaseShift()) yield return w;
        }

        // ---------------- presentation ----------------
        protected override void StepAlways()
        {
            if (!presenter) return;
            bool walking = Current == "approach" || Current == "reposition";
            presenter.TickBoss(walking ? "Walk" : "Idle");
            rig.Walking = walking;
        }

        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel;
            if (rig) rig.Heat = glowLevel;
        }

        protected override void OnDefeat()
        {
            // heat fades white/orange to red, kneels, a harmless final pour into its own crucible, locks solid
            glowTarget = 0.05f; glowLevel = 2.5f;
            presenter.PlayOnce("Defeat", 3f);
            rig.Shutdown();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
