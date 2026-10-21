using UnityEngine;

namespace LastBell.Run
{
    // Vanguard, approved four-slot kit (#31 handoff, October 20): Steel Rhythm (Basic), Sundering Blow (Special),
    // Last Word (Ultimate), Brace (shield Defensive); dash is the shared movement action. No attack resources, no parry,
    // no iframes. Opening is one non-stacking 6 s empowerment. Cooldowns and contact ticks live here, not in clips:
    // animations are stretched to these durations. Names/numbers are the handoff's starting targets; damage values and
    // frame timings are provisional (marked TUNE).
    public sealed class VanguardKit2 : PlayerKit
    {
        // ---- tuning (ticks at 60 Hz) ----
        // Steel Rhythm: two quick cuts then a heavier cleave
        static readonly int[] BasicTotal = { 20, 20, 32 }, BasicContact = { 8, 8, 15 }, BasicChainFrom = { 11, 11, 99 };
        static readonly int[] BasicDamage = { 22, 22, 42 };                       // TUNE
        static readonly float[] BasicReach = { 2.6f, 2.6f, 3.0f }, BasicArc = { 110, 110, 170 }, BasicLunge = { 0.35f, 0.35f, 0.6f };
        const int ComboWindow = 36;                // after a hit's recovery, the next press continues the chain
        const int ComboAfterDash = 180;            // dodge preserves the combo step for 3 s (#31)
        const int OpeningTicks = 360;              // 6 s
        // Sundering Blow
        const int SunderTotal = 42, SunderRelease = 20, SunderCd = 480, SunderDamage = 70, SunderBonus = 60, UltRefund = 120;
        const float SunderReach = 3.3f, SunderArc = 70, SunderLunge = 0.5f;
        // Last Word: ~0.8 s planted windup, overhead contact, shockwave toward the cursor
        const int UltTotal = 72, UltRelease = 48, UltCd = 2700, UltDamage = 140, WaveDamage = 90;
        const float UltReach = 2.6f, UltArc = 100, WaveSpeed = 18f, WaveRange = 8f, WaveWidth = 2.2f;
        // Brace: raise, hold up to 1.5 s, lower; 12 s cooldown from lowering
        const int BraceRaise = 6, BraceMax = 90, BraceLower = 9, BraceCd = 720;
        const float BraceReduction = 0.6f, BraceArc = 140, BraceMove = 0.35f;

        enum Act { None, Basic, Sunder, Ult, Brace }
        int buffered = -1, bufferUntil;   // PF-05: an ability pressed during another action fires as soon as it can (0.15 s)
        const int BufferTicks = 9;
        Act act; int t;           // ticks into the current action
        int step;                 // basic: hit being played (0..2)
        bool queued, contactDone, sunderEmpowered;
        int comboStep, comboExpire;   // next basic index and its expiry
        int sunderCd, ultCd, braceCd, opening;
        int bracePhase; bool braceAbsorbed;   // 0 raise, 1 hold, 2 lower
        Vector3 aim;
        // Last Word shockwave (presentation follows its simulated position)
        bool waveLive; Vector3 wavePos, waveDir; float waveTravel; bool waveHit;

        public bool OpeningActive => opening > 0;
        public bool Braced => act == Act.Brace && bracePhase < 2;
        public int Step01 => comboStep;
        public float UltRemaining => ultCd / (float)Sim.Hz;

