using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // One frame of player intent. Pressed flags latch between render frames and are consumed by the next combat tick,
    // so a quick tap is never lost and never applied twice.
    public struct PlayerInput
    {
        public const int Slots = 5;   // 1 Basic, 2/3/4 class actions, 6 = Chimesinger's Mending Chorus
        public Vector2 Move;          // camera-relative WASD, x right / y up the screen
        public Vector3 Aim;           // cursor on the floor (world)
        public bool Sprint, Dash, Potion;
        public bool WorldMove;        // LAN remote players: Move is already world x / z (the client used its own camera)
        public bool P0, P1, P2, P3, P4;   // pressed this tick
        public bool H0, H1, H2, H3, H4;   // held
        public bool Pressed(int i) => i switch { 0 => P0, 1 => P1, 2 => P2, 3 => P3, _ => P4 };
        public bool Held(int i) => i switch { 0 => H0, 1 => H1, 2 => H2, 3 => H3, _ => H4 };
    }

    public interface IInputSource { PlayerInput Read(PlayerController who); }

    // Keyboard + mouse, the project's agreed bindings (WASD move; LMB basic only, RMB dash only, 2-5 class actions;
    // Shift sprint; no jump, Chzy18 2 Nov). Reads Input System devices directly like the existing test controllers. Gamepads are
    // not read: the user's rudder pedals register as a non-standard controller.
    public sealed class KeyboardMouseInput : IInputSource
    {
        PlayerInput latched;
        bool armed;   // gameplay presses are ignored until every action key has been released once (UI clicks can't leak)
        public static bool Blocked;   // set while a menu/panel owns the mouse

        public void Poll()
        {
            var kb = Keyboard.current; var m = Mouse.current; if (kb == null) return;
            bool lmb = m != null && m.leftButton.isPressed, rmb = m != null && m.rightButton.isPressed;
            bool anyHeld = lmb || rmb || kb.digit2Key.isPressed || kb.digit3Key.isPressed || kb.digit4Key.isPressed
                           || kb.digit5Key.isPressed || kb.spaceKey.isPressed;
            if (Blocked) { armed = false; latched = default; return; }
            if (!armed) { if (!anyHeld) armed = true; return; }
            latched.P0 |= m != null && m.leftButton.wasPressedThisFrame;
            latched.P1 |= kb.digit2Key.wasPressedThisFrame;
            latched.P2 |= kb.digit3Key.wasPressedThisFrame;
            latched.P3 |= kb.digit4Key.wasPressedThisFrame;
            latched.P4 |= kb.digit5Key.wasPressedThisFrame;
            latched.Dash |= m != null && m.rightButton.wasPressedThisFrame;
            latched.Potion |= kb.fKey.wasPressedThisFrame;
        }

        public PlayerInput Read(PlayerController who)
        {
            var kb = Keyboard.current; var m = Mouse.current;
            var r = latched; latched = default;
            if (kb == null || Blocked || !armed) return default;
            Vector2 a = Vector2.zero;
            if (kb.wKey.isPressed) a.y += 1; if (kb.sKey.isPressed) a.y -= 1;
            if (kb.dKey.isPressed) a.x += 1; if (kb.aKey.isPressed) a.x -= 1;
            r.Move = Vector2.ClampMagnitude(a, 1);
            r.Sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            r.H0 = m != null && m.leftButton.isPressed;
            r.H1 = kb.digit2Key.isPressed; r.H2 = kb.digit3Key.isPressed; r.H3 = kb.digit4Key.isPressed; r.H4 = kb.digit5Key.isPressed;
            r.Aim = who.transform.position + who.transform.forward * 3;
            var cam = Camera.main;
            if (cam && m != null)
            {
                var ray = cam.ScreenPointToRay(m.position.ReadValue());
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d)) r.Aim = ray.GetPoint(d);
            }
            return r;
        }
    }

    // Shared movement rules for every class (current design + Game-Design delegated defaults): walk 6 m/s, sprint 9 m/s,
    // dash 3 m over 0.20 s, no jump (Chzy18, 2 Nov); one 100-point movement stamina (sprint 25/s, dash 30, +20/s after 0.75 s).
    // No invincibility frames anywhere. Hit-stun 0.25 s and 1.5 m knockback per boss hit (Bellkeeper spec v0.2 §5).
    // All values are provisional tuning.
    public sealed class PlayerController : MonoBehaviour
    {
        public const int MaxHp = 100;
        public const float MaxStamina = 100, WalkSpeed = 6f, SprintSpeed = 9f, TurnRate = 900f, Radius = 0.45f;
        public const float SprintCost = 25f, DashCost = 30f, Regen = 20f, RegenDelay = 0.75f;
        public const float DashDistance = 3f;
        public const int DashTicks = 12, DashRecover = 4, KnockTicks = 9;
        // Potions (F): 5 per refill, +40 HP each, 1 s between drinks (values from the earlier test fight).
        public const int MaxPotions = 5, PotionHeal = 40, PotionLockout = 60;

        public string ClassName { get; private set; }
        public CombatWorld World { get; private set; }
        public PlayerKit Kit { get; private set; }
        public CharacterPresenter Presenter { get; private set; }
        public IInputSource Input;
        public bool AbilitiesEnabled;   // only during combat
        public bool MovementEnabled = true;
        // mechanic markers over this player (MechanicIcons IDs: order numeral, team, carried mark, a spread target ...), set
        // by the encounter; drawn by RunHud with the name beside each icon; cleared before each encounter
        public readonly List<string> Marks = new List<string>();
        public Shop.PlayerBuild Build;   // run items bought in the shop (null = none); read through Shop.ItemHooks
        public int MaxHealth => netMaxHp > 0 ? netMaxHp : Shop.ItemHooks.MaxHp(this, MaxHp);                              // MaxHp unless items change it
        public float MaxStaminaNow => netMaxStamina > 0 ? netMaxStamina : Shop.ItemHooks.F(this, "stamina.max", MaxStamina);
        int netMaxHp; float netMaxStamina;   // LAN client replica: the host's maxima

        public int Hp { get; private set; } = MaxHp;
        public float Stamina { get; private set; } = MaxStamina;
        public int Potions { get; private set; } = MaxPotions;
        public int PotionsDrunk { get; private set; }
        int sincePotion = 999;
        public bool Down { get; private set; }
        // Revives (docs/proposals/revives-2026-11-04.md; built as recommended, 4 Nov): at 0 HP a player is downed, not out.
        // A standing ally who stands still within ReviveRange for ReviveSeconds gets them up with ReviveHp (a hit on the
        // reviver resets it; CombatWorld.StepRevives); after BleedOutSeconds unrevived they are dead for the rest of the fight.
        // Bleed-out 30 s since 5 Nov (it was 20): in the Hard party sweep a revive that landed took 8-15 s from the fall, the
        // channel pausing while the reviver dodged, and most 20 s downs bled out.
        // Solo is unchanged: nobody is left standing, so the fight is lost.
        public const float BleedOutSeconds = 30f, ReviveSeconds = 3f, ReviveRange = 1.5f; public const int ReviveHp = 40;
        public bool Dead { get; private set; }
        public int DownedTicks { get; private set; }
        public float ReviveProgress { get; internal set; }       // 0..1 while allies channel on this downed player
        public PlayerController Reviving { get; internal set; }  // the downed ally this player is channelling on (this tick)
        internal PlayerController WasReviving;                    // ...and last tick (CombatWorld.StepRevives)
        public int Revives { get; private set; }                  // times revived this fight
        public int LastHurtTick { get; private set; } = -999;
        // still, free and unhurt: standing next to a downed ally like this channels the revive
        public bool CanChannelRevive => !Down && stun == 0 && dashTick == 0 && pullTick == 0 && !Kit.Acting
            && Last.Move.sqrMagnitude < 0.01f && !(Last.P0 || Last.P1 || Last.P2 || Last.P3 || Last.P4 || Last.Dash);
        public bool Dashing => dashTick > 0;
        public int Stunned => stun;
        public Vector3 Facing => transform.forward;
        public PlayerInput Last { get; private set; }

        int dashTick, dashLock, stun, knockTick, sinceSpend = 999;
        Vector3 dashDir, knockDir; float knockDist, dashDist = DashDistance; int knockLen = KnockTicks;
        bool sprinting;

        public static PlayerController Spawn(CombatWorld world, string className, Vector3 pos, float yaw)
        {
            var go = new GameObject("Player · " + className);
            go.transform.SetParent(world.Root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var pc = go.AddComponent<PlayerController>();
            SimSmoother.Attach(go);   // drawn between ticks (smooth at 120 fps+); the sim pose is untouched
            pc.ClassName = className; pc.World = world;
            pc.Presenter = CharacterPresenter.ForPlayer(go.transform, className);
            pc.Kit = className == "Chimesinger" ? new ChimesingerKit() : className == "Arcanist" ? new ArcanistKit() : (PlayerKit)new TollbreakerKit();
            pc.Kit.Attach(pc);
            world.Players.Add(pc);
            return pc;
        }

        // Fresh state between encounters: stamina and cooldowns reset, class setup cleared. Health and potions are
        // handled by RefreshHealth (the director calls it per the run's health policy).
        public void PrepareForEncounter(Vector3 pos, float yaw)
        {
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Stamina = MaxStaminaNow; dashTick = 0; dashLock = 0; stun = 0; knockTick = 0; sinceSpend = 999; pullTick = 0;
            if (Down) { Down = false; Dead = false; Hp = MaxHealth; }   // each fight starts with everyone up (dead ones included)
            DownedTicks = 0; ReviveProgress = 0; Reviving = null; Revives = 0;
            Kit.ResetAll();
            Presenter.ResetPose();
            Marks.Clear();
        }

        // Health policy (Chzy18, Oct 21): full health before every boss after the first; potions carry over between
        // minibosses; health AND potions refill before the area's final boss.
        public void RefreshHealth(bool refillPotions)
        {
            if (Down) return;
            Hp = MaxHealth;
            if (refillPotions) Potions = MaxPotions + Shop.ItemHooks.ExtraPotions(this);
        }

        // campaign travel: the potions left when the run moved to the next arena (RunDirector.ResumeCarried)
        public void CarryPotions(int potions) => Potions = Mathf.Max(0, potions);

        bool DrinkPotion()
        {
            if (Down || Potions == 0 || Hp >= MaxHealth || sincePotion < PotionLockout) return false;
            Potions--; PotionsDrunk++; sincePotion = 0;
            Heal(Shop.ItemHooks.PotionHeal(this, PotionHeal));
            World.BossFx.PotionHeal(transform);
            return true;
        }

        public void Heal(int amount)
        {
            if (Down || amount <= 0) return;
            int h = Mathf.Min(amount, MaxHealth - Hp); if (h <= 0) return;
            Hp += h; RunHud.Float(transform.position + Vector3.up * 2.4f, "+" + h, new Color(0.55f, 1f, 0.75f));
        }

        // LAN client replica only: show the host's values (this copy never runs Step).
        public void ApplyNetworkState(int hp, int maxHp, float stamina, float maxStamina, int potions, bool down, bool dead, int downedTicks, float reviveProgress)
        {
            netMaxHp = Mathf.Max(1, maxHp); netMaxStamina = Mathf.Max(1, maxStamina);
            Hp = Mathf.Max(0, hp); Stamina = Mathf.Max(0, stamina); Potions = Mathf.Max(0, potions); Down = down;
            Dead = down && dead; DownedTicks = down ? Mathf.Max(0, downedTicks) : 0; ReviveProgress = down ? Mathf.Clamp01(reviveProgress) : 0;
        }

        // dev / test hook only
        public void DebugSetHp(int hp) { Hp = Mathf.Clamp(hp, 1, MaxHealth); }
        public void DebugRevive() { if (!Down) return; Down = false; Dead = false; Hp = MaxHealth; Stamina = MaxStaminaNow; Presenter.ResetPose(); }

        // an ally's revive finished: up with ReviveHp and full stamina, no immunity afterwards (gpt/08)
        public void Revive()
        {
            if (!Down || Dead) return;
            Down = false; Hp = Mathf.Min(MaxHealth, ReviveHp); Stamina = MaxStaminaNow; DownedTicks = 0; ReviveProgress = 0; Revives++;
            sincePotion = PotionLockout;   // free to drink at once
            Presenter.GetUp();
            RunHud.Float(transform.position + Vector3.up * 2.4f, "revived", new Color(0.55f, 1f, 0.75f));
        }

        public void ReceiveHit(Hazard h) => ReceiveHit(h, h.Damage);

        // damage: this player's share when the hazard is split (shared soak / line split), otherwise h.Damage
        public void ReceiveHit(Hazard h, int damage)
        {
            if (Down) return;
            float mult = Kit.Mitigation(h, out bool noKnock);
            int dmg = Mathf.RoundToInt(damage * mult * World.DamageTakenScale);   // difficulty scales the hit once, here
            LastHurtTick = World.Tick;
            if (mult >= 1 && damage > 0) dmg = Mathf.Max(1, dmg);   // a shield can absorb a hit completely; nothing else can (a 0-damage hazard is a push only)
            Hp = Mathf.Max(0, Hp - dmg);
            World.ReportPlayerHit(this, h, dmg);
            if (World.Players.Count > 0 && World.Players[0] == this) Debug.Log($"[hit] you: {h.Source} -{dmg} at ({transform.position.x:0.0},{transform.position.z:0.0}) hp {Hp}");   // playtests: what hit the player
            RunHud.Float(transform.position + Vector3.up * 2.4f, "-" + dmg, mult < 1 ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.42f, 0.36f));
            World.BossFx.PlayerHit(transform.position);
            if (Hp == 0) { GoDown(); return; }
            if (!noKnock && h.Flinch)
            {
                stun = h.Stun; knockDir = h.PushDir(transform.position); knockDist = h.Knockback * Shop.ItemHooks.F(this, "knockback", 1); knockTick = knockLen = KnockTicks;
                dashTick = 0; Kit.Interrupt();
                Presenter.Hit();
            }
        }

        // Standing-hazard damage (cooling slag): no knockback, stun or interrupt, and nothing mitigates it.
        public void ReceiveBurn(Hazard h, int dmg)
        {
            if (Down || dmg <= 0) return;
            Hp = Mathf.Max(0, Hp - dmg); if (!h.BurnKeepsRevive) LastHurtTick = World.Tick;
            World.ReportPlayerHit(this, h, dmg);
            RunHud.Float(transform.position + Vector3.up * 2.4f, "-" + dmg, new Color(1f, 0.6f, 0.25f));
            if (Hp == 0) GoDown();
        }

        // a party-wide wipe mechanic (Aurelion v4: an empty tower, Final Noon): down now, whatever the HP. Not a hit: no
        // shield or mitigation applies, and it is reported as the defeat cause.
        public void ForceDown(string cause, string attackId = null)
        {
            if (Down) return;
            Hp = 0; World.LastDamageSource = cause; World.LastDamageAttackId = attackId;
            GoDown();
        }

        // An empty shared soak (settled: it wipes the party). Not mitigated and not scaled by difficulty.
        public void ForceDown(Hazard h)
        {
            if (Down) return;
            int dmg = Hp; Hp = 0;
            World.ReportPlayerHit(this, h, dmg);
            RunHud.Float(transform.position + Vector3.up * 2.4f, "-" + dmg, new Color(1f, 0.42f, 0.36f));
            GoDown();
        }

        // Carry the player to pos over `ticks` ticks (Aurelion's Last Light fall): a forced slide like a knockback, never a
        // teleport (gpt/08 §2). Input, dash and pull stop meanwhile; the stun lasts at least as long as the slide.
        public void SlideTo(Vector3 pos, int ticks)
        {
            if (Down || ticks <= 0) return;
            var d = Sim.Flat(pos - transform.position);
            dashTick = 0; pullTick = 0;
            stun = Mathf.Max(stun, ticks); knockDir = d.normalized; knockDist = d.magnitude; knockTick = knockLen = ticks;
        }

        // Test setup only: put the player somewhere at once (no boss uses this; bosses never teleport players).
        public void Relocate(Vector3 pos)
        {
            if (Down) return;
            transform.position = World.Constrain(Sim.Flat(pos), Radius);
            knockTick = 0; pullTick = 0; dashTick = 0;
        }

        // Forced movement with no damage, stun or interrupt (Crucivar's inhale). The player keeps control: the pull is
        // added to their own movement for `ticks` ticks, then stops. Never a teleport.
        public void Pull(Vector3 dir, float distance, int ticks)
        {
            if (Down || ticks <= 0) return;
            pullDir = Sim.Flat(dir).normalized; pullSpeed = distance / ticks * Sim.Hz; pullTick = ticks;
        }
        public bool BeingPulled => pullTick > 0;
        int pullTick; Vector3 pullDir; float pullSpeed;

        void GoDown()
        {
            Down = true; stun = 0; dashTick = 0; pullTick = 0;
            DownedTicks = 0; ReviveProgress = 0; Reviving = null;
            Kit.Interrupt(); Kit.ClearTargetState();
            Presenter.Down();
        }

        void Update()
        {
            if (Input is KeyboardMouseInput km) km.Poll();
        }

        // profiler scope (perf agent, 2 Nov): the input read, which is the whole think for an ally bot
        static readonly Unity.Profiling.ProfilerMarker PmInput = new Unity.Profiling.ProfilerMarker("LastBell.Player.InputOrBotThink");

        public void Step()
        {
            PlayerInput inp;
            using (PmInput.Auto()) inp = Input != null && MovementEnabled ? Input.Read(this) : default;
            if (!AbilitiesEnabled) { inp.P0 = inp.P1 = inp.P2 = inp.P3 = inp.P4 = false; inp.H0 = inp.H1 = inp.H2 = inp.H3 = inp.H4 = false; inp.Dash = inp.Potion = false; }
            Last = inp;
            if (Down)
            {
                if (!Dead && World.CombatActive && ++DownedTicks >= Sim.Ticks(BleedOutSeconds)) { Dead = true; ReviveProgress = 0; Presenter.Death(); }
                Presenter.Tick(this); return;
            }
            sinceSpend++; sincePotion++;
            if (inp.Potion && stun == 0) DrinkPotion();   // usable while moving or mid-action, not while stunned
            if (sinceSpend > Sim.Ticks(Shop.ItemHooks.RegenDelay(this, RegenDelay))) Stamina = Mathf.Min(MaxStaminaNow, Stamina + Shop.ItemHooks.F(this, "stamina.regen", Regen) * Sim.Dt);   // Steady Breath

            var cam = Camera.main;
            Vector3 fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 move = inp.WorldMove ? new Vector3(inp.Move.x, 0, inp.Move.y) : fwd * inp.Move.y + right * inp.Move.x;

            Vector3 vel = Vector3.zero; bool stepped = false;

            if (stun > 0)
            {
                stun--;
                if (knockTick > 0) { knockTick--; vel = knockDir * (knockDist / knockLen) * Sim.Hz; }
            }
            else if (dashTick > 0)
            {
                dashTick--; vel = dashDir * (dashDist / DashTicks) * Sim.Hz;
                if (dashTick == 0) { dashLock = DashRecover; Kit.OnDashEnd(); Shop.ItemHooks.OnDashEnd(this); }
            }
            else
            {
                if (dashLock > 0) dashLock--;
                float dashCost = Shop.ItemHooks.F(this, "dash.cost", DashCost);
                if (inp.Dash && dashLock == 0 && Stamina >= dashCost && Kit.CanDash())
                {
                    Spend(dashCost); dashDist = Shop.ItemHooks.F(this, "dash.distance", DashDistance);
                    dashDir = move.sqrMagnitude > 0.01f ? move.normalized : transform.forward;
                    transform.rotation = Quaternion.LookRotation(dashDir);
                    dashTick = DashTicks - 1;   // this activation tick is the first of the DashTicks steps (3 m over 12 ticks)
                    Kit.OnDash(); Shop.ItemHooks.OnDash(this);
                    Presenter.Dash();
                    World.Fx.DashTrail(this);
                    vel = dashDir * (dashDist / DashTicks) * Sim.Hz;
                }
                else
                {
                    Kit.Step(inp); stepped = true;
                    float scale = Kit.MoveScale;
                    sprinting = inp.Sprint && move.sqrMagnitude > 0.01f && scale >= 1 && Stamina > 0;
                    if (sprinting) Spend(Shop.ItemHooks.SprintCost(this, SprintCost) * Sim.Dt);
                    vel = move * (sprinting ? SprintSpeed : WalkSpeed) * scale * Shop.ItemHooks.F(this, "move.speed", 1) + Kit.Velocity;
                    if (Kit.FaceAim) Face(inp.Aim, TurnRate * 2);
                    else if (move.sqrMagnitude > 0.01f && scale > 0) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(move), TurnRate * Sim.Dt);
                }
            }
            if (!stepped) Kit.TickTimersOnly();
            if (pullTick > 0) { pullTick--; vel += pullDir * pullSpeed; }

            var pos = World.Constrain(transform.position + vel * Sim.Dt, Radius);
            transform.position = pos;
            Presenter.Locomotion = stun > 0 || dashTick > 0 || !stepped ? 0 : (vel - Kit.Velocity).magnitude;
            Presenter.Sprinting = sprinting;
            Presenter.Tick(this);
        }

        void Spend(float s) { Stamina = Mathf.Max(0, Stamina - s); sinceSpend = 0; }

        public void Face(Vector3 point, float maxDegPerSec = 99999)
        {
            var to = Sim.Flat(point - transform.position);
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), maxDegPerSec * Sim.Dt);
        }
        public void FaceNow(Vector3 point)
        {
            var to = Sim.Flat(point - transform.position);
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
        }
    }

    // Class kit contract. A kit owns its abilities' cooldowns, contact ticks and class state; the controller owns
    // movement, stamina, dash, stun and health.
    public abstract class PlayerKit
    {
        protected PlayerController P;
        protected CombatWorld W => P.World;
        public float MoveScale { get; protected set; } = 1;   // 1 free, 0 rooted, between = slowed
        public Vector3 Velocity { get; protected set; }      // extra displacement authored by the kit (lunges)
        public bool FaceAim { get; protected set; }
        // Bots: how long the current action still holds MoveScale (s), and how long a press on a slot would root or slow the
        // player from now (slot -1: the longest ready press). A route search slows every step inside that time, not only its
        // first leg; the default (0.2 s while slowed, no press roots) is what the searches assumed before (31 Oct).
        public virtual float SlowLeft => MoveScale < 1 ? 0.2f : 0;
        public virtual float RootFor(int slot) => 0;
        public virtual float RootScale(int slot) => 1;
        public void Attach(PlayerController p) { P = p; }
        public abstract string[] SlotNames { get; }
        public abstract string[] SlotKeys { get; }
        public abstract float Cooldown01(int slot);         // 0 ready .. 1 just used
        public abstract string SlotState(int slot);         // short text: seconds left, charges, "HOLD" ...
        public abstract string Tooltip(int slot);           // PF-04: name, binding, effect, cooldown, conditions
        public abstract string ClassState();                // Opening / Harmony etc.
        public abstract void Step(PlayerInput inp);
        // LAN client replica: the host's cooldowns, slot text and class state (null = this kit's own). The HUD reads Shown*.
        public float[] NetCooldowns; public string[] NetSlotStates; public string NetClassState;
        public float ShownCooldown(int slot) => NetCooldowns != null && slot < NetCooldowns.Length ? NetCooldowns[slot] : Cooldown01(slot);
        public string ShownSlotState(int slot) => NetSlotStates != null && slot < NetSlotStates.Length ? NetSlotStates[slot] : SlotState(slot);
        public string ShownClassState() => NetClassState ?? ClassState();
        public virtual void TickTimersOnly() { }             // cooldowns keep running while stunned or dashing
        public virtual bool Acting => false;                 // mid-action (bots: decide the next press when idle)
        public virtual bool CanDash() => true;
        public virtual void OnDash() { }
        public virtual void OnDashEnd() { }                  // the dash has finished (Battle Spin)
        public virtual void Interrupt() { }                  // hit-stun or down: cancel unreleased actions
        public virtual void ClearTargetState() { }           // target died / encounter over: drop marks on the boss
        public abstract void ResetAll();                     // encounter start or fresh run
        public virtual float Mitigation(Hazard h, out bool noKnockback) { noKnockback = false; return 1; }
        public virtual void DrawWorld() { }                  // per-frame presentation (marks, indicators)
    }
}
