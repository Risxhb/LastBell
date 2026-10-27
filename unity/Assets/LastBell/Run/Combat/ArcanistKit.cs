using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Arcanist, approved hard-rotation kit (#41, handoff/arcanist-animation-20261021): Clockbolt builds Ticks (max 3);
    // Time Seal (2 charges) places independent 6 s seals, ripe in their last 2 s; an empowered Arc Lance (3 Ticks)
    // inscribes the oldest uninscribed seal on the target; Convergence detonates pending seals, best with two inscribed,
    // ripe seals; Temporal Anchor records a position, recast returns there with a brief shield; Clockstep = shared dash
    // (cancels an unfinished Lance, keeps Ticks). No ultimate, no time stop, no rewind of health, no invulnerability.
    // Every number marked TUNE is a prototype value; #41 leaves damage/cooldowns/cast times open.
    public sealed class ArcanistKit : PlayerKit
    {
        // ---- prototype tuning (ticks at 60 Hz) ----
        // Release ticks follow the clips' markers (Clockbolt .35, TimeSeal .58, ArcLance .45, Convergence .55). Cast times
        // are short enough that Seal A -> 3 bolts -> Lance -> Seal B fits the < 2 s spacing the 6 s / 2 s-ripe seals need
        // (measured by the autotest), with time left to move.
        const int BoltCast = 8, BoltRelease = 3, BoltCd = 15, BoltDamage = 14, MaxTicks = 3;                 // TUNE
        const float BoltSpeed = 24f, BoltRange = 15f;
        const int SealCast = 12, SealRelease = 7, SealCharges = 2, SealRecharge = 300, SealDamage = 10;          // TUNE
        const int SealLife = 360, RipeTicks = 120;                                                              // #41: 6 s, last 2 s ripe
        const float SealRange = 14f, SealWidth = 1.2f;
        const int LanceCast = 20, LanceRelease = 9, LanceCd = 45, LanceDamage = 28, LanceEmpowered = 46;       // TUNE
        const float LanceRange = 14f, LanceWidth = 1.0f;
        const int ConvCast = 16, ConvRelease = 9, ConvCd = 240;                                                // TUNE
        const int SealBase = 30, RipeMult = 2, InscribedMult = 2, PerfectPairBonus = 60, ExpiryBurst = 8;       // TUNE
        const int AnchorCast = 12, AnchorWindow = 240, AnchorCd = 720, ReturnAnim = 10;                          // TUNE
        const int ShieldTicks = 120, ShieldAmount = 30;                                                         // TUNE

        public sealed class Seal { public int Left; public bool Inscribed; public int Id; public bool Ripe => Left <= RipeTicks; }
        readonly List<Seal> seals = new List<Seal>();
        public IReadOnlyList<Seal> Seals => seals;
        int sealIds;

        enum Act { None, Bolt, Seal, Lance, Conv, Anchor, Return }
        Act act; int t; Vector3 aim; bool altHand;
        public override bool Acting => act != Act.None;
        int boltCd, lanceCd, convCd, anchorCd, charges = SealCharges, recharge;
        int buffered = -1, bufferUntil; const int BufferTicks = 9;
        public int Ticks { get; private set; }
        public int LastConvergence { get; private set; }
        public bool LastLanceEmpowered { get; private set; }
        // Temporal Anchor
        public bool AnchorSet => anchorLeft > 0;
        int anchorLeft; Vector3 anchorPos;
        int shieldLeft, shieldHp;
        public int ShieldHp => shieldLeft > 0 ? shieldHp : 0;

        public override string[] SlotNames => new[] { "Clockbolt", "Time Seal", "Arc Lance", "Convergence", AnchorSet ? "Anchor Return" : "Temporal Anchor" };
        public override string[] SlotKeys => new[] { "1 / LMB", "2", "3", "4", "6" };
        public override float Cooldown01(int s) => s switch
        {
            0 => boltCd / (float)BoltCd, 1 => charges > 0 ? 0 : recharge / (float)SealRecharge,
            2 => lanceCd / (float)LanceCd, 3 => convCd / (float)ConvCd, _ => AnchorSet ? 0 : anchorCd / (float)AnchorCd
        };
        public override string SlotState(int s) => s switch
        {
            0 => Ticks >= MaxTicks ? "TICKS FULL" : "",
            1 => $"{charges}/{SealCharges}" + (charges < SealCharges ? $" +{recharge / (float)Sim.Hz:0.0}s" : ""),
            2 => lanceCd > 0 ? Sec(lanceCd) : Ticks >= MaxTicks ? "EMPOWERED" : "ready",
            3 => convCd > 0 ? Sec(convCd) : seals.Count == 0 ? "no seals" : "ready",
            _ => AnchorSet ? $"return {anchorLeft / (float)Sim.Hz:0.0}s" : anchorCd > 0 ? Sec(anchorCd) : "ready"
        };
        static string Sec(int ticks) => (ticks / (float)Sim.Hz).ToString("0.0") + "s";
        public override string ClassState()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("TICKS ").Append(new string('●', Ticks)).Append(new string('○', MaxTicks - Ticks));
            for (int i = 0; i < seals.Count; i++)
            {
                var s = seals[i];
                sb.Append($"   SEAL {(char)('A' + i)} {s.Left / (float)Sim.Hz:0.0}s{(s.Ripe ? " RIPE" : "")}{(s.Inscribed ? " +INSCRIBED" : "")}");
            }
            if (ShieldHp > 0) sb.Append($"   SHIELD {ShieldHp}");
            return sb.ToString();
        }
        public override string Tooltip(int s) => s switch
        {
            0 => "CLOCKBOLT  [1 / LMB, hold to repeat]\nBolt toward the cursor. A hit adds one TICK (max 3; extra hits at 3 are wasted).\nAt 3 Ticks your next Arc Lance is EMPOWERED.",
            1 => "TIME SEAL  [2]\nAimed seal on the target, 6 s long, RIPE for its last 2 s. 2 charges, 5 s recharge each.\nEach cast is a separate seal, even on the same target.",
            2 => "ARC LANCE  [3]\nBrief rooted cast, then a piercing beam. EMPOWERED (3 Ticks): spends them and INSCRIBES the oldest\nuninscribed seal on each target hit. Unempowered: damage only. Dash cancels an unfinished cast and keeps Ticks.",
            3 => "CONVERGENCE  [4]\nDetonates all your seals. Ripe doubles a seal, inscribed doubles it again;\ntwo inscribed ripe seals at once add a bonus. Early seals pay less. Cooldown 4 s.",
            _ => "TEMPORAL ANCHOR  [6]\nRecord your position. Recast within 4 s to return there and gain a brief shield (absorbs 30).\nSeals and inscriptions are kept; their timers keep running. Cooldown 12 s.",
        };

        public override void ResetAll()
        {
            act = Act.None; t = 0; boltCd = lanceCd = convCd = anchorCd = 0; charges = SealCharges; recharge = 0;
            Ticks = 0; seals.Clear(); anchorLeft = 0; shieldLeft = shieldHp = 0; LastConvergence = 0; buffered = -1;
            MoveScale = 1; Velocity = Vector3.zero; FaceAim = false;
            P.World.Fx.Seals(null, null); P.World.Fx.Anchor(P, Vector3.zero, false); P.World.Fx.TimeShield(P, false);
        }
        public override void ClearTargetState() { seals.Clear(); P.World.Fx.Seals(null, null); }

        void Timers()
        {
            if (boltCd > 0) boltCd--; if (lanceCd > 0) lanceCd--; if (convCd > 0) convCd--;
            if (anchorCd > 0 && !AnchorSet) anchorCd--;
            if (charges < SealCharges && --recharge <= 0) { charges++; recharge = charges < SealCharges ? SealRecharge : 0; }
            var b = W.Boss;
            if (seals.Count > 0 && (b == null || !b.Solid)) ClearTargetState();
            for (int i = seals.Count - 1; i >= 0; i--)
                if (--seals[i].Left <= 0)
                {
                    // an expired seal bursts weakly (#41), exactly once, then is gone
                    seals.RemoveAt(i);
                    if (b != null && b.Solid) { W.DamageBoss(ExpiryBurst, b.transform.position + Vector3.up * b.HitHeight, "Seal expiry"); W.Fx.SealBurst(b.transform.position, false); }
                }
            if (seals.Count > 0) W.Fx.Seals(b, seals);
            if (AnchorSet && --anchorLeft == 0) { anchorCd = AnchorCd; W.Fx.Anchor(P, Vector3.zero, false); }
            if (shieldLeft > 0 && --shieldLeft == 0) { shieldHp = 0; W.Fx.TimeShield(P, false); }
        }
        public override void TickTimersOnly() { Timers(); Velocity = Vector3.zero; }

        public override void Step(PlayerInput inp)
        {
            Timers(); aim = inp.Aim; Velocity = Vector3.zero; MoveScale = 1; FaceAim = false;
            for (int i = 1; i <= 4; i++) if (inp.Pressed(i)) { buffered = i; bufferUntil = W.Tick + BufferTicks; }
            if (buffered >= 0 && W.Tick > bufferUntil) buffered = -1;   // absolute expiry: stunned or dashing ticks count too
            bool Want(int s) => inp.Pressed(s) || buffered == s;
            if (act == Act.None)
            {
                if (Want(4) && (AnchorSet || anchorCd == 0)) { buffered = -1; if (AnchorSet) BeginReturn(); else Begin(Act.Anchor, "TemporalAnchor", AnchorCast); }
                else if (Want(3) && convCd == 0 && seals.Count > 0) { buffered = -1; Begin(Act.Conv, "Convergence", ConvCast); }
                else if (Want(2) && lanceCd == 0) { buffered = -1; Begin(Act.Lance, "ArcLance", LanceCast); }
                else if (Want(1) && charges > 0) { buffered = -1; Begin(Act.Seal, "TimeSeal", SealCast); }
                else if ((inp.Pressed(0) || inp.Held(0)) && boltCd == 0) { altHand = !altHand; Begin(Act.Bolt, altHand ? "Clockbolt" : "Clockbolt2", BoltCast); }
            }
            if (act == Act.None) return;
            t++;
            switch (act)
            {
                case Act.Bolt: MoveScale = 0.6f; FaceAim = true; if (t == BoltRelease) FireBolt(); if (t >= BoltCast) act = Act.None; break;
                case Act.Seal: MoveScale = 0.35f; FaceAim = t < SealRelease; if (t == SealRelease) PlaceSeal(); if (t >= SealCast) act = Act.None; break;
                case Act.Lance: MoveScale = 0; FaceAim = t < LanceRelease; if (t == 1) W.Fx.LanceCharge(P, LanceRelease, Ticks >= MaxTicks); if (t == LanceRelease) FireLance(); if (t >= LanceCast) act = Act.None; break;
                case Act.Conv: MoveScale = 0.2f; if (t == ConvRelease) Converge(); if (t >= ConvCast) act = Act.None; break;
                case Act.Anchor: MoveScale = 0.5f; if (t == AnchorCast / 2) PlaceAnchor(); if (t >= AnchorCast) act = Act.None; break;
                case Act.Return: MoveScale = 0; if (t >= ReturnAnim) act = Act.None; break;
            }
        }

        void Begin(Act a, string clip, int ticks) { act = a; t = 0; P.FaceNow(aim); P.Presenter.Action(clip, ticks); }

        Vector3 AimDir() { var d = Sim.Flat(aim - P.transform.position); return d.sqrMagnitude > 0.1f ? d.normalized : P.transform.forward; }

        void FireBolt()
        {
            boltCd = BoltCd;
            var dir = AimDir(); var start = P.transform.position + dir * 0.7f + Vector3.up * 1.4f;
            W.Projectiles.Fire(new ProjectileSystem.Projectile
            {
                Pos = start, Vel = dir * BoltSpeed, Range = BoltRange, Radius = 0.3f, Damage = BoltDamage, Ability = "Clockbolt",
                Color = PlayerFx.TimeTeal,
                OnHit = (dealt, at) => { if (dealt > 0) Ticks = Mathf.Min(MaxTicks, Ticks + 1); }   // prototype: Ticks only on hit
            });
        }

        void PlaceSeal()
        {
            if (charges == SealCharges) recharge = SealRecharge;
            charges--;                                                  // prototype: a missed seal still spends the charge
            int dealt = W.LineBoss(P.transform.position, AimDir(), SealRange, SealWidth, SealDamage, "Time Seal", out var at);
            if (dealt > 0) seals.Add(new Seal { Left = SealLife, Id = ++sealIds });
            W.Fx.SealCast(P.transform.position + Vector3.up * 1.3f, at, dealt > 0);
        }

        void FireLance()
        {
            lanceCd = LanceCd;
            bool emp = Ticks >= MaxTicks; LastLanceEmpowered = emp;
            if (emp) Ticks = 0;                                         // prototype: an empowered Lance spends Ticks even on a miss
            int dealt = W.LineBoss(P.transform.position, AimDir(), LanceRange, LanceWidth, emp ? LanceEmpowered : LanceDamage, emp ? "Arc Lance+" : "Arc Lance", out var at);
            if (dealt > 0 && emp)
                foreach (var s in seals) if (!s.Inscribed) { s.Inscribed = true; W.Fx.Inscribe(W.Boss); break; }   // oldest uninscribed, one per seal
            W.Fx.Lance(P.transform.position + Vector3.up * 1.3f + AimDir() * 0.5f, AimDir(), LanceRange, emp, dealt > 0);
        }

        void Converge()
        {
            convCd = ConvCd;
            int dmg = 0, perfect = 0;
            foreach (var s in seals)
            {
                int v = SealBase * (s.Ripe ? RipeMult : 1) * (s.Inscribed ? InscribedMult : 1);
                if (s.Ripe && s.Inscribed) perfect++;
                dmg += v;
            }
            if (perfect >= 2) dmg += PerfectPairBonus;
            var b = W.Boss;
            LastConvergence = b != null && b.Solid ? W.DamageBoss(dmg, b.transform.position + Vector3.up * b.HitHeight, perfect >= 2 ? "Convergence (perfect)" : "Convergence") : 0;
            if (b != null) W.Fx.SealBurst(b.transform.position, perfect >= 2);
            seals.Clear(); W.Fx.Seals(null, null);
        }

        void PlaceAnchor() { anchorPos = P.transform.position; anchorLeft = AnchorWindow; W.Fx.Anchor(P, anchorPos, true); }

        void BeginReturn()
        {
            var from = P.transform.position;
            P.transform.position = W.Constrain(anchorPos, PlayerController.Radius);
            anchorLeft = 0; anchorCd = AnchorCd;
            shieldLeft = ShieldTicks; shieldHp = ShieldAmount;
            W.Fx.Anchor(P, Vector3.zero, false); W.Fx.AnchorReturn(from, P.transform.position); W.Fx.TimeShield(P, true);
            act = Act.Return; t = 0; P.Presenter.Action("AnchorReturn", ReturnAnim);
        }

        // brief shield after an Anchor return: absorbs a flat amount; knockback still applies (it is not a Brace)
        public override float Mitigation(Hazard h, out bool noKnockback)
        {
            noKnockback = false;
            if (ShieldHp <= 0 || h.Damage <= 0) return 1;
            int absorbed = Mathf.Min(shieldHp, h.Damage); shieldHp -= absorbed;
            if (shieldHp == 0) { shieldLeft = 0; W.Fx.TimeShield(P, false); }
            return (h.Damage - absorbed) / (float)h.Damage;
        }

        public override bool CanJump() => act == Act.None || act == Act.Bolt;
        // Clockstep cancels an unfinished Lance (nothing spent, Ticks kept) and keeps seals, inscriptions and the anchor
        public override void OnDash() { if (act == Act.Lance && t < LanceRelease) W.Fx.LanceCancel(P); act = Act.None; Velocity = Vector3.zero; W.Fx.ClockstepTrail(P); }
        public override void Interrupt() { if (act == Act.Lance) W.Fx.LanceCancel(P); act = Act.None; Velocity = Vector3.zero; }

        // test hooks
        public int Charges => charges;
    }
}
