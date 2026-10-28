using UnityEngine;

namespace LastBell.Run
{
    // Chimesinger, approved DPS kit (#36): Resonant Note (Basic, builds Harmony), Bellstrike (2 charges, places / refreshes
    // and strengthens a ringing bell), Harmonic Refrain (wave; adds up to two stored echoes to a ringing bell),
    // Crescendo (spends Harmony, detonates the bell and its echoes), Mending Chorus (instant self-centred burst heal),
    // Harmonic Step = the shared dash. No ultimate. Healing and dashing never clear Harmony, bells or echoes; ordinary
    // expiry still applies. Every number below is a labelled prototype value (the handoff leaves them unspecified).
    public sealed class ChimesingerKit : PlayerKit
    {
        // ---- prototype tuning (ticks at 60 Hz) ----
        const int NoteCast = 10, NoteRelease = 6, NoteCd = 22, NoteDamage = 16, HarmonyMax = 5;
        const float NoteSpeed = 22f, NoteRange = 15f;
        const int StrikeCast = 18, StrikeRelease = 10, StrikeCharges = 2, StrikeRecharge = 360, StrikeDamage = 40;
        const float StrikeRange = 13f, StrikeWidth = 1.2f;
        const int BellLife = 480, MaxStrength = 2;         // bell lasts 8 s; up to two strengthening refreshes
        const float StrengthStep = 1.5f;                   // each refresh multiplies stored echo damage
        const int RefrainCast = 22, RefrainRelease = 12, RefrainCd = 240, RefrainDamage = 34, MaxEchoes = 2;
        const float RefrainRange = 7.5f, RefrainHalf = 32f, EchoFraction = 0.6f;
        const int CrescCast = 26, CrescRelease = 16, CrescCd = 300, CrescPerHarmony = 12, BellBurst = 50;
        const int ChorusCd = 900, ChorusHeal = 30, ChorusAnim = 36;
        const float ChorusRadius = 6f;

        enum Act { None, Note, Strike, Refrain, Cresc, Chorus }
        int buffered = -1, bufferUntil; const int BufferTicks = 9;   // PF-05 input buffer
        Act act; int t;
        Vector3 aim;
        int noteCd, refrainCd, crescCd, chorusCd, charges = StrikeCharges, recharge;
        public int Harmony { get; private set; }
        // the ringing bell (on the boss: there is one target per encounter)
        public bool Bell => bellLeft > 0;
        int bellLeft; public int Strength { get; private set; }
        readonly int[] echo = new int[MaxEchoes]; public int Echoes { get; private set; }
        public int LastCrescendo { get; private set; }

        public override string[] SlotNames => new[] { "Resonant Note", "Bellstrike", "Harmonic Refrain", "Crescendo", "Mending Chorus" };
        public override string[] SlotKeys => new[] { "LMB", "2", "3", "4", "5" };
        public override float Cooldown01(int s) => s switch
        {
            0 => noteCd / (float)NoteCd, 1 => charges > 0 ? 0 : recharge / (float)StrikeRecharge,
            2 => refrainCd / (float)RefrainCd, 3 => crescCd / (float)CrescCd, _ => chorusCd / (float)ChorusCd
        };
        public override string SlotState(int s) => s switch
        {
            0 => "",
            1 => $"{charges}/{StrikeCharges}" + (charges < StrikeCharges ? $" +{recharge / (float)Sim.Hz:0.0}s" : ""),
            2 => refrainCd > 0 ? Sec(refrainCd) : Bell ? "adds echo" : "ready",
            3 => crescCd > 0 ? Sec(crescCd) : (Harmony == 0 && !Bell ? "needs Harmony/bell" : "ready"),
            _ => chorusCd > 0 ? Sec(chorusCd) : "ready"
        };
        static string Sec(int ticks) => (ticks / (float)Sim.Hz).ToString("0.0") + "s";
        public override string Tooltip(int s) => s switch
        {
            0 => "RESONANT NOTE  [LMB, hold to repeat]\nRanged sound bolt toward the cursor. +1 HARMONY on hit (max 5).",
            1 => "BELLSTRIKE  [2]\nStrong aimed hit that rings a BELL on the target for 8 s. 2 charges, 6 s recharge each.\nOn a ringing bell: refreshes it and STRENGTHENS stored echoes (x1.5, up to twice).",
            2 => "HARMONIC REFRAIN  [3]\nDamaging sound wave in a cone. Hitting a ringing bell stores an ECHO worth 60% of the wave.\nMax 2 echoes per bell. Cooldown 4 s.",
            3 => "CRESCENDO  [4]\nSpends all HARMONY and detonates the bell and its echoes. Cooldown 5 s.\nEarly detonation pays out now; the full sequence pays far more.",
            _ => "MENDING CHORUS  [5]\nInstant burst heal for you and allies within 6 m: +30 HP, capped. No lingering zone. Cooldown 15 s.\nHealing and dashing never clear Harmony, bells or echoes.",
        };
        public override string ClassState()
        {
            string bell = Bell ? $"BELL {bellLeft / (float)Sim.Hz:0.0}s  strength {new string('+', Strength)}{(Strength == 0 ? "base" : "")}  echoes {Echoes}/{MaxEchoes}" : "no bell";
            return $"HARMONY {Harmony}/{HarmonyMax}   {bell}";
        }