        public override string[] SlotNames => new[] { "Steel Rhythm", "Sundering Blow", "Last Word", "Brace" };
        public override string[] SlotKeys => new[] { "1 / LMB", "2", "3", "4 hold" };
        public override float Cooldown01(int s) => s switch { 1 => sunderCd / (float)SunderCd, 2 => ultCd / (float)UltCd, 3 => act == Act.Brace ? 1 : braceCd / (float)BraceCd, _ => 0 };
        public override string SlotState(int s) => s switch
        {
            0 => comboExpire > 0 || act == Act.Basic ? $"hit {Mathf.Min(3, (act == Act.Basic ? step : comboStep) + 1)}/3" : "",
            1 => sunderCd > 0 ? Sec(sunderCd) : OpeningActive ? "EMPOWERED" : "ready",
            2 => ultCd > 0 ? Sec(ultCd) : "ready",
            3 => act == Act.Brace ? (bracePhase < 2 ? "BRACED" : "lowering") : braceCd > 0 ? Sec(braceCd) : "ready",
            _ => ""
        };
        static string Sec(int ticks) => (ticks / (float)Sim.Hz).ToString("0.0") + "s";
        public override string Tooltip(int s) => s switch
        {
            0 => "STEEL RHYTHM  [1 / LMB, hold to repeat]\nThree-hit sword combo toward the cursor: two quick cuts, then a heavier cleave.\nLanding hit 3 grants OPENING (6 s, does not stack). No cooldown.\nA dash keeps your combo step for 3 s.",
            1 => "SUNDERING BLOW  [2]\nHeavy aimed strike. Cooldown 8 s, starting on release.\nWith OPENING: extra damage and 2 s off Last Word's cooldown.\nDash before the release cancels it with no cooldown spent.",
            2 => "LAST WORD  [3]\nPlanted 0.8 s windup, overhead strike, then a shockwave toward the cursor.\nCooldown 45 s. Works without OPENING. Dash during the windup cancels it (nothing spent).",
            _ => "BRACE  [hold 4]\nRaise the shield toward the cursor for up to 1.5 s; you can still walk slowly.\nFrontal strikes: 60% less damage, no knockback. Rear hits and ground eruptions bypass it.\nFirst absorbed hit grants OPENING. Cooldown 12 s after lowering. No attacking while braced.",
        };
        public override string ClassState() => OpeningActive ? $"OPENING {opening / (float)Sim.Hz:0.0}s" : "";

        public override void ResetAll()
        {
            act = Act.None; t = 0; step = 0; queued = false; comboStep = 0; comboExpire = 0;
            sunderCd = ultCd = braceCd = 0; opening = 0; waveLive = false; buffered = -1;
            MoveScale = 1; Velocity = Vector3.zero; FaceAim = false;
            P.World.Fx.Opening(P, false); P.World.Fx.Shield(P, 0, false);
        }

        void Timers()
        {
            if (sunderCd > 0) sunderCd--; if (ultCd > 0) ultCd--;
            if (braceCd > 0 && act != Act.Brace) braceCd--;
            if (opening > 0 && --opening == 0) P.World.Fx.Opening(P, false);
            if (comboExpire > 0 && act != Act.Basic && --comboExpire == 0) comboStep = 0;
            StepWave();
        }

        public override void TickTimersOnly() { Timers(); Velocity = Vector3.zero; }

        public override void Step(PlayerInput inp)
        {
            Timers();
            aim = inp.Aim;
            Velocity = Vector3.zero; MoveScale = 1; FaceAim = false;
            for (int i = 1; i <= 3; i++) if (inp.Pressed(i)) { buffered = i; bufferUntil = W.Tick + BufferTicks; }
            if (buffered >= 0 && W.Tick > bufferUntil) buffered = -1;   // absolute expiry: stunned or dashing ticks count too
            switch (act)
            {
                case Act.None: Idle(inp); break;
                case Act.Basic: Basic(inp); break;
                case Act.Sunder: Sunder(); break;
                case Act.Ult: Ult(); break;
                case Act.Brace: Brace(inp); break;
            }
        }

        void Idle(PlayerInput inp)
        {
            if (TryAbility(inp)) return;
            if (inp.Pressed(0) || inp.Held(0)) StartBasic(comboExpire > 0 ? comboStep : 0);
        }

