using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Tollbreaker, the berserker axe thrower who replaced the Vanguard (Chzy18, 29 Oct). Accepted build spec:
    // docs/proposals/tollbreaker-kit-and-rotation-2026-10-30.md. He carries two axes; a thrown axe is gone from its hand until
    // it comes back, so standing closer brings them back sooner ("closer is faster"). Twin Hurl (LMB: tap, or hold 0.5 s to
    // lodge), Grinding Toll (2), Splitting Throw (3: a lodged split gives HIS hits +40 %, personal), Call of Iron (4: recall
    // every lodged axe through the boss), Bell Frenzy (5, 30 s), shared dash only. Every ability aims at the cursor; nothing
    // seeks the boss; no block, parry or invincibility. Damage numbers are TUNE.
    public sealed class TollbreakerKit : PlayerKit
    {
        // ---- tuning (ticks at 60 Hz) ----
        // Twin Hurl: 0.17 s cast, 0.25 s from one release to the next; hold 0.5 s for the heavier throw that lodges
        const int CastTicks = 10, ThrowInterval = 15, HoldTicks = 30, BufferTicks = 9;
        const float ThrowSpeed = 22f, ReturnSpeed = 18f, RecallSpeed = 30f, MaxRange = 9f, MinLodge = 1.5f, AxeRadius = 0.35f, CatchRadius = 0.6f;
        const int TapOut = 14, TapBack = 3, HeavyDamage = 30;   // return hit 6 -> 3 (Twin Hurl and Grinding Toll): option B, Chzy18 1 Nov
        const int BossLodgeTicks = 360, FloorLodgeTicks = 480;                            // lodged axes time out (6 s / 8 s)
        // Grinding Toll: an axe thrown to the cursor spins in place for 2 s, 1.5 m across, hitting every 0.4 s
        const int TollCd = 480, TollCast = 8, TollSpinTicks = 120, TollEvery = 24, TollDamage = 10;   // TUNE
        const float TollRadius = 0.75f;
        // Splitting Throw: 0.4 s planted windup, heavy throw that lodges; in the boss it splits his plating for 6 s
        const int SplitCd = 600, SplitWindup = 24, SplitTotal = 34, SplitDamage = 50, SplitTicks = 360;   // TUNE
        public const float SplitBonus = 1.4f;
        // Call of Iron: every lodged axe flies straight back; crossing the boss hits 20 + 8 per metre travelled, max 110
        const int CallCd = 360, CallBase = 20, CallMax = 110; const float CallPerMetre = 8f;   // TUNE
        // Bell Frenzy: 4 s; the thrown axe is back the instant it hits, each tap throws a fan of 3 (sides 50 %)
        const int FrenzyCd = 1800, FrenzyTicks = 240, FrenzyCast = 18; const float FanAngle = 14f, FanSide = 0.5f;

        // ---- run items (docs/proposals/tollbreaker-items-2026-10-31.md): numbers the shop may override (Shop.ItemHooks keys
        // "t.*"; no items = the constants above), the events it listens to, and what the damage hook can read (Hitting)
        public System.Collections.Generic.Dictionary<string, float> TestTuning;   // autotest only: forces a "t.*" value without a shop build
        float N(string key, float v) => TestTuning != null && TestTuning.TryGetValue(key, out var x) ? x : Shop.ItemHooks.F(P, key, v);
        int NI(string key, int v) => Mathf.RoundToInt(N(key, v));
        int SplitCdNow => NI("t.split.cd", SplitCd);                 // Short Fuse
        float TollR => N("t.toll.radius", TollRadius);                // Wide Grind
        int TollSpinNow => NI("t.toll.spin", TollSpinTicks);         // Long Grind
        int TollEveryNow => Mathf.Max(1, NI("t.toll.every", TollEvery));   // Quick Grind
        float ReturnSpeedNow => N("t.return.speed", ReturnSpeed);    // Quick Catch
        int TapBackNow => NI("t.tap.back", TapBack);                 // Backswing (Twin Hurl returns only)
        int TollBackNow => NI("t.toll.back", TapBack);               // Grinding Toll's return (same base, its own key)
        float CallPerMetreNow => N("t.call.per_m", CallPerMetre);    // Iron Pull
        int CallMaxNow => NI("t.call.max", CallMax);                 // Iron Pull
        int CallCdNow => NI("t.call.cd", CallCd);                    // Rooted Axe
        int SplitTicksNow => NI("t.split.ticks", SplitTicks);        // Deep Split
        int BossLodgeNow => NI("t.lodge.boss", BossLodgeTicks);      // Deep Split
        int FloorLodgeNow => NI("t.lodge.floor", FloorLodgeTicks);   // Anchor Line
        int FrenzyCdNow => NI("t.frenzy.cd", FrenzyCd);              // Frenzied Hour
        int FrenzyTicksNow => NI("t.frenzy.ticks", FrenzyTicks);     // Frenzied Hour
        float FanSideNow => N("t.fan.side", FanSide);                // Frenzied Hour
        float HeldRangeNow => N("t.held.range", MaxRange);           // Long Toss
        int HeldDamageNow => NI("t.held.damage", HeavyDamage);       // Long Toss
        public float SplitBonusNow => N("t.split.bonus", SplitBonus);   // Rooted Axe
        // behaviour switches (0 = off): Twin Split (second axe's damage %), Rooted Axe, Battle Spin
        float TwinSplitPct => N("t.twin_split", 0);
        bool Rooted => N("t.rooted", 0) > 0;
        bool BattleSpinOn => N("t.battle_spin", 0) > 0;
        bool battleSpin;   // Battle Spin armed by a dash end, spent by the next tap

        // what the hit about to land is (set just before every Twin Hurl, Grinding Toll, Splitting Throw and Call of Iron
        // hit): Close Quarters / Berserker's Pace read the distances, Anchor Line the floor recall, Riven Iron the recall
        public sealed class HitContext
        {
            public string Ability; public float ReleaseDistance, Distance;
            public bool Recall, Return, FromFloor, Heavy, BattleSpin;   // Return: a non-recall axe flying back (Keen Edge skips it)
        }
        public readonly HitContext Hitting = new HitContext();
        // Ringing Return: the shop takes ticks off a cooldown (slot 1-4) on the recall-hit event
        public void ReduceCooldown(int slot, int ticks)
        {
            if (slot == 1) tollCd = Mathf.Max(0, tollCd - ticks); else if (slot == 2) splitCd = Mathf.Max(0, splitCd - ticks);
            else if (slot == 3) callCd = Mathf.Max(0, callCd - ticks); else if (slot == 4) frenzyCd = Mathf.Max(0, frenzyCd - ticks);
        }
        float ToBoss(Vector3 from) => W.Boss != null && W.Boss.Solid ? Mathf.Max(0, Sim.Flat(W.Boss.transform.position - from).magnitude - W.Boss.BodyRadius) : float.MaxValue;

        public enum AxeState { Hand, Out, Back, Lodged, Spin, Recall }
        public sealed class Axe
        {
            public AxeState State;
            public Vector3 Pos, Dir; public float Travelled, Range;
            public int Damage, Life, Age; public string Ability;
            public bool Hit, Heavy, Splitting, Toll, Frenzy, Free, InBoss, FromFloor, BattleSpin;
            public float ReleaseDistance;   // his gap to the boss when it left his hand (Close Quarters)
            public Vector3 Offset;
            public bool InHand => State == AxeState.Hand;
            public bool Stuck => State == AxeState.Lodged || State == AxeState.Spin;
        }
        public readonly Axe[] Axes = { new Axe(), new Axe() };   // 0 right hand, 1 left hand

        enum Act { None, Throw, Toll, Split, Frenzy }
        Act act; int t, hand, lastHand = 1, nextThrow, buffered = -1, bufferUntil, emptyFlash;
        bool heavyShown, callArmed;
        int tollCd, splitCd, callCd, frenzyCd, splitLeft, frenzyLeft;
        Vector3 aim;

        public override bool Acting => act != Act.None;
        public int AxesInHand => (Axes[0].InHand ? 1 : 0) + (Axes[1].InHand ? 1 : 0);
        public int AxesStuck => (Axes[0].Stuck ? 1 : 0) + (Axes[1].Stuck ? 1 : 0);
        public bool SplitActive => splitLeft > 0;
        public bool FrenzyActive => frenzyLeft > 0;
        public int Throws { get; private set; }            // releases this encounter (tests)
        public int LastCallDamage { get; private set; }    // biggest Call of Iron hit of the last recall (tests)

        public override string[] SlotNames => new[] { "Twin Hurl", "Grinding Toll", "Splitting Throw", "Call of Iron", "Bell Frenzy" };
        public override string[] SlotKeys => new[] { "LMB", "2", "3", "4", "5" };
        public override float Cooldown01(int s) => s switch
        {
            1 => tollCd / (float)TollCd, 2 => splitCd / (float)Mathf.Max(1, SplitCdNow), 3 => callCd / (float)Mathf.Max(1, CallCdNow), 4 => frenzyCd / (float)Mathf.Max(1, FrenzyCdNow), _ => 0
        };
        public override string SlotState(int s) => s switch
        {
            0 => emptyFlash > 0 ? "NO AXE" : AxesInHand == 2 ? "" : AxesInHand + "/2",
            1 => tollCd > 0 ? Sec(tollCd) : AxesInHand == 0 ? "no axe" : "ready",
            2 => splitCd > 0 ? Sec(splitCd) : SplitActive ? "SPLIT" : AxesInHand == 0 ? "no axe" : "ready",
            3 => callCd > 0 ? Sec(callCd) : AxesStuck == 0 ? "none lodged" : "ready",
            4 => FrenzyActive ? "FRENZY" : frenzyCd > 0 ? Sec(frenzyCd) : "ready",
            _ => ""
        };
        static string Sec(int ticks) => (ticks / (float)Sim.Hz).ToString("0.0") + "s";
        public override string Tooltip(int s) => s switch
        {
            0 => "TWIN HURL  [LMB]\nTap: throw the next axe at the cursor; it flies back to your hand, hitting going out and coming back.\nThe closer you stand, the sooner it's back. Hold 0.5 s: a heavier throw that lodges in the boss or the floor.",
            1 => "GRINDING TOLL  [2]\nThrow an axe to the cursor, where it spins in place for 2 s and grinds anything inside its ring.\nCooldown 8 s. It never moves toward the boss: place it where he stands. Then it flies back to your hand.",
            2 => "SPLITTING THROW  [3]\nPlanted 0.4 s windup, then a heavy throw that lodges where it lands.\nLodged in the boss: your Twin Hurl and Grinding Toll hits deal +40% for 6 s. Cooldown 10 s.",
            3 => "CALL OF IRON  [4]\nEvery lodged axe flies straight back to your hand. An axe crossing the boss hits harder the further it flew.\nCooldown 6 s. Needs a lodged or spinning axe. Hold the key to see the return paths.",
            _ => "BELL FRENZY  [5]\nFor 4 s each Twin Hurl throws a fan of three axes, and the thrown axe is back the instant it hits.\nCooldown 30 s. Lodged axes come back for free when it starts.",
        };
        string Pip(Axe a) => a.State switch { AxeState.Hand => "in hand", AxeState.Lodged => "lodged", AxeState.Spin => "spinning", _ => "flying" };
        public override string ClassState()
        {
            var s = "";
            if (SplitActive) s += $"SPLIT {splitLeft / (float)Sim.Hz:0.0}s  ";
            if (FrenzyActive) s += $"FRENZY {frenzyLeft / (float)Sim.Hz:0.0}s  ";
            if (AxesInHand < 2 || s.Length > 0) s += $"AXES  R {Pip(Axes[0])} / L {Pip(Axes[1])}";
            return s.Trim();
        }

        // bots (ported from the Vanguard, 2 Nov): how long a press slows its thrower, and how much. Twin Hurl x0.6 for its cast,
        // Grinding Toll and Bell Frenzy x0.5, Splitting Throw roots for its whole windup and throw; Call of Iron is instant
        public override float SlowLeft => act switch
        {
            Act.Throw => Mathf.Max(0, CastTicks - t) / (float)Sim.Hz, Act.Toll => (TollCast - t) / (float)Sim.Hz,
            Act.Split => (SplitTotal - t) / (float)Sim.Hz, Act.Frenzy => (FrenzyCast - t) / (float)Sim.Hz, _ => 0
        };
        public override float RootFor(int slot)
        {
            if (slot < 0) return Mathf.Max(Mathf.Max(RootFor(0), RootFor(1)), Mathf.Max(RootFor(2), RootFor(4)));
            return slot switch
            {
                0 => SlowLeft + CastTicks / (float)Sim.Hz,
                1 => tollCd > 0 ? 0 : SlowLeft + TollCast / (float)Sim.Hz,
                2 => splitCd > 0 ? 0 : SlowLeft + SplitTotal / (float)Sim.Hz,
                4 => frenzyCd > 0 ? 0 : SlowLeft + FrenzyCast / (float)Sim.Hz,
                _ => 0
            };
        }
        public override float RootScale(int slot) => slot switch { 0 => 0.6f, 1 => 0.5f, 4 => 0.5f, 3 => 1, _ => 0 };

        public override void ResetAll()
        {
            act = Act.None; t = 0; buffered = -1; nextThrow = 0; emptyFlash = 0; callArmed = false; lastHand = 1;
            tollCd = splitCd = callCd = frenzyCd = splitLeft = frenzyLeft = 0; Throws = 0; LastCallDamage = 0; battleSpin = false;
            foreach (var a in Axes) Catch(a);
            MoveScale = 1; Velocity = Vector3.zero; FaceAim = false;
            Vis.Reset(this);
        }

        void Timers()
        {
            if (tollCd > 0) tollCd--; if (splitCd > 0) splitCd--; if (callCd > 0) callCd--; if (frenzyCd > 0) frenzyCd--;
            if (splitLeft > 0) splitLeft--; if (frenzyLeft > 0) frenzyLeft--; if (emptyFlash > 0) emptyFlash--;
            for (int i = 0; i < 2; i++) StepAxe(Axes[i]);
            if (Rooted) foreach (var a in Axes) if (a.Splitting && a.State == AxeState.Lodged && a.InBoss) splitLeft = Mathf.Max(splitLeft, 2);
            Vis.Draw(this, callArmed);
        }

        public override void TickTimersOnly() { Timers(); Velocity = Vector3.zero; }

        public override void Step(PlayerInput inp)
        {
            Timers();
            aim = inp.Aim;
            Velocity = Vector3.zero; MoveScale = 1; FaceAim = false;
            for (int i = 0; i < PlayerInput.Slots; i++) if (inp.Pressed(i)) { buffered = i; bufferUntil = W.Tick + BufferTicks; }
            if (buffered >= 0 && W.Tick > bufferUntil) buffered = -1;
            // Call of Iron previews its paths while the key is held and fires on release (a tap fires at once)
            if (callArmed && !inp.Held(3)) { callArmed = false; Recall(); }
            switch (act)
            {
                case Act.None: Idle(inp); break;
                case Act.Throw: Throw(inp); break;
                case Act.Toll: Toll(); break;
                case Act.Split: Split(); break;
                case Act.Frenzy: t++; MoveScale = 0.5f; if (t >= FrenzyCast) act = Act.None; break;
            }
        }

        bool Want(int s) { if (buffered != s) return false; buffered = -1; return true; }
        int ReadyHand() => Axes[1 - lastHand].InHand ? 1 - lastHand : Axes[lastHand].InHand ? lastHand : -1;

        void Idle(PlayerInput inp)
        {
            if (buffered == 4 && frenzyCd == 0) { Want(4); StartFrenzy(); return; }
            if (buffered == 3 && callCd == 0 && AxesStuck > 0) { Want(3); callArmed = true; if (!inp.Held(3)) { callArmed = false; Recall(); } return; }
            if (buffered == 2 && splitCd == 0 && ReadyHand() >= 0) { Want(2); Begin(Act.Split, ReadyHand()); P.Presenter.Action("SplittingThrow", SplitTotal); return; }
            if (buffered == 1 && tollCd == 0 && ReadyHand() >= 0) { Want(1); Begin(Act.Toll, ReadyHand()); P.Presenter.Action("GrindingToll", TollCast + 8); return; }
            if (buffered == 0 && W.Tick >= nextThrow)
            {
                int h = ReadyHand();
                if (h < 0) { emptyFlash = 30; return; }   // both axes out: the press stays buffered for 0.15 s in case one lands
                Want(0); Begin(Act.Throw, h); heavyShown = false;
                P.Presenter.Action(h == 0 ? "TwinHurl" : "TwinHurl2", CastTicks + 4);
            }
        }

        void Begin(Act a, int h) { act = a; t = 0; hand = h; P.FaceNow(aim); }

        void Throw(PlayerInput inp)
        {
            t++; FaceAim = true; MoveScale = 0.6f;
            if (!Axes[hand].InHand) { act = Act.None; return; }
            bool held = inp.Held(0);
            if (t >= CastTicks && !held) { Release(hand, false); return; }
            if (held && t >= HoldTicks) { Release(hand, true); return; }
            if (held && t == CastTicks + 2 && !heavyShown) { heavyShown = true; P.Presenter.Action("HurlHeavy", HoldTicks - CastTicks + 8); }
        }

        void Toll()
        {
            t++; FaceAim = true; MoveScale = 0.5f;
            if (t < TollCast) return;
            if (!Axes[hand].InHand) { act = Act.None; return; }
            var a = Launch(hand, Mathf.Clamp(Sim.Flat(aim - P.transform.position).magnitude, MinLodge, MaxRange), 0, "Grinding Toll");
            a.Toll = true; tollCd = Shop.ItemHooks.Cooldown(P, "GrindingToll", TollCd);
            act = Act.None;
        }

        void Split()
        {
            t++; MoveScale = 0;
            if (t < SplitWindup) { FaceAim = true; return; }
            if (t == SplitWindup && Axes[hand].InHand)
            {
                float range = Mathf.Clamp(Sim.Flat(aim - P.transform.position).magnitude, MinLodge, MaxRange);
                bool both = TwinSplitPct > 0 && Axes[1 - hand].InHand;   // Twin Split: both axes, the second for its % (no extra split)
                var a = Launch(hand, range, SplitDamage, "Splitting Throw");
                a.Splitting = true; splitCd = Shop.ItemHooks.Cooldown(P, "SplittingThrow", SplitCdNow);
                if (both)
                {
                    var b2 = Launch(1 - hand, range, Mathf.RoundToInt(SplitDamage * TwinSplitPct / 100f), "Splitting Throw");
                    b2.Splitting = true; b2.Pos = a.Pos + Vector3.Cross(Vector3.up, a.Dir) * (hand == 0 ? -0.45f : 0.45f); lastHand = hand;
                }
            }
            if (t >= SplitTotal) act = Act.None;
        }

        void StartFrenzy()
        {
            // lodged axes come back for free (no damage); axes in flight are caught too, so both hands are full
            foreach (var a in Axes) Catch(a);
            frenzyLeft = FrenzyTicksNow; frenzyCd = Shop.ItemHooks.Cooldown(P, "BellFrenzy", FrenzyCdNow);
            Shop.ItemHooks.Cast(P, "t.frenzy", P.transform.position, P.transform.forward, frenzyLeft, 0);   // War Cry (allies in range)
            act = Act.Frenzy; t = 0;
            P.Presenter.Action("BellFrenzy", FrenzyCast + 12);
            Impact(P.transform.position);
        }

        void Recall()
        {
            if (callCd > 0 || AxesStuck == 0) return;
            LastCallDamage = 0;
            foreach (var a in Axes)
                if (a.Stuck) { a.FromFloor = !a.InBoss; a.State = AxeState.Recall; a.Travelled = 0; a.Hit = false; a.Free = false; a.InBoss = false; a.Ability = "Call of Iron"; }
            callCd = Shop.ItemHooks.Cooldown(P, "CallOfIron", CallCdNow);
            P.Presenter.Action("CallOfIron", 20);
        }

        Vector3 HandPos(int h) => P.transform.position + Vector3.up * 1.3f + P.transform.right * (h == 0 ? 0.35f : -0.35f);
        Vector3 AimDir()
        {
            var d = Sim.Flat(aim - P.transform.position);
            return d.sqrMagnitude > 0.01f ? d.normalized : P.transform.forward;
        }

        void Release(int h, bool heavy)
        {
            act = Act.None;
            bool spin = !heavy && battleSpin; battleSpin = false;   // Battle Spin: this tap hits like the held throw, but doesn't lodge
            var a = Launch(h, heavy ? Mathf.Clamp(Sim.Flat(aim - P.transform.position).magnitude, MinLodge, HeldRangeNow) : MaxRange,
                heavy ? HeldDamageNow : spin ? HeavyDamage : TapOut, heavy ? "Twin Hurl (held)" : spin ? "Twin Hurl (battle spin)" : "Twin Hurl");
            a.Heavy = heavy; a.BattleSpin = spin; a.Frenzy = FrenzyActive && !heavy;
            Shop.ItemHooks.Cast(P, "t.release", a.Pos, a.Dir, a.ReleaseDistance, heavy ? 1 : 0);   // Close Quarters, Berserker's Pace
            nextThrow = W.Tick + Mathf.Max(0, ThrowInterval - CastTicks);
            if (a.Frenzy)
                for (int side = -1; side <= 1; side += 2)
                {
                    var dir = Quaternion.Euler(0, side * FanAngle, 0) * a.Dir;
                    W.Projectiles.Fire(new ProjectileSystem.Projectile
                    {
                        Pos = a.Pos, Vel = dir * ThrowSpeed, Range = MaxRange, Radius = AxeRadius,
                        Damage = Boosted(Mathf.RoundToInt(TapOut * FanSideNow)), Ability = "Twin Hurl (fan)", Color = PlayerFx.TollCyan
                    });
                }
        }

        Axe Launch(int h, float range, int damage, string ability)
        {
            var a = Axes[h]; lastHand = h; Throws++;
            a.State = AxeState.Out; a.Pos = HandPos(h); a.Dir = AimDir(); a.Travelled = 0; a.Range = range;
            a.Damage = damage; a.Ability = ability; a.Hit = a.Heavy = a.Splitting = a.Toll = a.Frenzy = a.Free = a.InBoss = a.FromFloor = a.BattleSpin = false; a.Age = 0;
            a.ReleaseDistance = ToBoss(P.transform.position);
            return a;
        }

        void Catch(Axe a) { a.State = AxeState.Hand; a.InBoss = false; a.Hit = false; a.Free = false; a.Toll = false; }

        // split bonus: personal, only this Tollbreaker's Twin Hurl and Grinding Toll hits on the boss
        int Boosted(int dmg) => SplitActive ? Mathf.RoundToInt(dmg * SplitBonusNow) : dmg;

        // what an axe at pos touches: a live boss part (Crucivar's castings) or the boss body
        bool Touch(Vector3 pos, float radius, out Vector3 contact, out BossPart part)
        {
            part = null; contact = pos;
            foreach (var q in W.Parts)
                if (q.Alive && Sim.Flat(pos - q.Pos).magnitude <= q.BodyRadius + radius)
                { part = q; contact = q.Pos + Sim.Flat(pos - q.Pos).normalized * q.BodyRadius + Vector3.up * q.HitHeight; return true; }
            var b = W.Boss;
            if (b == null || !b.Solid) return false;
            for (int i = 0; i < b.BodyCount; i++)   // the body and any extra bodies (the Ultimate's P4 gear cores) count as the boss
            {
                b.Body(i, out var c, out float r); if (r <= 0 || Sim.Flat(pos - c).magnitude > r + radius) continue;
                contact = c + Sim.Flat(pos - c).normalized * r + Vector3.up * b.HitHeight;
                return true;
            }
            return false;
        }
        void Context(Axe a, string ability, bool recall)
        {
            var h = Hitting; h.Ability = ability; h.Recall = recall; h.Return = !recall && (a.State == AxeState.Back); h.FromFloor = recall && a.FromFloor; h.Heavy = a.Heavy || a.Splitting;
            h.BattleSpin = a.BattleSpin && !recall; h.ReleaseDistance = a.ReleaseDistance; h.Distance = ToBoss(P.transform.position);
        }
        int Deal(int dmg, Vector3 contact, BossPart part, string ability, bool boostable)
        {
            if (part != null) return W.DamagePart(part, dmg, contact, ability);
            return W.DamageBoss(boostable ? Boosted(dmg) : dmg, contact, ability);
        }

        void StepAxe(Axe a)
        {
            a.Age++;
            switch (a.State)
            {
                case AxeState.Out:
                {
                    float step = ThrowSpeed * Sim.Dt; a.Pos += a.Dir * step; a.Travelled += step;
                    if (!a.Hit && Touch(a.Pos, AxeRadius, out var c, out var part))
                    {
                        a.Hit = true;
                        if (a.Damage > 0)
                        {
                            Context(a, a.Ability, false); Deal(a.Damage, c, part, a.Ability, !a.Splitting);
                            Impact(c);
                        }
                        if (a.Splitting || a.Heavy)
                        {
                            Lodge(a, part == null);
                            if (a.Splitting && part == null) splitLeft = Mathf.Max(splitLeft, SplitTicksNow);   // a second Twin Split axe doesn't stack
                            return;
                        }
                        if (a.Toll) { Spin(a); return; }
                        if (a.Frenzy) { Catch(a); return; }   // Bell Frenzy: back the instant it hits
                        Turn(a); return;
                    }
                    if (a.Travelled >= a.Range || !CombatWorld.InPlayerBound(a.Pos, 1))
                    {
                        if (a.Splitting || a.Heavy) Lodge(a, false);
                        else if (a.Toll) Spin(a);
                        else Turn(a);
                    }
                    break;
                }
                case AxeState.Back:
                case AxeState.Recall:
                {
                    bool recall = a.State == AxeState.Recall;
                    var to = HandPos(Hand(a)) - a.Pos; float step = (recall ? RecallSpeed : ReturnSpeedNow) * Sim.Dt;
                    if (to.magnitude <= Mathf.Max(CatchRadius, step)) { Catch(a); return; }
                    // touch before moving: an axe that turned on the boss is still against it on its first tick back
                    if (!a.Hit && !a.Free && Touch(a.Pos, AxeRadius, out var c, out var part))
                    {
                        a.Hit = true;
                        int dmg = recall ? Mathf.Min(CallMaxNow, Mathf.RoundToInt(CallBase + CallPerMetreNow * a.Travelled)) : a.Toll ? TollBackNow : TapBackNow;
                        string ab = recall ? "Call of Iron" : a.Toll ? "Grinding Toll" : "Twin Hurl";
                        Context(a, ab, recall); int dealt = Deal(dmg, c, part, ab, !recall);
                        if (recall)
                        {
                            LastCallDamage = Mathf.Max(LastCallDamage, dealt);
                            if (part == null) Shop.ItemHooks.Cast(P, "t.recall.hit", c, to.normalized, dealt, a.FromFloor ? 1 : 0);   // Ringing Return, Riven Iron
                        }
                        Impact(c);
                    }
                    a.Pos += to.normalized * step; a.Travelled += step;
                    break;
                }
                case AxeState.Lodged:
                {
                    var b = W.Boss;
                    if (a.InBoss)
                    {
                        if (b == null || !b.Solid) { a.InBoss = false; a.Life = 0; }
                        else a.Pos = b.transform.position + a.Offset;
                    }
                    if (--a.Life <= 0 && !(Rooted && a.Splitting && a.InBoss)) { a.State = AxeState.Back; a.Free = true; a.Hit = true; a.InBoss = false; }   // timed out: flies home, no damage (Rooted Axe: never while split)
                    break;
                }
                case AxeState.Spin:
                {
                    if (a.Age % TollEveryNow == 0 && Touch(a.Pos, TollR, out var c, out var part)) { Context(a, "Grinding Toll", false); Deal(TollDamage, c, part, "Grinding Toll", true); Impact(c); }
                    if (--a.Life <= 0) Turn(a);
                    break;
                }
            }
        }
        void Impact(Vector3 at) => W.Fx.NoteImpact(at, PlayerFx.TollCyan);   // stand-in hit spark (shared player motes)
        int Hand(Axe a) => a == Axes[0] ? 0 : 1;
        void Turn(Axe a) { a.State = AxeState.Back; a.Hit = false; a.Free = false; }
        void Spin(Axe a) { a.State = AxeState.Spin; a.Life = TollSpinNow; a.Age = 0; a.Pos.y = 0.5f; }
        void Lodge(Axe a, bool inBoss)
        {
            a.State = AxeState.Lodged; a.InBoss = inBoss && W.Boss != null && W.Boss.Solid;
            if (a.InBoss) a.Offset = a.Pos - W.Boss.transform.position;
            else
            {
                var p = W.Constrain(a.Pos, 0.2f); a.Pos = new Vector3(p.x, 0.25f, p.z);   // in the floor, inside the arena
            }
            a.Life = a.InBoss ? BossLodgeNow : FloorLodgeNow;
        }

        public override bool CanDash() => true;   // axes in flight or lodged keep going
        public override void OnDash() { Cancel(); }
        public override void OnDashEnd() { if (BattleSpinOn) battleSpin = true; }   // Battle Spin
        public override void Interrupt() { Cancel(); callArmed = false; }
        // an unreleased throw, windup or toll is dropped with nothing spent
        void Cancel() { if (act != Act.Frenzy) act = Act.None; Velocity = Vector3.zero; }

        // encounter over or the boss died: every axe is back in hand, the split ends
        public override void ClearTargetState()
        {
            foreach (var a in Axes) Catch(a);
            splitLeft = 0; callArmed = false;
            Vis.Draw(this, false);
        }

        // ---------------------------------------------------------------- presentation (stand-in, code-built)
        // Axes in flight, lodged or spinning are simple code-built axes (haft, steel head, cyan edge); Grinding Toll's reach,
        // Bell Frenzy and Call of Iron's return paths use thin dotted cyan rings and lines (friendly effects: never filled).
        // Nothing here decides a hit. Replaced when the Tollbreaker model and his effects land.
        readonly AxeVisuals Vis = new AxeVisuals();
        sealed class AxeVisuals
        {
            readonly Transform[] axes = new Transform[2];
            readonly List<Transform> dots = new List<Transform>();
            Transform root; Material dotMat; int used;
            static readonly Color Cyan = new Color(0.45f, 1.7f, 2.1f);

            bool Ready(TollbreakerKit k)
            {
                if (root) return true;
                if (k.P == null || k.W == null || k.W.Root == null) return false;
                root = new GameObject("Tollbreaker axes · " + k.P.name).transform; root.SetParent(k.W.Root, false);
                dots.Clear(); dotMat = null;
                for (int i = 0; i < 2; i++)
                {
                    var a = new GameObject(i == 0 ? "AxeR" : "AxeL").transform; a.SetParent(root, false);
                    Prim.Make(PrimitiveKind.Cylinder, a, new Vector3(0, -0.12f, 0), new Vector3(0.06f, 0.3f, 0.06f), new Color(0.32f, 0.22f, 0.15f));
                    Prim.Make(PrimitiveKind.Cube, a, new Vector3(0, 0.12f, 0.14f), new Vector3(0.05f, 0.3f, 0.26f), new Color(0.55f, 0.58f, 0.62f));
                    Prim.Make(PrimitiveKind.Cube, a, new Vector3(0, 0.12f, 0.28f), new Vector3(0.06f, 0.32f, 0.03f), Cyan, 1.4f);
                    a.gameObject.SetActive(false); axes[i] = a;
                }
                return true;
            }

            public void Reset(TollbreakerKit k)
            {
                foreach (var h in held) if (h) h.gameObject.SetActive(true);
                if (root) { foreach (var a in axes) if (a) a.gameObject.SetActive(false); foreach (var d in dots) if (d) d.gameObject.SetActive(false); }
            }

            Transform Dot()
            {
                if (used < dots.Count) { var d = dots[used++]; d.gameObject.SetActive(true); return d; }
                var t = Prim.Make(PrimitiveKind.Sphere, root, Vector3.zero, Vector3.one * 0.09f, Cyan, 1.6f);
                var mr = t.GetComponent<MeshRenderer>(); if (dotMat == null) dotMat = mr.sharedMaterial; else mr.sharedMaterial = dotMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dots.Add(t); used++; return t;
            }
            void Ring(Vector3 c, float r, int n, float phase)
            {
                for (int i = 0; i < n; i++)
                {
                    float ang = (i / (float)n) * Mathf.PI * 2 + phase;
                    Dot().position = new Vector3(c.x + Mathf.Cos(ang) * r, 0.06f, c.z + Mathf.Sin(ang) * r);
                }
            }
            void Line(Vector3 from, Vector3 to, float spacing)
            {
                var d = Sim.Flat(to - from); float len = d.magnitude; if (len < 0.1f) return;
                int n = Mathf.Min(40, Mathf.FloorToInt(len / spacing));
                for (int i = 1; i <= n; i++) { var p = from + d * (i / (float)(n + 1)); Dot().position = new Vector3(p.x, 0.06f, p.z); }
            }

            // the model's own axes ('AxeR' / 'AxeL' nodes in his fists): hidden while that axe is out of his hand
            readonly Transform[] held = new Transform[2]; bool heldLooked;
            void FindHeld(TollbreakerKit k)
            {
                if (heldLooked || k.P == null || k.P.Presenter == null || !k.P.Presenter.HasModel) return;
                heldLooked = true;
                foreach (var t in k.P.Presenter.Visual.GetComponentsInChildren<Transform>(true))
                    if (t.name == "AxeR") held[0] = t; else if (t.name == "AxeL") held[1] = t;
            }

            public void Draw(TollbreakerKit k, bool preview)
            {
                if (!Ready(k)) return;
                FindHeld(k);
                used = 0; float time = k.W.Tick / (float)Sim.Hz;
                for (int i = 0; i < 2; i++)
                {
                    var a = k.Axes[i]; var v = axes[i];
                    if (held[i] && held[i].gameObject.activeSelf != a.InHand) held[i].gameObject.SetActive(a.InHand);
                    bool show = a.State != AxeState.Hand; v.gameObject.SetActive(show);
                    if (!show) continue;
                    v.position = a.Pos;
                    switch (a.State)
                    {
                        case AxeState.Out: case AxeState.Back: case AxeState.Recall:
                            v.rotation = Quaternion.LookRotation(a.Dir.sqrMagnitude > 0 ? a.Dir : Vector3.forward) * Quaternion.Euler(time * 1440f, 0, 0); break;
                        case AxeState.Spin:
                            v.rotation = Quaternion.Euler(0, time * 1080f, 90); Ring(a.Pos, k.TollR, 16, time * 2); break;
                        default:   // lodged: blade buried, haft tilted up toward the thrower
                            v.rotation = Quaternion.LookRotation(a.Dir.sqrMagnitude > 0 ? a.Dir : Vector3.forward) * Quaternion.Euler(-60, 0, 0); break;
                    }
                }
                if (k.FrenzyActive) Ring(k.P.transform.position, 1.1f, 20, -time * 1.5f);
                if (preview) for (int i = 0; i < 2; i++) if (k.Axes[i].Stuck) Line(k.Axes[i].Pos, k.P.transform.position, 0.45f);
                for (int i = used; i < dots.Count; i++) if (dots[i].gameObject.activeSelf) dots[i].gameObject.SetActive(false);
            }

        }
    }
}
