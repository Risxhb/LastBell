using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Arcanist, arcane redesign (docs/design/arcanist-redesign.md, agreed October 26, 2026): a ranged glass cannon.
    //   Magic Missile (LMB): a tap fires one straight missile on the press; keep holding to summon up to 3 that home on
    //     release. Disintegrate (2, hold): a steered beam channel that builds through 3 damage steps. Rune Sigil (3): a
    //     rune at the cursor that arms, then explodes. Astral Orb (4): the astrolabe's orb auto-fires for 8 s.
    //     Arcane Comet (5): a mark at the cursor, the comet lands 2 s later; the bullseye doubles it.
    //   Blink is the shared dash (same distance, time and stamina; no invincibility) drawn as a teleport.
    // No defensive ability. Every number marked TUNE is a starting value (PF-08: the old kit hit too hard).
    public sealed class ArcanistKit : PlayerKit
    {
        // ---- tuning (ticks at 60 Hz) ----
        const int TapCast = 8, TapRelease = 3, TapCd = 15, TapDamage = 10;                                   // TUNE
        const float MissileSpeed = 26f, MissileRange = 18f, HomingSpeed = 20f, HomingRange = 32f, HomingTurn = 7f;
        const int ChargeStep = 15, MaxMissiles = 3, VolleyCast = 9, VolleyDamage = 13;                       // TUNE
        const int DisWind = 21, DisMax = 120, DisEvery = 10, DisStepTicks = 40, DisCd = 480, DisEnd = 12;      // TUNE
        static readonly int[] DisDamage = { 8, 12, 16 };                                                   // TUNE
        const float DisRange = 16f, DisWidth = 0.7f, DisTurn = 1.5f;                                        // deg per tick
        const int SigCast = 18, SigRelease = 9, SigArm = 36, SigCd = 360, SigDamage = 55;                    // TUNE
        const float SigRange = 16f, SigRadius = 2.5f;
        const int OrbCharge = 45, OrbLife = 480, OrbEvery = 60, OrbFirst = 30, OrbCd = 600, OrbDamage = 8;  // TUNE
        const float OrbShotSpeed = 22f, OrbTurn = 10f;
        const int ComCast = 24, ComRelease = 12, ComFall = 120, ComCd = 1080, ComDamage = 90;               // TUNE
        const float ComRange = 20f, ComRadius = 4f, ComCore = 1.33f;
        const int BlinkInTicks = 15;

        // where the astrolabe's orb floats while it fights (the effects draw it at the same spot)
        public static Vector3 OrbOffset(Transform t) => Vector3.up * 2.15f + t.right * 0.55f - t.forward * 0.15f;
        public static Vector3 HandOffset(Transform t) => Vector3.up * 1.35f + t.forward * 0.6f + t.right * 0.12f;
        // the fan of summoned missiles behind his right shoulder (slot 0 nearest the astrolabe), relative to his position
        public static Vector3 MissileSlot(Transform t, int i)
        {
            float a = (i - 1) * 28f * Mathf.Deg2Rad;
            return Vector3.up * (1.8f + 0.28f * Mathf.Cos(a)) + t.right * (0.62f + 0.45f * Mathf.Sin(a)) - t.forward * 0.15f;
        }

        public sealed class Mark { public int Id; public Vector3 Pos; public int Left, Total; }
        readonly List<Mark> sigils = new List<Mark>(), comets = new List<Mark>();
        int markIds;

        enum Act { None, Tap, Charge, Volley, DisWind, DisBeam, DisEnd, Sigil, OrbCharge, Comet, BlinkIn }
        Act act; int t; Vector3 aim; bool altHand;
        int tapCd, disCd, sigCd, orbCd, comCd;
        int buffered = -1, bufferUntil; const int BufferTicks = 9;
        Vector3 beamDir; int beamTicks;
        int orbLeft, orbShots;
        bool blinking;

        // ---- state for the HUD, effects and tests ----
        public int Missiles { get; private set; }            // summoned while charging
        public int LastVolley { get; private set; }          // missiles in the last released volley
        public bool Charging => act == Act.Charge;
        public bool BeamActive => act == Act.DisBeam;
        public int BeamStep => beamTicks < DisStepTicks ? 1 : beamTicks < 2 * DisStepTicks ? 2 : 3;
        public Vector3 BeamDir => beamDir;
        public int BeamDamage { get; private set; }          // dealt by the current or last beam
        public IReadOnlyList<Mark> Sigils => sigils;
        public IReadOnlyList<Mark> Comets => comets;
        public bool OrbActive => orbLeft > 0;
        public float OrbLeft01 => orbLeft / (float)OrbLife;
        public int OrbShots => orbShots;
        public int LastSigil { get; private set; }
        public int LastComet { get; private set; }
        public bool LastCometBullseye { get; private set; }
        public bool Blinking => blinking;
        public static float SigilRadius => SigRadius;
        public static float CometRadius => ComRadius;
        public static float CometCore => ComCore;
        public static int CometFallTicks => ComFall;
        public static int SigilArmTicks => SigArm;
        public static int OrbChargeTicks => OrbCharge;
        public static int DisWindTicks => DisWind;

        public override string[] SlotNames => new[] { "Magic Missile", "Disintegrate", "Rune Sigil", "Astral Orb", "Arcane Comet" };
        public override string[] SlotKeys => new[] { "LMB", "2 hold", "3", "4", "5" };
        public override float Cooldown01(int s) => s switch
        {
            0 => tapCd / (float)TapCd, 1 => disCd / (float)DisCd, 2 => sigCd / (float)SigCd,
            3 => orbCd / (float)OrbCd, _ => comCd / (float)ComCd
        };
        public override string SlotState(int s) => s switch
        {
            0 => act == Act.Charge ? $"{Missiles}/{MaxMissiles}" : "",
            1 => act == Act.DisBeam ? $"STEP {BeamStep}" : disCd > 0 ? Sec(disCd) : "ready",
            2 => sigCd > 0 ? Sec(sigCd) : "ready",
            3 => orbLeft > 0 ? $"ACTIVE {orbLeft / (float)Sim.Hz:0.0}s" : orbCd > 0 ? Sec(orbCd) : "ready",
            _ => comCd > 0 ? Sec(comCd) : "ready"
        };
        static string Sec(int ticks) => (ticks / (float)Sim.Hz).ToString("0.0") + "s";
        public override string ClassState()
        {
            var sb = new System.Text.StringBuilder();
            if (act == Act.Charge) sb.Append("MISSILES ").Append(new string('●', Missiles)).Append(new string('○', MaxMissiles - Missiles));
            if (act == Act.DisBeam) Sep(sb).Append("DISINTEGRATE ").Append(new string('▮', BeamStep)).Append(new string('▯', 3 - BeamStep));
            if (orbLeft > 0) Sep(sb).Append($"ORB {orbLeft / (float)Sim.Hz:0.0}s");
            foreach (var c in comets) Sep(sb).Append($"COMET {c.Left / (float)Sim.Hz:0.0}s");
            return sb.ToString();
        }
        static System.Text.StringBuilder Sep(System.Text.StringBuilder sb) => sb.Length > 0 ? sb.Append("   ") : sb;
        public override string Tooltip(int s) => s switch
        {
            0 => "MAGIC MISSILE  [LMB]\nTap: one missile straight at the cursor (10). It can miss.\nHold: summon up to 3 missiles, one every 0.25 s; release to fire them. They home on the boss (13 each).\nYou move at half speed while charging. Dashing drops the charge.",
            1 => "DISINTEGRATE  [2, hold]\nA beam you steer toward the cursor for up to 2 s. Damage builds in 3 steps the longer you hold.\nYou move at 20% speed. Release to stop; dashing breaks the beam. Cooldown 8 s.",
            2 => "RUNE SIGIL  [3]\nDraws a rune at the cursor (16 m). After 0.6 s it explodes (55) if the boss is inside it (2.5 m).\nCooldown 6 s.",
            3 => "ASTRAL ORB  [4]\nCharge 0.75 s to release the astrolabe's orb. For 8 s it fires a homing bolt every second (8 each)\nwhile you do anything else. Dashing during the charge cancels it for free. Cooldown 10 s from the cast.",
            _ => "ARCANE COMET  [5]\nMarks a spot at the cursor (20 m); the comet lands 2 s later (90, 4 m).\nThe bullseye in the middle doubles it. The mark stays where you put it. Cooldown 18 s.",
        };

        public override void ResetAll()
        {
            act = Act.None; t = 0; tapCd = disCd = sigCd = orbCd = comCd = 0; buffered = -1;
            Missiles = 0; LastVolley = 0; beamTicks = 0; BeamDamage = 0; orbLeft = 0; orbShots = 0; blinking = false;
            LastSigil = LastComet = 0; LastCometBullseye = false;
            sigils.Clear(); comets.Clear();
            MoveScale = 1; Velocity = Vector3.zero; FaceAim = false;
            if (P != null) { P.Presenter.SetHidden(false); P.Presenter.EndOverlay(); W.Fx.ArcanistReset(P); }
        }
        public override void ClearTargetState()
        {
            foreach (var s in sigils) W.Fx.SigilFade(s.Id); sigils.Clear();
            foreach (var c in comets) W.Fx.CometFade(c.Id); comets.Clear();
        }

        void Timers()
        {
            if (tapCd > 0) tapCd--; if (disCd > 0) disCd--; if (sigCd > 0) sigCd--; if (orbCd > 0) orbCd--; if (comCd > 0) comCd--;
            var b = W.Boss; bool live = b != null && b.Solid;
            for (int i = sigils.Count - 1; i >= 0; i--)
                if (--sigils[i].Left <= 0)
                {
                    var s = sigils[i]; sigils.RemoveAt(i);
                    bool hit = live && Sim.Flat(b.transform.position - s.Pos).magnitude <= SigRadius + b.BodyRadius;
                    LastSigil = hit ? W.DamageBoss(SigDamage, b.transform.position + Vector3.up * b.HitHeight, "Rune Sigil", false) : 0;
                    W.Fx.SigilExplode(s.Id, s.Pos, hit);
                }
            for (int i = comets.Count - 1; i >= 0; i--)
                if (--comets[i].Left <= 0)
                {
                    var c = comets[i]; comets.RemoveAt(i);
                    float d = live ? Sim.Flat(b.transform.position - c.Pos).magnitude : 999;
                    bool hit = live && d <= ComRadius + b.BodyRadius, bull = hit && d <= ComCore + 0.5f * b.BodyRadius;
                    LastComet = hit ? W.DamageBoss(bull ? ComDamage * 2 : ComDamage, b.transform.position + Vector3.up * b.HitHeight, bull ? "Arcane Comet (bullseye)" : "Arcane Comet", false) : 0;
                    LastCometBullseye = bull;
                    W.Fx.CometImpact(c.Id, c.Pos, hit, bull);
                }
            if (orbLeft > 0)
            {
                int age = OrbLife - orbLeft;
                if (age >= OrbFirst && (age - OrbFirst) % OrbEvery == 0 && live) FireOrbShot();
                if (--orbLeft == 0) W.Fx.OrbEnd(P);
            }
            // Blink: the body is hidden for the dash and rebuilt where it ends; a stun mid-blink shows him where he stands
            if (blinking && P.Stunned > 0) { blinking = false; P.Presenter.SetHidden(false); W.Fx.BlinkBlocked(P); }
            else if (blinking && !P.Dashing)
            {
                blinking = false; P.Presenter.SetHidden(false); W.Fx.BlinkIn(P);
                if (act == Act.None) { act = Act.BlinkIn; t = 0; P.Presenter.Action("BlinkIn", BlinkInTicks); }
            }
        }
        public override void TickTimersOnly() { Timers(); Velocity = Vector3.zero; }

        public override void Step(PlayerInput inp)
        {
            Timers(); aim = inp.Aim; Velocity = Vector3.zero; MoveScale = 1; FaceAim = false;
            for (int i = 1; i <= 4; i++) if (inp.Pressed(i)) { buffered = i; bufferUntil = W.Tick + BufferTicks; }
            if (buffered >= 0 && W.Tick > bufferUntil) buffered = -1;   // absolute expiry: stunned or dashing ticks count too
            bool Want(int s) => inp.Pressed(s) || buffered == s;
            if (act == Act.None || act == Act.BlinkIn)
            {
                if (Want(4) && comCd == 0) { buffered = -1; Begin(Act.Comet, "ArcaneComet", ComCast); }
                else if (Want(3) && orbCd == 0 && orbLeft == 0) { buffered = -1; Begin(Act.OrbCharge, "AstralOrb", OrbCharge); W.Fx.OrbCharge(P, OrbCharge); }
                else if (Want(2) && sigCd == 0) { buffered = -1; Begin(Act.Sigil, "RuneSigil", SigCast); }
                else if (Want(1) && disCd == 0) { buffered = -1; BeginDisintegrate(); }
                else if (inp.Pressed(0) && tapCd == 0) { altHand = !altHand; Begin(Act.Tap, altHand ? "MissileTap" : "MissileTap2", TapCast); }
            }
            if (act == Act.None) return;
            t++;
            switch (act)
            {
                case Act.Tap:
                    MoveScale = 0.6f; FaceAim = true;
                    if (t == TapRelease) FireTap();
                    if (t >= TapCast) { if (inp.Held(0)) BeginCharge(); else act = Act.None; }
                    break;
                case Act.Charge:
                    MoveScale = 0.5f; FaceAim = true;
                    int n = Mathf.Min(MaxMissiles, t / ChargeStep);
                    if (n != Missiles) { Missiles = n; W.Fx.MissileSummon(P, n); }
                    if (!inp.Held(0)) { if (Missiles > 0) BeginVolley(); else EndCharge(false); }
                    break;
                case Act.Volley:
                    MoveScale = 0.6f; FaceAim = t < 3;
                    if (t == 2) BeginFireVolley();
                    if (t >= 2 && (t - 2) % 3 == 0 && (t - 2) / 3 < volleyCount) FireVolleyDart((t - 2) / 3);   // 0.05 s apart: tak-tak-tak
                    if (t >= VolleyCast) act = Act.None;
                    break;
                case Act.DisWind:
                    MoveScale = 0.2f; FaceAim = true;
                    if (t >= DisWind) { act = Act.DisBeam; t = 0; beamTicks = 0; beamDir = AimDir(); P.Presenter.Overlay("DisintegrateHold", 1.0f, loop: true); W.Fx.BeamStart(P); BeamTick(); }
                    break;
                case Act.DisBeam:
                    MoveScale = 0.2f; FaceAim = false;
                    beamTicks++;
                    // the beam turns toward the cursor at a capped rate, and the body follows the beam
                    var want = AimDir();
                    beamDir = Vector3.RotateTowards(beamDir, want, DisTurn * Mathf.Deg2Rad, 0).normalized;
                    P.transform.rotation = Quaternion.LookRotation(beamDir);
                    BeamTick();
                    if (!inp.Held(1) || beamTicks >= DisMax) EndBeam(false);
                    break;
                case Act.DisEnd:
                    MoveScale = 0.4f; if (t >= DisEnd) act = Act.None; break;
                case Act.Sigil:
                    MoveScale = 0.5f; FaceAim = t < SigRelease;
                    if (t == SigRelease) PlaceSigil();
                    if (t >= SigCast) act = Act.None;
                    break;
                case Act.OrbCharge:
                    MoveScale = 0.5f;
                    if (t >= OrbCharge) { act = Act.None; orbLeft = OrbLife; orbShots = 0; orbCd = OrbCd - OrbCharge; W.Fx.OrbRelease(P); }
                    break;
                case Act.Comet:
                    MoveScale = 0.3f; FaceAim = t < ComRelease;
                    if (t == ComRelease) PlaceComet();
                    if (t >= ComCast) act = Act.None;
                    break;
                case Act.BlinkIn:
                    if (t >= BlinkInTicks) act = Act.None; break;
            }
        }

        void Begin(Act a, string clip, int ticks)
        {
            if (act == Act.BlinkIn) P.Presenter.EndOverlay();
            act = a; t = 0; P.FaceNow(aim); P.Presenter.Overlay(clip, ticks / (float)Sim.Hz);
        }

        Vector3 AimDir() { var d = Sim.Flat(aim - P.transform.position); return d.sqrMagnitude > 0.1f ? d.normalized : P.transform.forward; }
        // a ground mark at the cursor, within range and on the arena floor (it may sit under the boss)
        Vector3 ClampAim(float range)
        {
            var d = Sim.Flat(aim - P.transform.position); if (d.magnitude > range) d = d.normalized * range;
            var at = P.transform.position + d; at.y = 0; float bound = CombatWorld.PlayerBound;
            if (CombatWorld.SquareBound) { at.x = Mathf.Clamp(at.x, -bound, bound); at.z = Mathf.Clamp(at.z, -bound, bound); }
            else { var f = new Vector2(at.x, at.z); if (f.magnitude > bound) { f = f.normalized * bound; at.x = f.x; at.z = f.y; } }
            return at;
        }

        // ---- Magic Missile ----
        void FireTap()
        {
            tapCd = TapCd;
            var dir = AimDir(); var from = P.transform.position + HandOffset(P.transform);
            Fire(from, dir * MissileSpeed, MissileRange, 0, TapDamage, "Magic Missile", false);
            W.Fx.MissileTapCast(P, from, dir);
        }
        void BeginCharge()
        {
            act = Act.Charge; t = 0; Missiles = 0;
            P.Presenter.Overlay("MissileCharge", 0.4f, hold: true);
            W.Fx.MissileChargeStart(P);
        }
        void EndCharge(bool fired)
        {
            if (!fired) W.Fx.MissileChargeCancel(P);
            Missiles = 0; if (act == Act.Charge) act = Act.None;
        }
        void BeginVolley() { act = Act.Volley; t = 0; P.Presenter.Overlay("MissileVolley", VolleyCast / (float)Sim.Hz); }
        int volleyCount;
        void BeginFireVolley()
        {
            tapCd = TapCd; LastVolley = volleyCount = Missiles;
            W.Fx.MissileVolley(P, Missiles);
            Missiles = 0;
        }
        // each summoned dart leaves its spot in the fan, flies outward, then curves in on the boss
        void FireVolleyDart(int i)
        {
            var tr = P.transform; var dir = AimDir();
            var at = tr.position + MissileSlot(tr, i);
            float side = volleyCount == 1 ? 0 : (i - (volleyCount - 1) / 2f) / ((volleyCount - 1) / 2f);
            var launch = (Quaternion.AngleAxis(side * 50f, Vector3.up) * dir + Vector3.up * 0.35f).normalized;
            Fire(at, launch * HomingSpeed, HomingRange, HomingTurn, VolleyDamage, "Magic Missile (charged)", true, i);
            W.Fx.MissileLaunch(P, i);
        }
        void Fire(Vector3 from, Vector3 vel, float range, float homing, int dmg, string ability, bool charged, int index = 0)
        {
            var pr = new ProjectileSystem.Projectile
            {
                Pos = from, Vel = vel, Range = range, Radius = 0.3f, Damage = dmg, Ability = ability, Color = PlayerFx.ArcLilac,
                Homing = homing, HomingDelay = charged ? 4 : 0
            };
            W.Fx.Missile(pr, charged, index);
            W.Projectiles.Fire(pr);
        }

        // ---- Disintegrate ----
        void BeginDisintegrate()
        {
            act = Act.DisWind; t = 0; P.FaceNow(aim); BeamDamage = 0;
            P.Presenter.Overlay("DisintegrateStart", DisWind / (float)Sim.Hz, hold: true);
            W.Fx.BeamWindup(P, DisWind);
        }
        void BeamTick()
        {
            var tr = P.transform; var from = tr.position + HandOffset(tr);
            int dealt = 0; Vector3 at = from + beamDir * DisRange; bool hit = false;
            if (beamTicks % DisEvery == 0)
            {
                dealt = W.LineBoss(tr.position, beamDir, DisRange, DisWidth, DisDamage[BeamStep - 1], "Disintegrate", out at, false);
                BeamDamage += dealt; hit = dealt > 0;
            }
            else
            {
                // presentation only: where the beam would meet the boss this tick (no damage between damage ticks)
                var b = W.Boss;
                if (b != null && b.Solid)
                {
                    var to = Sim.Flat(b.transform.position - tr.position); float along = Vector3.Dot(to, beamDir), side = (to - beamDir * along).magnitude;
                    if (along > 0 && along < DisRange + b.BodyRadius && side < DisWidth / 2 + b.BodyRadius) { hit = true; at = tr.position + beamDir * Mathf.Max(0, along - b.BodyRadius) + Vector3.up * b.HitHeight; }
                }
            }
            W.Fx.Beam(P, from, beamDir, hit ? at : from + beamDir * DisRange, BeamStep, hit);
        }
        void EndBeam(bool broken)
        {
            disCd = DisCd; act = broken ? Act.None : Act.DisEnd; t = 0;
            if (broken) P.Presenter.EndOverlay(); else P.Presenter.Overlay("DisintegrateEnd", DisEnd / (float)Sim.Hz);
            W.Fx.BeamEnd(P, broken);
        }

        // ---- Rune Sigil ----
        void PlaceSigil()
        {
            sigCd = SigCd;
            var at = ClampAim(SigRange);
            var s = new Mark { Id = ++markIds, Pos = at, Left = SigArm, Total = SigArm };
            sigils.Add(s); W.Fx.SigilPlace(P, s.Id, at, SigArm);
        }

        // ---- Astral Orb ----
        void FireOrbShot()
        {
            orbShots++;
            var from = P.transform.position + OrbOffset(P.transform);
            var b = W.Boss; var to = (b.transform.position + Vector3.up * b.HitHeight - from).normalized;
            var pr = new ProjectileSystem.Projectile
            {
                Pos = from, Vel = (to + Vector3.up * 0.25f).normalized * OrbShotSpeed, Range = 40f, Radius = 0.3f, Damage = OrbDamage,
                Ability = "Astral Orb", Color = PlayerFx.StarSilver, Homing = OrbTurn
            };
            W.Fx.OrbShot(P, pr);
            W.Projectiles.Fire(pr);
        }

        // ---- Arcane Comet ----
        void PlaceComet()
        {
            comCd = ComCd;
            var at = ClampAim(ComRange);
            var c = new Mark { Id = ++markIds, Pos = at, Left = ComFall, Total = ComFall };
            comets.Add(c); W.Fx.CometMark(P, c.Id, at, ComFall);
        }

        public override bool CanJump() => act == Act.None || act == Act.Tap || act == Act.BlinkIn;

        // Blink (shared dash): cancels unreleased casts. A Disintegrate windup or an Orb charge refunds; a running beam breaks.
        public override void OnDash()
        {
            CancelForMove(true);
            blinking = true; P.Presenter.SetHidden(true); W.Fx.BlinkOut(P);
        }
        public override void Interrupt() => CancelForMove(false);
        void CancelForMove(bool dash)
        {
            switch (act)
            {
                case Act.Charge: EndCharge(false); break;
                case Act.DisWind: W.Fx.BeamCancel(P); break;
                case Act.DisBeam: EndBeam(true); break;
                case Act.OrbCharge: W.Fx.OrbCancel(P); break;
            }
            act = Act.None; Missiles = 0; Velocity = Vector3.zero; P.Presenter.EndOverlay();
        }
    }
}