        bool TryAbility(PlayerInput inp)
        {
            bool Want(int s) => inp.Pressed(s) || buffered == s;
            if ((Want(3) || inp.Held(3)) && braceCd == 0) { buffered = -1; StartBrace(); return true; }   // hold works as a buffer
            if (Want(2) && ultCd == 0) { buffered = -1; Start(Act.Ult); P.Presenter.Action("LastWord", UltTotal); return true; }
            if (Want(1) && sunderCd == 0) { buffered = -1; Start(Act.Sunder); P.Presenter.Action("SunderingBlow", SunderTotal); return true; }
            return false;
        }

        void Start(Act a) { act = a; t = 0; contactDone = false; P.FaceNow(aim); }

        void StartBasic(int i)
        {
            Start(Act.Basic); step = Mathf.Clamp(i, 0, 2); queued = false; comboExpire = 0;
            P.Presenter.Action("SteelRhythm" + (step + 1), BasicTotal[step]);
        }

        void Basic(PlayerInput inp)
        {
            t++; MoveScale = 0;
            if (inp.Pressed(0) || (inp.Held(0) && t >= BasicChainFrom[step])) queued = true;
            if (t <= BasicContact[step] + 2) Velocity = P.transform.forward * (BasicLunge[step] / (BasicContact[step] + 2)) * Sim.Hz;
            if (t == BasicContact[step])
            {
                contactDone = true;
                int dealt = W.StrikeBoss(P, P.transform.position, P.transform.forward, BasicReach[step], BasicArc[step], BasicDamage[step], "SteelRhythm" + (step + 1));
                W.Fx.Slash(P, BasicReach[step], BasicArc[step], step == 2, step == 1);
                if (dealt > 0 && step == 2) GrantOpening();
            }
            // after contact, a ready ability cancels the rest of the swing (Basic 3 -> empowered Special); the dash
            // cancels at any time (controller calls OnDash)
            if (t > BasicContact[step] + 1)
            {
                int keepStep = step; bool landed3 = step == 2;
                act = Act.None;
                if (TryAbility(inp)) { comboStep = landed3 ? 0 : Mathf.Min(2, keepStep + 1); comboExpire = landed3 ? 0 : ComboAfterDash; return; }
                act = Act.Basic;
            }
            if (queued && t >= BasicChainFrom[step] && step < 2) { StartBasic(step + 1); return; }
            if (t >= BasicTotal[step])
            {
                act = Act.None;
                comboStep = step < 2 ? step + 1 : 0;
                comboExpire = step < 2 ? ComboWindow : 0;
            }
        }

        void Sunder()
        {
            t++; MoveScale = 0;
            if (t < SunderRelease) { FaceAim = true; }   // limited aim correction during the windup
            if (t > SunderRelease - 4 && t <= SunderRelease) Velocity = P.transform.forward * (SunderLunge / 4) * Sim.Hz;
            if (t == SunderRelease)
            {
                // release: cooldown starts and Opening is consumed here, whether or not the blow lands
                sunderCd = SunderCd;
                sunderEmpowered = OpeningActive;
                if (sunderEmpowered) { opening = 0; W.Fx.Opening(P, false); ultCd = Mathf.Max(0, ultCd - UltRefund); }
                int dmg = SunderDamage + (sunderEmpowered ? SunderBonus : 0);
                int dealt = W.StrikeBoss(P, P.transform.position, P.transform.forward, SunderReach, SunderArc, dmg, sunderEmpowered ? "SunderingBlow+" : "SunderingBlow");
                W.Fx.Sunder(P, SunderReach, sunderEmpowered, dealt > 0);
            }
            if (t >= SunderTotal) act = Act.None;
        }

        void Ult()
        {
            t++; MoveScale = 0;
            if (t < UltRelease - 12) FaceAim = true;
            if (t == 1) W.Fx.UltWindup(P, UltRelease);
            if (t == UltRelease)
            {
                ultCd = UltCd;
                W.StrikeBoss(P, P.transform.position, P.transform.forward, UltReach, UltArc, UltDamage, "LastWord");
                waveLive = true; waveHit = false; waveTravel = 0;
                waveDir = Sim.Flat(aim - P.transform.position).sqrMagnitude > 0.1f ? Sim.Flat(aim - P.transform.position).normalized : P.transform.forward;
                wavePos = P.transform.position + P.transform.forward * 1.2f;
                W.Fx.UltSlam(P, wavePos);
            }
            if (t >= UltTotal) act = Act.None;
        }