        public override void ResetAll()
        {
            act = Act.None; t = 0; noteCd = refrainCd = crescCd = chorusCd = 0; charges = StrikeCharges; recharge = 0;
            Harmony = 0; ClearBell(); LastCrescendo = 0; buffered = -1;
            MoveScale = 1; Velocity = Vector3.zero; FaceAim = false;
        }

        void ClearBell() { bellLeft = 0; Strength = 0; Echoes = 0; echo[0] = echo[1] = 0; P.World.Fx.Bell(null, 0, 0, 0); }
        public override void ClearTargetState() => ClearBell();

        void Timers()
        {
            if (noteCd > 0) noteCd--; if (refrainCd > 0) refrainCd--; if (crescCd > 0) crescCd--; if (chorusCd > 0) chorusCd--;
            if (charges < StrikeCharges && --recharge <= 0) { charges++; recharge = charges < StrikeCharges ? StrikeRecharge : 0; }
            if (bellLeft > 0 && --bellLeft == 0) ClearBell();   // ordinary expiry
            var b = W.Boss;
            if (Bell && (b == null || !b.Solid)) ClearBell();    // target died
            if (Bell) W.Fx.Bell(b, Strength, Echoes, bellLeft / (float)BellLife);
        }
        public override void TickTimersOnly() { Timers(); Velocity = Vector3.zero; }

        public override void Step(PlayerInput inp)
        {
            Timers(); aim = inp.Aim; Velocity = Vector3.zero; MoveScale = 1; FaceAim = false;
            for (int i = 1; i <= 3; i++) if (inp.Pressed(i)) { buffered = i; bufferUntil = W.Tick + BufferTicks; }
            if (buffered >= 0 && W.Tick > bufferUntil) buffered = -1;   // absolute expiry: stunned or dashing ticks count too
            bool Want(int s) => inp.Pressed(s) || buffered == s;
            if (act == Act.None || act == Act.Chorus)
            {
                // the heal is instant on press and can be woven into anything that isn't mid-cast
                if (inp.Pressed(4) && chorusCd == 0) Chorus();
                if (act == Act.None)
                {
                    if (Want(3) && crescCd == 0 && (Harmony > 0 || Bell)) { buffered = -1; Begin(Act.Cresc, "Crescendo", CrescCast); }
                    else if (Want(2) && refrainCd == 0) { buffered = -1; Begin(Act.Refrain, "HarmonicRefrain", RefrainCast); }
                    else if (Want(1) && charges > 0) { buffered = -1; Begin(Act.Strike, "Bellstrike", StrikeCast); }
                    else if ((inp.Pressed(0) || inp.Held(0)) && noteCd == 0) Begin(Act.Note, "ResonantNote", NoteCast);
                }
            }
            if (act == Act.None) return;
            t++;
            switch (act)
            {
                case Act.Note:
                    MoveScale = 0.55f; FaceAim = true;
                    if (t == NoteRelease) FireNote();
                    if (t >= NoteCast) act = Act.None;
                    break;
                case Act.Strike:
                    MoveScale = 0.3f; FaceAim = t < StrikeRelease;
                    if (t == StrikeRelease) DoStrike();
                    if (t >= StrikeCast) act = Act.None;
                    break;
                case Act.Refrain:
                    MoveScale = 0.25f; FaceAim = t < RefrainRelease;
                    if (t == RefrainRelease) DoRefrain();
                    if (t >= RefrainCast) act = Act.None;
                    break;
                case Act.Cresc:
                    MoveScale = 0; FaceAim = t < CrescRelease;
                    if (t == CrescRelease) DoCrescendo();
                    if (t >= CrescCast) act = Act.None;
                    break;
                case Act.Chorus:
                    MoveScale = 0.6f;
                    if (t >= ChorusAnim) act = Act.None;
                    break;
            }
        }

        void Begin(Act a, string clip, int ticks) { act = a; t = 0; P.FaceNow(aim); P.Presenter.Action(clip, ticks); }

