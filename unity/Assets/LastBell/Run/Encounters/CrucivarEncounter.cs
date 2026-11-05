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
        public override string Lesson => "Carry your brand out, break the castings, fill the moulds";
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
        // Ready-screen warm-up (BossEntry.WarmUp): the pour and turret numerals are the first TextMeshes of the fight, and
        // loading the built-in font then cost a ~40 ms frame at ~9 s. Load it and rasterise every numeral glyph here instead,
        // and build the Mould Towers kits (pooled) so Phase 3's first towers build nothing in their tick.
        public static void WarmUp(CombatWorld _)
        {
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf").RequestCharactersInTexture("IVX", 64, FontStyle.Bold);
            MouldPressFx.WarmUp();
        }
        void Awake() { MaxHp = V4Hp; BodyRadius = 1.7f; HitHeight = 2.0f; MarkHeight = 4.6f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "Crucivar", 3f, 0.8f, 0, CrucivarRig.BuildPlaceholder);
            rig = presenter.Visual.gameObject.AddComponent<CrucivarRig>();
            rig.Bind(presenter.Visual);
            presenter.PlayOnce("Intro", 2f);   // the run's 2 s intro (RunDirector.IntroTicks): rises from the gantry crouch, one hammer beat
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
                int want = Mathf.Min(PhaseFor(), phase + 1);   // one phase per loop: a fast party that drops past 30 % in Phase 1 still gets the Foundry Uprising
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

        // ---------------- LAN ----------------
        // Host: the uprising turrets and castings and the crown turret travel as props (their rise and sink are the host's
        // positions); the glow and the phase as Look numbers; the CrucivarFx / BellowsDrawFx effects, the rig's Pour and
        // VentsAll, the pour numerals and the Mould Tower overlay as recorded calls. Client replica (never stepped): the rig
        // follows the glow (heat), the phase (insulation, fins) and defeat (shutdown), and the effects replay here.
        internal static bool NetOn => NetRecording;
        internal static void Net(string method, params object[] args) => NetFx(method, args);
        internal static int NewNetId() => NextNetPropId--;
        const byte PropTurret = 10, PropCasting = 20, PropCrownTurret = 30;

        public override void CaptureNetProps(List<Networking.LanWorldFrame.Prop> into)
        {
            if (Uprising != null)
                foreach (var tu in Uprising.Turrets)
                {
                    byte k = (byte)Mathf.Clamp(tu.K, 0, 3);
                    if (tu.Look) into.Add(NetProp(tu.NetTurretId, (byte)(PropTurret + k), tu.Look.position));
                    if (tu.CastLook) into.Add(NetProp(tu.NetCastId, (byte)(PropCasting + k), tu.CastLook.position));
                }
            if (Crown != null && Crown.TurretView) into.Add(NetProp(Crown.TurretNet, PropCrownTurret, Crown.TurretView.position));
        }
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(phase);   // [0] phase 1-3
        }
        public override Transform MakeNetProp(byte kind)
        {
            var parent = transform.parent;
            if (kind >= PropTurret && kind < PropTurret + 4)
            { NetPropsMade++; return FoundryUprising.TurretLook(parent, FoundryUprising.Numeral(kind - PropTurret), "Lava turret (LAN)"); }
            if (kind >= PropCasting && kind < PropCasting + 4)
            {
                NetPropsMade++;
                var cast = FoundryUprising.CastingLook(parent, FoundryUprising.Numeral(kind - PropCasting), "Molten Casting (LAN)");
                var chain = FoundryUprising.ChainLook(cast, "Casting chain (LAN)");
                cast.gameObject.AddComponent<NetChain>().Line = chain;
                return cast;
            }
            if (kind == PropCrownTurret) { NetPropsMade++; return FoundryUprising.TurretLook(parent, null, "Crown turret (LAN)"); }
            return null;
        }
        // a client's casting view: the chain runs from the casting to its turret (across the arena on the rim)
        sealed class NetChain : MonoBehaviour
        {
            public LineRenderer Line;
            void LateUpdate()
            {
                if (!Line) return;
                var at = transform.position;
                Line.SetPosition(0, FoundryUprising.TurretFor(at) + Vector3.up * 0.4f); Line.SetPosition(1, new Vector3(at.x, 0.4f, at.z));
            }
        }

        // client-side state and counters (two-process smoke tests read these)
        public int NetFrames { get; private set; }
        public int NetPropsMade { get; private set; }
        public int NetEffects { get; private set; }          // recorded Crucivar calls replayed on this replica
        public int NetMouldSets { get; private set; }        // Mould Tower overlays built from stand-ins
        public int NetPourNumerals { get; private set; }
        public int NetMissed { get; private set; }           // a recorded call whose stand-in was not found
        int replicaPhase = 1; bool replicaDown;
        readonly HashSet<Hazard> replicaMoulds = new HashSet<Hazard>();

        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            NetFrames++;
            if (presenter) presenter.GlowLevel = state.Glow;
            if (rig) rig.Heat = state.Glow;
            int ph = state.Look.Count > 0 ? Mathf.Clamp(Mathf.RoundToInt(state.Look[0]), 1, 3) : replicaPhase;
            if (ph > replicaPhase) { replicaPhase = ph; phase = ph; SecondForm = ph >= 2; if (rig) rig.BeginPhase(ph); }
            if (Dead && !replicaDown) { replicaDown = true; if (rig) rig.Shutdown(); }
            if (replicaMoulds.Count > 32) replicaMoulds.RemoveWhere(h => h.Cancelled);
        }

        public override bool ReplayNetEffect(string method, Networking.LanWorldFrame.EffectArg[] a, System.Func<int, PlayerController> player)
        {
            var fx = W != null ? W.BossFx : null;
            bool ok = true;
            switch (method)
            {
                case "CrucivarFx.DiebreakerResolve": if (fx == null || a.Length < 2) return false; fx.DiebreakerResolve(NetV(a[0]), NetF(a[1])); break;
                case "CrucivarFx.SlagSplash": if (fx == null || a.Length < 2) return false; fx.SlagSplash(NetV(a[0]), NetF(a[1])); break;
                case "CrucivarFx.StripStamp": if (fx == null || a.Length < 4) return false; fx.StripStamp(NetV(a[0]), NetF(a[1]), NetF(a[2]), NetF(a[3])); break;
                case "CrucivarFx.FlameJet":   // the host records every second call: play it twice
                    if (fx == null || a.Length < 4) return false;
                    fx.FlameJet(NetV(a[0]), NetF(a[1]), NetF(a[2]), NetF(a[3])); fx.FlameJet(NetV(a[0]), NetF(a[1]), NetF(a[2]), NetF(a[3])); break;
                case "CrucivarFx.RimFlood": if (fx == null || a.Length < 2) return false; fx.RimFlood(NetF(a[0]), NetF(a[1])); break;
                case "CrucivarFx.Eruption": if (fx == null || a.Length < 1) return false; fx.Eruption(NetF(a[0])); break;
                case "CrucivarFx.InhaleChevrons": if (fx == null || a.Length < 3) return false; fx.InhaleChevrons(NetV(a[0]), NetF(a[1]), NetF(a[2])); break;
                case "CrucivarFx.BellowsBlast": if (fx == null || a.Length < 2) return false; fx.BellowsBlast(NetV(a[0]), NetF(a[1])); break;
                case "CrucivarRig.Pour": if (!rig || a.Length < 1) return false; rig.Pour(NetB(a[0])); break;
                case "CrucivarRig.VentsAll": if (!rig || a.Length < 1) return false; rig.VentsAll(NetB(a[0])); break;
                case "Crucivar.PourNumeral":
                {
                    if (a.Length < 3 || W == null) return false;
                    var who = NetP(a[0], player); var pour = ReplicaHazard(NetS(a[2]));
                    if (!who || pour == null) { NetMissed++; return true; }
                    ForgeRiteNumeral.Show(W, pour, who, Mathf.Clamp(NetI(a[1]), 1, 4)); NetPourNumerals++;
                    break;
                }
                case "Crucivar.MouldTowers":
                {
                    if (a.Length < 2 || W == null) return false;
                    string source = NetS(a[0]); var list = new List<Hazard>();
                    var live = W.Hazards.Live;
                    for (int i = 0; i < live.Count; i++)
                    {
                        var h = live[i];
                        if (h != null && h.Replica && !h.Cancelled && h.Source == source && !replicaMoulds.Contains(h)) list.Add(h);
                    }
                    if (list.Count == 0) { NetMissed++; return true; }
                    foreach (var h in list) replicaMoulds.Add(h);
                    MouldPressFx.Show(W, list, false).HideCounts = NetB(a[1]); NetMouldSets++;
                    break;
                }
                default: ok = false; break;
            }
            if (ok) NetEffects++;
            return ok;
        }
        // the newest live stand-in with this source (one that has not landed yet first)
        Hazard ReplicaHazard(string source)
        {
            Hazard best = null; var live = W.Hazards.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h == null || !h.Replica || h.Cancelled || h.Source != source) continue;
                if (best == null || (h.Age < h.ResolveAt && best.Age >= best.ResolveAt)) best = h;
            }
            return best;
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