        void StepWave()
        {
            if (!waveLive) return;
            float d = WaveSpeed * Sim.Dt; wavePos += waveDir * d; waveTravel += d;
            W.Fx.Shockwave(wavePos, waveDir, WaveWidth);
            if (!waveHit)
            {
                var b = W.Boss;
                if (b != null && b.Solid && Sim.Flat(b.Core(wavePos) - wavePos).magnitude <= b.BodyRadius + WaveWidth / 2)
                {
                    waveHit = true;   // the shockwave hits once per cast
                    var core = b.Core(wavePos);
                    W.DamageBoss(WaveDamage, core - Sim.Flat(core - wavePos).normalized * b.BodyRadius + Vector3.up * b.HitHeight, "LastWordWave");
                }
            }
            if (waveTravel >= WaveRange) waveLive = false;
        }

        void StartBrace()
        {
            act = Act.Brace; t = 0; bracePhase = 0; braceAbsorbed = false;
            P.Presenter.Brace(1);
            W.Fx.Shield(P, 0, true);
        }

        void Brace(PlayerInput inp)
        {
            t++; FaceAim = true; MoveScale = BraceMove;
            if (bracePhase == 0 && t >= BraceRaise) { bracePhase = 1; }
            if (bracePhase < 2 && (t >= BraceMax || (!inp.Held(3) && t >= BraceRaise))) { bracePhase = 2; t = 0; P.Presenter.Brace(2); W.Fx.Shield(P, 0, false); }
            if (bracePhase == 2 && t >= BraceLower) EndBrace();
        }
        void EndBrace() { act = Act.None; braceCd = BraceCd; P.Presenter.Brace(0); W.Fx.Shield(P, 0, false); }

        // Brace: 60 % less damage and no knockback from frontal Strikes while raised; rear and ground hazards bypass.
        // A successful absorption grants Opening once per use and keeps the combo step.
        public override float Mitigation(Hazard h, out bool noKnockback)
        {
            noKnockback = false;
            if (!Braced || bracePhase == 0 || h.Kind != HazardKind.Strike) return 1;
            var from = Sim.Flat(h.IncomingFrom(P.transform.position) - P.transform.position);
            if (from.sqrMagnitude > 0.01f && Vector3.Angle(P.transform.forward, from) > BraceArc / 2) return 1;
            noKnockback = true;
            W.Fx.Shield(P, 1, true);
            P.Presenter.BraceHit();
            if (!braceAbsorbed) { braceAbsorbed = true; GrantOpening(); }
            return 1 - BraceReduction;
        }

        void GrantOpening() { opening = OpeningTicks; W.Fx.Opening(P, true); }

        public override bool CanJump() => act == Act.None;
        public override bool CanDash() => !(act == Act.Ult && t >= UltRelease) && !(act == Act.Sunder && t >= SunderRelease);

        // Dash interrupts basics and unreleased Special/Ultimate windups: no damage, no cooldown spent.
        public override void OnDash()
        {
            if (act == Act.Basic) { comboStep = contactDone ? Mathf.Min(2, step + 1) : step; if (contactDone && step == 2) comboStep = 0; comboExpire = ComboAfterDash; }
            else if (comboExpire > 0) comboExpire = Mathf.Max(comboExpire, ComboAfterDash);
            if (act == Act.Brace) EndBrace();
            if (act == Act.Ult) W.Fx.UltCancel(P);
            act = Act.None; Velocity = Vector3.zero;
        }

        public override void Interrupt()
        {
            if (act == Act.Brace) EndBrace();
            if (act == Act.Ult) W.Fx.UltCancel(P);
            act = Act.None; Velocity = Vector3.zero;
        }

        public override void ClearTargetState() { waveLive = false; }
    }
}