        void FireNote()
        {
            noteCd = NoteCd;
            var dir = Sim.Flat(aim - P.transform.position); dir = dir.sqrMagnitude > 0.1f ? dir.normalized : P.transform.forward;
            var start = P.transform.position + dir * 0.8f + Vector3.up * 1.3f;
            W.Fx.NoteCast(start, dir);
            W.Projectiles.Fire(new ProjectileSystem.Projectile
            {
                Pos = start, Vel = dir * NoteSpeed, Range = NoteRange, Radius = 0.35f, Damage = NoteDamage, Ability = "ResonantNote",
                Color = PlayerFx.NoteGold,
                OnHit = (dealt, at) => { if (dealt > 0) Harmony = Mathf.Min(HarmonyMax, Harmony + 1); }
            });
        }

        void DoStrike()
        {
            // charge is spent on release even if it misses (prototype decision)
            if (charges == StrikeCharges) recharge = StrikeRecharge;
            charges--;
            var dir = Sim.Flat(aim - P.transform.position); dir = dir.sqrMagnitude > 0.1f ? dir.normalized : P.transform.forward;
            int dealt = W.LineBoss(P.transform.position, dir, StrikeRange, StrikeWidth, StrikeDamage, "Bellstrike", out var contact);
            bool refresh = dealt > 0 && Bell;
            if (dealt > 0)
            {
                if (refresh && Strength < MaxStrength) { Strength++; for (int i = 0; i < Echoes; i++) echo[i] = Mathf.RoundToInt(echo[i] * StrengthStep); }
                bellLeft = BellLife;
            }
            W.Fx.Bellstrike(P.transform.position + Vector3.up * 1.2f, contact, dealt > 0, refresh);
        }

        void DoRefrain()
        {
            refrainCd = RefrainCd;
            int dealt = W.StrikeBoss(P, P.transform.position, P.transform.forward, RefrainRange, RefrainHalf * 2, RefrainDamage, "HarmonicRefrain");
            if (dealt > 0 && Bell && Echoes < MaxEchoes)
            {
                echo[Echoes++] = Mathf.RoundToInt(dealt * EchoFraction * Mathf.Pow(StrengthStep, Strength));
                W.Fx.EchoAdded(W.Boss, Echoes);
            }
            W.Fx.Refrain(P, RefrainRange, RefrainHalf, dealt > 0);
        }

        void DoCrescendo()
        {
            crescCd = CrescCd;
            int dmg = Harmony * CrescPerHarmony;
            bool full = Bell && Echoes == MaxEchoes && Strength > 0;
            if (Bell) { int e = 0; for (int i = 0; i < Echoes; i++) e += echo[i]; dmg += Mathf.RoundToInt(BellBurst * Mathf.Pow(StrengthStep, Strength)) + e; }
            Harmony = 0;
            var b = W.Boss; int dealt = 0;
            if (b != null && b.Solid && dmg > 0)
            {
                var at = b.transform.position + Vector3.up * b.HitHeight;
                // detonates every ringing bell anywhere in the arena: exactly once, here
                dealt = Bell ? W.DamageBoss(dmg, at, full ? "Crescendo (full)" : "Crescendo") : 0;
                if (!Bell && Sim.Flat(b.transform.position - P.transform.position).magnitude <= 6f + b.BodyRadius) dealt = W.DamageBoss(dmg, at, "Crescendo");
                W.Fx.Crescendo(b.transform.position, Bell, Echoes, full);
            }
            else W.Fx.Crescendo(P.transform.position + P.transform.forward * 2, false, 0, false);
            LastCrescendo = dealt;
            ClearBell();
        }

        void Chorus()
        {
            chorusCd = Shop.ItemHooks.Cooldown(P, "MendingChorus", ChorusCd);
            // immediate: heals the caster and every standing ally inside the radius, capped at max health
            foreach (var ally in W.Players)
                if (!ally.Down && Sim.Flat(ally.transform.position - P.transform.position).magnitude <= ChorusRadius) { ally.Heal(ChorusHeal); Shop.ItemHooks.ChorusTouched(P, ally); }
            W.Fx.Chorus(P.transform.position, ChorusRadius);
            if (act == Act.None) { act = Act.Chorus; t = 0; P.Presenter.Action("MendingChorus", ChorusAnim); }
        }

        public override bool CanJump() => act == Act.None || act == Act.Chorus;
        // Harmonic Step cancels an unreleased cast (nothing spent) and keeps Harmony, bells and echoes.
        public override void OnDash() { act = Act.None; Velocity = Vector3.zero; W.Fx.NoteTrail(P); }
        public override void Interrupt() { act = Act.None; Velocity = Vector3.zero; }

        // test hooks
        public int Charges => charges;
        public int EchoTotal { get { int e = 0; for (int i = 0; i < Echoes; i++) e += echo[i]; return e; } }   // stored echo damage (tests)
    }
}
