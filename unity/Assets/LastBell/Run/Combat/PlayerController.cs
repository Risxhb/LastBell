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
        public bool Sprint, Jump, Dash, Potion;
        public bool P0, P1, P2, P3, P4;   // pressed this tick
        public bool H0, H1, H2, H3, H4;   // held
        public bool Pressed(int i) => i switch { 0 => P0, 1 => P1, 2 => P2, 3 => P3, _ => P4 };
        public bool Held(int i) => i switch { 0 => H0, 1 => H1, 2 => H2, 3 => H3, _ => H4 };
    }

    public interface IInputSource { PlayerInput Read(PlayerController who); }

    // Keyboard + mouse, the project's agreed bindings (number row for actions, 5 dash; LMB basic, RMB dash as extras;
    // Shift sprint, Space jump). Reads Input System devices directly like the existing test controllers. Gamepads are
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
            bool anyHeld = lmb || rmb || kb.digit1Key.isPressed || kb.digit2Key.isPressed || kb.digit3Key.isPressed || kb.digit4Key.isPressed
                           || kb.digit5Key.isPressed || kb.digit6Key.isPressed || kb.spaceKey.isPressed;
            if (Blocked) { armed = false; latched = default; return; }
            if (!armed) { if (!anyHeld) armed = true; return; }
            latched.P0 |= kb.digit1Key.wasPressedThisFrame || (m != null && m.leftButton.wasPressedThisFrame);
            latched.P1 |= kb.digit2Key.wasPressedThisFrame;
            latched.P2 |= kb.digit3Key.wasPressedThisFrame;
            latched.P3 |= kb.digit4Key.wasPressedThisFrame;
            latched.P4 |= kb.digit6Key.wasPressedThisFrame;
            latched.Dash |= kb.digit5Key.wasPressedThisFrame || (m != null && m.rightButton.wasPressedThisFrame);
            latched.Jump |= kb.spaceKey.wasPressedThisFrame;
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
            r.H0 = kb.digit1Key.isPressed || (m != null && m.leftButton.isPressed);
            r.H1 = kb.digit2Key.isPressed; r.H2 = kb.digit3Key.isPressed; r.H3 = kb.digit4Key.isPressed; r.H4 = kb.digit6Key.isPressed;
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
    // dash 3 m over 0.20 s, jump; one 100-point movement stamina (sprint 25/s, jump 20, dash 30, +20/s after 0.75 s).
    // No invincibility frames anywhere. Hit-stun 0.25 s and 1.5 m knockback per boss hit (Bellkeeper spec v0.2 §5).
    // All values are provisional tuning.
    public sealed class PlayerController : MonoBehaviour
    {
        public const int MaxHp = 100;
        public const float MaxStamina = 100, WalkSpeed = 6f, SprintSpeed = 9f, TurnRate = 900f, Radius = 0.45f;
        public const float SprintCost = 25f, JumpCost = 20f, DashCost = 30f, Regen = 20f, RegenDelay = 0.75f;
        public const float DashDistance = 3f, JumpHeight = 0.8f;
        public const int DashTicks = 12, DashRecover = 4, JumpTicks = 36, KnockTicks = 9;
        // Potions (F): 5 per refill, +40 HP each, 1 s between drinks (values from the earlier test fight).
        public const int MaxPotions = 5, PotionHeal = 40, PotionLockout = 60;

        public string ClassName { get; private set; }
        public CombatWorld World { get; private set; }
        public PlayerKit Kit { get; private set; }
        public CharacterPresenter Presenter { get; private set; }
        public IInputSource Input;
        public bool AbilitiesEnabled;   // only during combat
        public bool MovementEnabled = true;

        public int Hp { get; private set; } = MaxHp;
        public float Stamina { get; private set; } = MaxStamina;
        public int Potions { get; private set; } = MaxPotions;
        public int PotionsDrunk { get; private set; }
        int sincePotion = 999;
        public bool Down { get; private set; }
        public float Height { get; private set; }
        public bool Airborne => jumpTick >= 0;
        public bool Dashing => dashTick > 0;
        public int Stunned => stun;
        public Vector3 Facing => transform.forward;
        public PlayerInput Last { get; private set; }

        int dashTick, dashLock, jumpTick = -1, stun, knockTick, sinceSpend = 999;
        Vector3 dashDir, knockDir; float knockDist;
        bool sprinting;

        public static PlayerController Spawn(CombatWorld world, string className, Vector3 pos, float yaw)
        {
            var go = new GameObject("Player · " + className);
            go.transform.SetParent(world.Root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var pc = go.AddComponent<PlayerController>();
            pc.ClassName = className; pc.World = world;
            pc.Presenter = CharacterPresenter.ForPlayer(go.transform, className);
            pc.Kit = className == "Chimesinger" ? new ChimesingerKit() : className == "Arcanist" ? new ArcanistKit() : (PlayerKit)new VanguardKit2();
            pc.Kit.Attach(pc);
            world.Players.Add(pc);
            return pc;
        }

        // Fresh state between encounters: stamina and cooldowns reset, class setup cleared. Health and potions are
        // handled by RefreshHealth (the director calls it per the run's health policy).
        public void PrepareForEncounter(Vector3 pos, float yaw)
        {
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Stamina = MaxStamina; dashTick = 0; dashLock = 0; jumpTick = -1; Height = 0; stun = 0; knockTick = 0; sinceSpend = 999;
            Kit.ResetAll();
            Presenter.ResetPose();
        }

        // Health policy (Chzy18, Oct 21): full health before every boss after the first; potions carry over between
        // minibosses; health AND potions refill before the area's final boss.
        public void RefreshHealth(bool refillPotions)
        {
            if (Down) return;
            Hp = MaxHp;
            if (refillPotions) Potions = MaxPotions;
        }

        bool DrinkPotion()
        {
            if (Down || Potions == 0 || Hp >= MaxHp || sincePotion < PotionLockout) return false;
            Potions--; PotionsDrunk++; sincePotion = 0;
            Heal(PotionHeal);
            World.BossFx.PotionHeal(transform);
            return true;
        }

        public void Heal(int amount)
        {
            if (Down || amount <= 0) return;
            int h = Mathf.Min(amount, MaxHp - Hp); if (h <= 0) return;
            Hp += h; RunHud.Float(transform.position + Vector3.up * 2.4f, "+" + h, new Color(0.55f, 1f, 0.75f));
        }

        // dev / test hook only
        public void DebugSetHp(int hp) { Hp = Mathf.Clamp(hp, 1, MaxHp); }
        public void DebugRevive() { if (!Down) return; Down = false; Hp = MaxHp; Stamina = MaxStamina; Presenter.ResetPose(); }

        public void ReceiveHit(Hazard h)
        {
            if (Down) return;
            float mult = Kit.Mitigation(h, out bool noKnock);
            int dmg = Mathf.RoundToInt(h.Damage * mult * World.DamageTakenScale);   // difficulty scales the hit once, here
            if (mult >= 1) dmg = Mathf.Max(1, dmg);   // a shield can absorb a hit completely; nothing else can
            Hp = Mathf.Max(0, Hp - dmg);
            World.ReportPlayerHit(this, h, dmg);
            RunHud.Float(transform.position + Vector3.up * 2.4f, "-" + dmg, mult < 1 ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.42f, 0.36f));
            World.BossFx.PlayerHit(transform.position);
            if (Hp == 0) { GoDown(); return; }
            if (!noKnock)
            {
                stun = h.Stun; knockDir = h.PushDir(transform.position); knockDist = h.Knockback; knockTick = KnockTicks;
                dashTick = 0; Kit.Interrupt();
                Presenter.Hit();
            }
        }

        // Standing-hazard damage (cooling slag): no knockback, stun or interrupt, and nothing mitigates it.
        public void ReceiveBurn(Hazard h, int dmg)
        {
            if (Down || dmg <= 0) return;
            Hp = Mathf.Max(0, Hp - dmg);
            World.ReportPlayerHit(this, h, dmg);
            RunHud.Float(transform.position + Vector3.up * 2.4f, "-" + dmg, new Color(1f, 0.6f, 0.25f));
            if (Hp == 0) GoDown();
        }

        // a party-wide wipe mechanic (Aurelion v4: an empty tower, Final Noon): down now, whatever the HP. Not a hit: no
        // shield or mitigation applies, and it is reported as the defeat cause.
        public void ForceDown(string cause)
        {
            if (Down) return;
            Hp = 0; World.LastDamageSource = cause;
            GoDown();
        }

        void GoDown()
        {
            Down = true; stun = 0; dashTick = 0; jumpTick = -1; Height = 0;
            Kit.Interrupt(); Kit.ClearTargetState();
            Presenter.Down();
        }

        void Update()
        {
            if (Input is KeyboardMouseInput km) km.Poll();
        }

        public void Step()
        {
            var inp = Input != null && MovementEnabled ? Input.Read(this) : default;
            if (!AbilitiesEnabled) { inp.P0 = inp.P1 = inp.P2 = inp.P3 = inp.P4 = false; inp.H0 = inp.H1 = inp.H2 = inp.H3 = inp.H4 = false; inp.Dash = inp.Jump = inp.Potion = false; }
            Last = inp;
            if (Down) { Presenter.Tick(this); return; }
            sinceSpend++; sincePotion++;
            if (inp.Potion && stun == 0) DrinkPotion();   // usable while moving or mid-action, not while stunned
            if (sinceSpend > Sim.Ticks(RegenDelay)) Stamina = Mathf.Min(MaxStamina, Stamina + Regen * Sim.Dt);

            var cam = Camera.main;
            Vector3 fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 move = fwd * inp.Move.y + right * inp.Move.x;

            Vector3 vel = Vector3.zero; bool stepped = false;
            if (jumpTick >= 0)
            {
                jumpTick++; float u = (float)jumpTick / JumpTicks; Height = JumpHeight * 4 * u * (1 - u);
                if (jumpTick >= JumpTicks) { jumpTick = -1; Height = 0; Presenter.Land(); }
            }

            if (stun > 0)
            {
                stun--;
                if (knockTick > 0) { knockTick--; vel = knockDir * (knockDist / KnockTicks) * Sim.Hz; }
            }
            else if (dashTick > 0)
            {
                dashTick--; vel = dashDir * (DashDistance / DashTicks) * Sim.Hz;
                if (dashTick == 0) dashLock = DashRecover;
            }
            else
            {
                if (dashLock > 0) dashLock--;
                if (inp.Dash && dashLock == 0 && Stamina >= DashCost && Kit.CanDash())
                {
                    Spend(DashCost);
                    dashDir = move.sqrMagnitude > 0.01f ? move.normalized : transform.forward;
                    transform.rotation = Quaternion.LookRotation(dashDir);
                    dashTick = DashTicks - 1;   // this activation tick is the first of the DashTicks steps (3 m over 12 ticks)
                    Kit.OnDash();
                    Presenter.Dash();
                    World.Fx.DashTrail(this);
                    vel = dashDir * (DashDistance / DashTicks) * Sim.Hz;
                }
                else
                {
                    if (inp.Jump && jumpTick < 0 && Stamina >= JumpCost && Kit.CanJump())
                    {
                        Spend(JumpCost); jumpTick = 0; Presenter.Jump();
                    }
                    Kit.Step(inp); stepped = true;
                    float scale = Kit.MoveScale;
                    sprinting = inp.Sprint && move.sqrMagnitude > 0.01f && scale >= 1 && Stamina > 0;
                    if (sprinting) Spend(SprintCost * Sim.Dt);
                    vel = move * (sprinting ? SprintSpeed : WalkSpeed) * scale + Kit.Velocity;
                    if (Kit.FaceAim) Face(inp.Aim, TurnRate * 2);
                    else if (move.sqrMagnitude > 0.01f && scale > 0) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(move), TurnRate * Sim.Dt);
                }
            }
            if (!stepped) Kit.TickTimersOnly();

            var pos = World.Constrain(transform.position + vel * Sim.Dt, Radius);
            transform.position = pos;
            Presenter.Locomotion = stun > 0 || dashTick > 0 || !stepped ? 0 : (vel - Kit.Velocity).magnitude;
            Presenter.Sprinting = sprinting;
            Presenter.HeightOffset = Height;
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
    // movement, stamina, dash, jump, stun and health.
    public abstract class PlayerKit
    {
        protected PlayerController P;
        protected CombatWorld W => P.World;
        public float MoveScale { get; protected set; } = 1;   // 1 free, 0 rooted, between = slowed
        public Vector3 Velocity { get; protected set; }      // extra displacement authored by the kit (lunges)
        public bool FaceAim { get; protected set; }
        public void Attach(PlayerController p) { P = p; }
        public abstract string[] SlotNames { get; }
        public abstract string[] SlotKeys { get; }
        public abstract float Cooldown01(int slot);         // 0 ready .. 1 just used
        public abstract string SlotState(int slot);         // short text: seconds left, charges, "HOLD" ...
        public abstract string Tooltip(int slot);           // PF-04: name, binding, effect, cooldown, conditions
        public abstract string ClassState();                // Opening / Harmony etc.
        public abstract void Step(PlayerInput inp);
        public virtual void TickTimersOnly() { }             // cooldowns keep running while stunned or dashing
        public virtual bool CanDash() => true;
        public virtual bool CanJump() => true;
        public virtual void OnDash() { }
        public virtual void Interrupt() { }                  // hit-stun or down: cancel unreleased actions
        public virtual void ClearTargetState() { }           // target died / encounter over: drop marks on the boss
        public abstract void ResetAll();                     // encounter start or fresh run
        public virtual float Mitigation(Hazard h, out bool noKnockback) { noKnockback = false; return 1; }
        public virtual void DrawWorld() { }                  // per-frame presentation (marks, indicators)
    }
}
