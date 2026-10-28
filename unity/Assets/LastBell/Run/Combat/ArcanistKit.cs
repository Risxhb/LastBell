using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Arcanist, arcane redesign (docs/design/arcanist-redesign.md): a ranged glass cannon built around GLYPHS (0-3).
    //   Magic Missile (LMB): a tap fires one straight missile; hold to summon up to 3 (5 in Surge) that fly straight at the
    //     cursor on release. Filler damage.
    //   Disintegrate (2, hold): the glyph builder. A steered beam channel in 3 damage steps; each step completed while the
    //     beam is hitting the boss carves one glyph (a full channel on the boss = 3).
    //   Rune Sigil (3) and Arcane Comet (4): the rune spenders. Each spends 1 glyph for full power; cast without a glyph
    //     the rune is faint and deals 35%. Sigil explodes 0.8 s after it is placed; the Comet lands 3 s after the mark.
    //   Arcane Surge (5): spends every glyph for 2 s of empowerment per glyph: volleys summon 5 missiles and taps fire two,
    //     runes cost no glyph and arm or fall twice as fast.
    //   Blink is the shared dash (same distance, time and stamina; no invincibility) drawn as a teleport.
    // Rotation: missiles while Disintegrate cools -> Disintegrate on the boss (3 glyphs) -> Sigil / Comet (1 each) or Surge.
    // No defensive ability. Every number marked TUNE is a starting value.
    public sealed class ArcanistKit : PlayerKit
    {
        // ---- tuning (ticks at 60 Hz) ----
        const int TapCast = 8, TapRelease = 3, TapCd = 15, TapDamage = 10;                                   // TUNE
        const float MissileSpeed = 26f, MissileRange = 18f, VolleySpeed = 24f, VolleyRange = 24f;
        const int ChargeStep = 15, SurgeChargeStep = 10, MaxMissiles = 3, SurgeMissiles = 5, VolleyDamage = 13;  // TUNE
        const int DisWind = 21, DisMax = 120, DisEvery = 10, DisStepTicks = 40, DisCd = 480, DisEnd = 12;      // TUNE
        static readonly int[] DisDamage = { 8, 12, 16 };                                                   // TUNE
        const float DisRange = 16f, DisWidth = 0.7f, DisTurn = 1.5f;                                        // deg per tick
        const int SigCast = 18, SigRelease = 9, SigArm = 48, SigArmSurge = 24, SigCd = 360, SigDamage = 55;  // TUNE
        const float SigRange = 16f, SigRadius = 2.5f;
        const int ComCast = 24, ComRelease = 12, ComFall = 180, ComFallSurge = 90, ComCd = 1080, ComDamage = 90;   // TUNE
        const float ComRange = 20f, ComRadius = 4f, ComCore = 1.33f;
        const float Unpowered = 0.35f;                                                                     // TUNE: runes without a glyph
        public const int MaxGlyphs = 3;
        const int SurgeCast = 18, SurgeRelease = 10, SurgePerGlyph = 120, SurgeCd = 900;                    // TUNE
        const int BlinkInTicks = 15;

        // where the astrolabe floats (effects), the casting hand, and the fan of summoned missiles behind his right shoulder
        public static Vector3 OrbOffset(Transform t) => Vector3.up * 2.15f + t.right * 0.55f - t.forward * 0.15f;
        public static Vector3 HandOffset(Transform t) => Vector3.up * 1.35f + t.forward * 0.6f + t.right * 0.12f;
        public static Vector3 MissileSlot(Transform t, int i, int count = 3)
        {
            float a = (i - (count - 1) / 2f) * (count > 3 ? 20f : 28f) * Mathf.Deg2Rad;
            return Vector3.up * (1.8f + 0.28f * Mathf.Cos(a)) + t.right * (0.62f + 0.45f * Mathf.Sin(a)) - t.forward * 0.15f;
        }

        public sealed class Mark { public int Id; public Vector3 Pos; public int Left, Total; public bool Powered; }
        readonly List<Mark> sigils = new List<Mark>(), comets = new List<Mark>();
        int markIds;

        enum Act { None, Tap, Charge, Volley, DisWind, DisBeam, DisEnd, Sigil, Comet, Surge, BlinkIn }
        Act act; int t; Vector3 aim; bool altHand;
        public override bool Acting => act != Act.None;
        int tapCd, disCd, sigCd, comCd, surgeCd;
        int buffered = -1, bufferUntil; const int BufferTicks = 9;
        Vector3 beamDir; int beamTicks; bool stepHit; Vector3 beamContact;
        int surgeLeft, surgeTotal;
        bool blinking;

        // ---- state for the HUD, effects and tests ----
        public int Glyphs { get; private set; }
        public bool Surging => surgeLeft > 0;
        public float SurgeLeft01 => surgeTotal > 0 ? surgeLeft / (float)surgeTotal : 0;
        public float SurgeSeconds => surgeLeft / (float)Sim.Hz;
        public int Missiles { get; private set; }            // summoned while charging
        public int MissileCap => Surging ? SurgeMissiles : MaxMissiles;
        public int LastVolley { get; private set; }          // missiles in the last released volley
        public bool Charging => act == Act.Charge;
        public bool BeamActive => act == Act.DisBeam;
        public int BeamStep => beamTicks < DisStepTicks ? 1 : beamTicks < 2 * DisStepTicks ? 2 : 3;
        public Vector3 BeamDir => beamDir;
        public int BeamDamage { get; private set; }          // dealt by the current or last beam
        public IReadOnlyList<Mark> Sigils => sigils;
        public IReadOnlyList<Mark> Comets => comets;
        public int LastSigil { get; private set; }
        public bool LastSigilPowered { get; private set; }
        public int LastComet { get; private set; }
        public bool LastCometBullseye { get; private set; }
        public bool LastCometPowered { get; private set; }
        public int LastSurgeGlyphs { get; private set; }
        public bool Blinking => blinking;
        public static float SigilRadius => SigRadius;
        public static float CometRadius => ComRadius;
        public static float CometCore => ComCore;
        public static int CometFallTicks => ComFall;
        public static int SigilArmTicks => SigArm;
        public static int DisWindTicks => DisWind;

        public override string[] SlotNames => new[] { "Magic Missile", "Disintegrate", "Rune Sigil", "Arcane Comet", "Arcane Surge" };
        public override string[] SlotKeys => new[] { "LMB", "2 hold", "3", "4", "5" };
        public override float Cooldown01(int s) => s switch
        {
            0 => tapCd / (float)TapCd, 1 => disCd / (float)DisCd, 2 => sigCd / (float)SigCd,
            3 => comCd / (float)ComCd, _ => surgeCd / (float)SurgeCd
        };
        public override string SlotState(int s) => s switch
        {
            0 => act == Act.Charge ? $"{Missiles}/{MissileCap}" : "",
            1 => act == Act.DisBeam ? $"STEP {BeamStep}" : disCd > 0 ? Sec(disCd) : "ready",
            2 => sigCd > 0 ? Sec(sigCd) : Surging || Glyphs > 0 ? "ready" : "weak",
            3 => comCd > 0 ? Sec(comCd) : Surging || Glyphs > 0 ? "ready" : "weak",
            _ => Surging ? $"ACTIVE {SurgeSeconds:0.0}s" : surgeCd > 0 ? Sec(surgeCd) : Glyphs > 0 ? "ready" : "no glyphs"
        };
        static string Sec(int ticks) => (ticks / (float)Sim.Hz).ToString("0.0") + "s";
        public override string ClassState()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("GLYPHS ").Append(new string('◆', Glyphs)).Append(new string('◇', MaxGlyphs - Glyphs));
            if (Surging) Sep(sb).Append($"SURGE {SurgeSeconds:0.0}s");
            if (act == Act.Charge) Sep(sb).Append("MISSILES ").Append(new string('●', Missiles)).Append(new string('○', MissileCap - Missiles));
            if (act == Act.DisBeam) Sep(sb).Append("DISINTEGRATE ").Append(new string('▮', BeamStep)).Append(new string('▯', 3 - BeamStep));
            foreach (var c in comets) Sep(sb).Append($"COMET {c.Left / (float)Sim.Hz:0.0}s");
            return sb.ToString();
        }
        static System.Text.StringBuilder Sep(System.Text.StringBuilder sb) => sb.Length > 0 ? sb.Append("   ") : sb;
        public override string Tooltip(int s) => s switch
        {
            0 => "MAGIC MISSILE  [LMB]\nTap: one missile straight at the cursor (10).\nHold: summon up to 3 missiles (5 in Surge); release to fire them straight at the cursor (13 each).\nYou move at half speed while charging. Dashing drops the charge.",
            1 => "DISINTEGRATE  [2, hold]  - builds GLYPHS\nA beam you steer toward the cursor for up to 2 s, in 3 damage steps.\nEach step completed while the beam is on the boss carves a glyph (max 3). Cooldown 8 s.",
            2 => "RUNE SIGIL  [3]  - spends 1 glyph\nDraws a rune at the cursor (16 m) that explodes after 0.8 s (55) on the boss inside it (2.5 m).\nWithout a glyph the rune is faint and deals 35%. Cooldown 6 s.",
            3 => "ARCANE COMET  [4]  - spends 1 glyph\nDraws a rune at the cursor (20 m); the comet crashes into it 3 s later (90, 4 m; bullseye doubles).\nWithout a glyph it deals 35%. Cooldown 18 s.",
            _ => "ARCANE SURGE  [5]  - spends every glyph\n2 s of Surge per glyph: volleys summon 5 missiles, taps fire two,\nRune Sigil and Arcane Comet cost no glyph and arm or fall twice as fast. Cooldown 15 s.",
        };

        // test and showcase hook: hold n glyphs (shown arriving at their sockets)
        public void DebugSetGlyphs(int n)
        {
            n = Mathf.Clamp(n, 0, MaxGlyphs);
            while (Glyphs < n) { Glyphs++; W.Fx.GlyphGain(P, Glyphs, P.transform.position + Vector3.up * 1.1f); }
            if (Glyphs > n) Glyphs = n;
        }

        public override void ResetAll()
        {
            act = Act.None; t = 0; tapCd = disCd = sigCd = comCd = surgeCd = 0; buffered = -1;
            Missiles = 0; LastVolley = 0; beamTicks = 0; BeamDamage = 0; blinking = false; Glyphs = 0; surgeLeft = surgeTotal = 0;
            LastSigil = LastComet = 0; LastCometBullseye = false; LastSigilPowered = LastCometPowered = false; LastSurgeGlyphs = 0;
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
            W.Fx.ArcanistTick(P);   // keeps the glyph socket ring alive
            if (tapCd > 0) tapCd--; if (disCd > 0) disCd--; if (sigCd > 0) sigCd--; if (comCd > 0) comCd--; if (surgeCd > 0) surgeCd--;
            var b = W.Boss; bool live = b != null && b.Solid;
            for (int i = sigils.Count - 1; i >= 0; i--)
                if (--sigils[i].Left <= 0)
                {
                    var s = sigils[i]; sigils.RemoveAt(i);
                    bool hit = live && Sim.Flat(b.transform.position - s.Pos).magnitude <= SigRadius + b.BodyRadius;
                    int dmg = s.Powered ? SigDamage : Mathf.RoundToInt(SigDamage * Unpowered);
                    LastSigil = hit ? W.DamageBoss(dmg, b.transform.position + Vector3.up * b.HitHeight, s.Powered ? "Rune Sigil" : "Rune Sigil (no glyph)", false) : 0;
                    LastSigilPowered = s.Powered;
                    W.Fx.SigilExplode(s.Id, s.Pos, hit);
                }
            for (int i = comets.Count - 1; i >= 0; i--)
                if (--comets[i].Left <= 0)
                {
                    var c = comets[i]; comets.RemoveAt(i);
                    float d = live ? Sim.Flat(b.transform.position - c.Pos).magnitude : 999;
                    bool hit = live && d <= ComRadius + b.BodyRadius, bull = hit && d <= ComCore + 0.5f * b.BodyRadius;
                    int dmg = (bull ? ComDamage * 2 : ComDamage); if (!c.Powered) dmg = Mathf.RoundToInt(dmg * Unpowered);
                    LastComet = hit ? W.DamageBoss(dmg, b.transform.position + Vector3.up * b.HitHeight, (bull ? "Arcane Comet (bullseye)" : "Arcane Comet") + (c.Powered ? "" : " (no glyph)"), false) : 0;
                    LastCometBullseye = bull; LastCometPowered = c.Powered;
                    W.Fx.CometImpact(c.Id, c.Pos, hit, bull);
                }
            if (surgeLeft > 0 && --surgeLeft == 0) { surgeTotal = 0; W.Fx.SurgeEnd(P); }
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
                if (Want(4) && surgeCd == 0 && !Surging && Glyphs > 0) { buffered = -1; Begin(Act.Surge, "ArcaneSurge", SurgeCast); W.Fx.SurgeCharge(P, Glyphs, SurgeCast); }
                else if (Want(3) && comCd == 0) { buffered = -1; Begin(Act.Comet, "ArcaneComet", ComCast); }
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
                    int n = Mathf.Min(MissileCap, t / (Surging ? SurgeChargeStep : ChargeStep));
                    if (n != Missiles) { Missiles = n; W.Fx.MissileSummon(P, n); }
                    if (!inp.Held(0)) { if (Missiles > 0) BeginVolley(); else EndCharge(false); }
                    break;
                case Act.Volley:
                    MoveScale = 0.6f; FaceAim = t < 3;
                    if (t == 2) BeginFireVolley();
                    if (t >= 2 && (t - 2) % 3 == 0 && (t - 2) / 3 < volleyCount) FireVolleyDart((t - 2) / 3);   // 0.05 s apart: tak-tak-tak
                    if (t >= 2 + 3 * volleyCount) act = Act.None;
                    break;
                case Act.DisWind:
                    MoveScale = 0.2f; FaceAim = true;
                    if (t >= DisWind) { act = Act.DisBeam; t = 0; beamTicks = 0; stepHit = false; beamDir = AimDir(); P.Presenter.Overlay("DisintegrateHold", 1.0f, loop: true); W.Fx.BeamStart(P); BeamTick(); }
                    break;
                case Act.DisBeam:
                    MoveScale = 0.2f; FaceAim = false;
                    beamTicks++;
                    // the beam turns toward the cursor at a capped rate, and the body follows the beam
                    var want = AimDir();
                    beamDir = Vector3.RotateTowards(beamDir, want, DisTurn * Mathf.Deg2Rad, 0).normalized;
                    P.transform.rotation = Quaternion.LookRotation(beamDir);
                    BeamTick();
                    // each completed damage step that hit the boss carves a glyph
                    if (beamTicks % DisStepTicks == 0) { if (stepHit) GainGlyph(); stepHit = false; }
                    if (!inp.Held(1) || beamTicks >= DisMax) EndBeam(false);
                    break;
                case Act.DisEnd:
                    MoveScale = 0.4f; if (t >= DisEnd) act = Act.None; break;
                case Act.Sigil:
                    MoveScale = 0.5f; FaceAim = t < SigRelease;
                    if (t == SigRelease) PlaceSigil();
                    if (t >= SigCast) act = Act.None;
                    break;
                case Act.Comet:
                    MoveScale = 0.3f; FaceAim = t < ComRelease;
                    if (t == ComRelease) PlaceComet();
                    if (t >= ComCast) act = Act.None;
                    break;
                case Act.Surge:
                    MoveScale = 0.4f;
                    if (t == SurgeRelease) StartSurge();
                    if (t >= SurgeCast) act = Act.None;
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

        // ---- glyphs ----
        void GainGlyph()
        {
            if (Glyphs >= MaxGlyphs) return;
            Glyphs++; W.Fx.GlyphGain(P, Glyphs, beamContact);
        }
        // a rune spends one glyph for full power (free in Surge); returns whether it is powered
        bool SpendGlyph(Vector3 target)
        {
            if (Surging) return true;
            if (Glyphs <= 0) return false;
            Glyphs--; W.Fx.GlyphSpend(P, Glyphs, target); return true;
        }

        // ---- Magic Missile ----
        void FireTap()
        {
            tapCd = TapCd;
            var dir = AimDir(); var from = P.transform.position + HandOffset(P.transform);
            Fire(from, dir * MissileSpeed, MissileRange, TapDamage, "Magic Missile", false, 1, false);
            if (Surging)
            {
                // Surge: a tap fires two missiles side by side
                var side = Vector3.Cross(Vector3.up, dir) * 0.35f;
                Fire(from + side, dir * MissileSpeed, MissileRange, TapDamage, "Magic Missile", false, 2, true);
            }
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
        void BeginVolley() { act = Act.Volley; t = 0; P.Presenter.Overlay("MissileVolley", 9 / (float)Sim.Hz); }
        int volleyCount;
        void BeginFireVolley()
        {
            tapCd = TapCd; LastVolley = volleyCount = Missiles;
            W.Fx.MissileVolley(P, Missiles);
            Missiles = 0;
        }
        // each summoned dart leaves its spot in the fan and flies straight at the cursor (a little spread so they don't stack)
        void FireVolleyDart(int i)
        {
            var tr = P.transform;
            var at = tr.position + MissileSlot(tr, i, volleyCount);
            float side = volleyCount == 1 ? 0 : (i - (volleyCount - 1) / 2f) / ((volleyCount - 1) / 2f);
            var target = new Vector3(aim.x, 1.2f, aim.z) + Vector3.Cross(Vector3.up, AimDir()) * side * 0.4f;
            var dir = (target - at).normalized;
            Fire(at, dir * VolleySpeed, VolleyRange, VolleyDamage, "Magic Missile (charged)", true, i, Surging);
            W.Fx.MissileLaunch(P, i);
        }
        void Fire(Vector3 from, Vector3 vel, float range, int dmg, string ability, bool charged, int index, bool surged)
        {
            var pr = new ProjectileSystem.Projectile
            {
                Pos = from, Vel = vel, Range = range, Radius = 0.3f, Damage = dmg, Ability = ability, Color = PlayerFx.ArcLilac
            };
            W.Fx.Missile(pr, charged, index, surged);
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
                BeamDamage += dealt; hit = dealt > 0; if (hit) { stepHit = true; beamContact = at; }
            }
            else
            {
                // presentation only: where the beam would meet the boss this tick (no damage between damage ticks)
                var b = W.Boss;
                if (b != null && b.Solid)
                {
                    var to = Sim.Flat(b.transform.position - tr.position); float along = Vector3.Dot(to, beamDir), side = (to - beamDir * along).magnitude;
                    if (along > 0 && along < DisRange + b.BodyRadius && side < DisWidth / 2 + b.BodyRadius) { hit = true; at = tr.position + beamDir * Mathf.Max(0, along - b.BodyRadius) + Vector3.up * b.HitHeight; beamContact = at; }
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
            var at = ClampAim(SigRange); bool powered = SpendGlyph(at); int arm = Surging ? SigArmSurge : SigArm;
            var s = new Mark { Id = ++markIds, Pos = at, Left = arm, Total = arm, Powered = powered };
            sigils.Add(s); W.Fx.SigilPlace(P, s.Id, at, arm, powered);
        }

        // ---- Arcane Comet ----
        void PlaceComet()
        {
            comCd = ComCd;
            var at = ClampAim(ComRange); bool powered = SpendGlyph(at); int fall = Surging ? ComFallSurge : ComFall;
            var c = new Mark { Id = ++markIds, Pos = at, Left = fall, Total = fall, Powered = powered };
            comets.Add(c); W.Fx.CometMark(P, c.Id, at, fall, powered);
        }

        // ---- Arcane Surge ----
        void StartSurge()
        {
            surgeCd = SurgeCd - SurgeRelease;
            LastSurgeGlyphs = Glyphs; surgeTotal = surgeLeft = SurgePerGlyph * Glyphs; Glyphs = 0;
            W.Fx.SurgeStart(P, LastSurgeGlyphs, surgeTotal);
        }

        public override bool CanJump() => act == Act.None || act == Act.Tap || act == Act.BlinkIn;

        // Blink (shared dash): cancels unreleased casts. A Disintegrate windup or a Surge cast before its release refunds;
        // a running beam breaks (glyphs already carved are kept).
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
                case Act.Surge: if (t < SurgeRelease) W.Fx.SurgeCancel(P); break;
            }
            act = Act.None; Missiles = 0; Velocity = Vector3.zero; P.Presenter.EndOverlay();
        }
    }
}
