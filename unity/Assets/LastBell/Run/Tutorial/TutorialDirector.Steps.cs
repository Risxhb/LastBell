using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The tutorial's step list. Every step is a tick script (yield n = wait n combat ticks), run in order by Script().
    // Class sections: the Arcanist only for now; Tollbreaker and Chimesinger get a one-page note (Risxhb: skip them for now).
    public sealed partial class TutorialDirector
    {
        sealed class Step { public string Id, Section, Title; public Func<IEnumerable<int>> Run; public bool ArcanistOnly, OtherClassOnly; }
        Step[] steps;
        Step[] Steps => steps ?? (steps = BuildSteps());
        bool IsArcanist => Player && Player.Kit is ArcanistKit;
        bool Applies(Step s) => !(s.ArcanistOnly && !IsArcanist) && !(s.OtherClassOnly && IsArcanist);

        Step[] BuildSteps() => new[]
        {
            // ---- the basics ----
            S("welcome", "Basics", "Welcome to Last Bell", Welcome),
            S("move", "Basics", "Move", Move),
            S("dash", "Basics", "Dash", Dash),
            S("sprint", "Basics", "Sprint", Sprint),
            S("potion", "Basics", "Potions", Potion),
            // ---- class: Arcanist ----
            S("arcanist", "Arcanist", "The Arcanist", ArcanistIntro, arcanist: true),
            S("missile", "Arcanist", "Magic Missile", Missile, arcanist: true),
            S("disintegrate", "Arcanist", "Disintegrate: build glyphs", Disintegrate, arcanist: true),
            S("sigil", "Arcanist", "Rune Sigil: spend a glyph", Sigil, arcanist: true),
            S("comet", "Arcanist", "Arcane Comet: spend a glyph", Comet, arcanist: true),
            S("surge", "Arcanist", "Arcane Surge: cash in", Surge, arcanist: true),
            S("rotation", "Arcanist", "The rotation", Rotation, arcanist: true),
            S("class-later", "Class", "Class guide coming later", ClassLater, other: true),
            // ---- the Tempo Tyrant's own attacks ----
            S("tempo", "Tempo Tyrant", "Haste and Normal", TempoIntro),
            S("severance", "Tempo Tyrant", "Threefold Severance", () => BossDrill("Severance")),
            S("wake", "Tempo Tyrant", "Sundering Wake", () => BossDrill("Wake")),
            S("crown", "Tempo Tyrant", "Hollow Crown", () => BossDrill("Crown")),
            S("slices", "Tempo Tyrant", "Spiral Slices", () => BossDrill("Slices")),
            S("rush", "Tempo Tyrant", "Closing Rush", RushDrill),
            // ---- shared mechanics, with the bot allies ----
            S("allies", "Party", "Your allies", AlliesIntro),
            S("soak", "Party", "Soak towers", SoakDrill),
            S("spread", "Party", "Spread marks", SpreadDrill),
            S("burn", "Party", "Burning pools", BurnDrill),
            S("marks", "Party", "Shade and Sun marks", MarksDrill),
            S("tether", "Party", "Chains", TetherDrill),
            S("follow", "Party", "Following circles", FollowDrill),
            // ---- finale ----
            S("hours", "Finale", "Broken Cadence and Falling Hours", Hours),
            S("finish", "Finale", "Finish him", Finish),
        };
        static Step S(string id, string section, string title, Func<IEnumerable<int>> run, bool arcanist = false, bool other = false) =>
            new Step { Id = id, Section = section, Title = title, Run = run, ArcanistOnly = arcanist, OtherClassOnly = other };

        // the whole tutorial
        IEnumerable<int> Script()
        {
            yield return 1;
            var list = Steps;
            for (int i = 0; i < list.Length; i++)
            {
                var s = list[i];
                if (!Applies(s)) continue;
                StepIndex = i; StepNumber++; StepId = s.Id; Status = "";
                var rec = new Record { Id = s.Id, Title = s.Title, Tries = 1, Passed = true }; Records.Add(rec);
                current = rec;
                Debug.Log($"[tutorial] step {i + 1}/{list.Length} {s.Id}");
                foreach (var w in s.Run()) yield return w;
                Clear();
                if (Player) Player.DebugSetHp(PlayerController.MaxHp);
            }
        }
        Record current;

        // ---------------- building blocks ----------------
        IEnumerable<int> Page(string title, string body)
        {
            Show(Steps[StepIndex].Section, title, body, "");
            Snap(StepId);
            continuePressed = false; WaitingForContinue = true;
            while (!continuePressed) yield return 1;
            WaitingForContinue = false; continuePressed = false;
        }
        IEnumerable<int> Until(Func<bool> done) { while (!done()) yield return 1; }

        // One mechanic, up to MaxTries attempts: attempt() runs it, verdict() returns null when handled or the reason it wasn't.
        IEnumerable<int> Drill(string title, string body, Func<IEnumerable<int>> attempt, Func<string> verdict)
        {
            for (int t = 1; t <= MaxTries; t++)
            {
                current.Tries = t;
                Show(Steps[StepIndex].Section, title, body, t == 1 ? "" : $"Attempt {t} of {MaxTries}");
                if (Player) Player.DebugSetHp(PlayerController.MaxHp);
                StepHits.Clear(); PartyHits.Clear();
                yield return 45;   // a beat to read before it starts
                foreach (var w in attempt()) yield return w;
                string why = verdict();
                Clear();
                if (why == null)
                {
                    current.Passed = true; current.Note = t > 1 ? $"handled on attempt {t}" : "";
                    Status = "Handled. Well done!"; yield return 90; yield break;
                }
                current.Passed = false; current.Note = why;
                Status = why + (t < MaxTries ? " Let's try that again." : " Moving on: replay the tutorial any time to practise it.");
                yield return 160;
            }
        }
        string HitVerdict(Func<string, bool> counts = null)
        {
            var hit = StepHits.FirstOrDefault(x => counts == null || counts(x.source));
            return hit.source == null ? null : $"You were hit by {hit.source}.";
        }

        // The boss's real script for one block: ForcedNext picks the attack, BeginCombat restarts his script (he may walk in
        // or rush first), and he is halted again at the block boundary. Anything still on the floor finishes before the verdict.
        IEnumerable<int> BossBlock(string attack)
        {
            boss.ForcedNext = attack; int b0 = boss.Blocks, liveFor = 0; bool shot = false;
            boss.BeginCombat();
            for (int g = 0; g < 45 * Sim.Hz; g++)
            {
                if (boss.Blocks > b0 && boss.Current == "between blocks") break;
                if (W.Hazards.Count > 0) liveFor++;
                if (!shot && liveFor >= 40) { shot = true; Snap(StepId); }
                yield return 1;
            }
            boss.Halt();
            for (int g = 0; g < 6 * Sim.Hz && W.Hazards.Count > 0; g++) yield return 1;
        }
        IEnumerable<int> BossDrill(string attack)
        {
            string title = Steps[StepIndex].Title, body;
            switch (attack)
            {
                case "Severance": body = "He plants his feet and shows three numbered cuts: 1 left, 2 right, 3 his whole front half. They land in order, one beat apart. Stand where a cut has <b>already landed</b>, or get behind him."; break;
                case "Wake": body = "He charges down a lane at the farthest player, then swings behind where he lands (<b>Backswing</b>: the half circle behind him). The charge leaves a fissure that erupts a moment later. In Haste he charges twice, and a mirrored fissure appears on the other side. Leave the lane, don't stand behind his landing spot, stay off the fissures."; break;
                case "Crown": body = "<b>Hollow Crown</b>: first a ring lands everywhere except a pocket around him: get <b>in</b> close. Then the inside erupts: get <b>out</b>."; break;
                default: body = "The floor is cut into 12 slices. One slice is safe and it walks round the clock, one slice per beat. <b>Follow the safe slice.</b> Then every other slice lands: stand in one that isn't lit."; break;
            }
            return Drill(title, body, () => BossBlock(attack), () => HitVerdict());
        }

        // ---------------- basics ----------------
        IEnumerable<int> Welcome() => Page("Welcome to Last Bell",
            "Bosses here never hit at random. Every attack draws its shape on the floor first, and <b>the shape is exactly what gets hit</b>. Read the shape, step out, and punish.\n\nThis tutorial uses the Tempo Tyrant. He can't be defeated until you're told to finish him.");

        IEnumerable<int> Move()
        {
            Show("Basics", "Move", "Walk with <b>W A S D</b>. Go and stand in the gold ring.", "");
            var p = Player.transform.position;
            var goal = new Vector3(p.x > 0 ? p.x - 6f : p.x + 6f, 0, Mathf.Clamp(p.z + 2f, -9f, 9f));
            SetGoal(goal, 1.2f);
            yield return 30; Snap(StepId);
            foreach (var w in Until(AtGoal)) yield return w;
            Status = "Good."; SetGoal(null); yield return 45;
        }
        IEnumerable<int> Dash()
        {
            Show("Basics", "Dash", "<b>Right-click</b> to dash 3 m toward where you're moving (the Arcanist's dash is <b>Blink</b>). It costs stamina (the bar under your health) and gives <b>no invulnerability</b>: dash out of a shape, never through it.", "");
            yield return 30; Snap(StepId);
            foreach (var w in Until(() => Player.Dashing || (Player.Kit is ArcanistKit a && a.Blinking))) yield return w;
            Status = "Good."; yield return 60;
        }
        IEnumerable<int> Sprint()
        {
            Show("Basics", "Sprint", "Hold <b>Shift</b> while moving to sprint. It shares the stamina bar with your dash. There is no jump: every attack is dodged on foot. Sprint now.", "");
            yield return 30; Snap(StepId);
            foreach (var w in Until(() => Player.Presenter.Sprinting)) yield return w;
            Status = "Good."; yield return 60;
        }
        IEnumerable<int> Potion()
        {
            Player.DebugSetHp(55);
            int before = Player.PotionsDrunk;
            Show("Basics", "Potions", "You're hurt. Press <b>F</b> to drink a potion: +40 health. You carry 5 per fight, one drink per second.", "");
            yield return 30; Snap(StepId);
            foreach (var w in Until(() => Player.PotionsDrunk > before)) yield return w;
            Status = "Good."; yield return 60;
        }

        // ---------------- Arcanist ----------------
        ArcanistKit Kit => Player ? Player.Kit as ArcanistKit : null;
        IEnumerable<int> ArcanistIntro() => Page("The Arcanist",
            "A ranged caster built around <b>glyphs</b> (0 to 3, shown on your ability bar). <b>Disintegrate</b> builds glyphs; <b>Rune Sigil</b> and <b>Arcane Comet</b> spend them for full power; <b>Arcane Surge</b> cashes them all in. You have no defensive ability: your feet keep you alive.\n\nThe Tyrant will stand still while you practise.");
        IEnumerable<int> Missile()
        {
            Show("Arcanist", "Magic Missile", "Aim at the Tyrant with the mouse. <b>Tap left-click</b> to fire a missile. Then <b>hold left-click</b> to summon up to 3 missiles and <b>release</b> to send the volley.", "");
            yield return 30;
            bool charged = false, shot = false;
            while (true)
            {
                var k = Kit; if (k == null) yield break;
                if (k.Charging && k.Missiles >= 2) { charged = true; if (!shot) { shot = true; Snap(StepId); } }
                if (charged && !k.Charging && k.LastVolley >= 2) break;
                if (charged && !k.Charging && k.LastVolley < 2) { charged = false; Status = "Hold a little longer: wait for at least 2 missiles before releasing."; }
                yield return 1;
            }
            Status = $"Volley of {Kit.LastVolley}. Good."; yield return 60;
        }
        IEnumerable<int> Disintegrate()
        {
            Show("Arcanist", "Disintegrate: build glyphs", "<b>Hold 2</b> and keep the beam on him. The beam has 3 steps; each step that hits him carves a <b>glyph</b>. Carve at least 2. You walk slowly while channelling.", "");
            yield return 30;
            bool shot = false; int beamTicks = 0;
            while (true)
            {
                var k = Kit; if (k == null) yield break;
                if (k.BeamActive) { beamTicks++; if (!shot && beamTicks > 70) { shot = true; Snap(StepId); } }
                if (k.Glyphs >= 2 && !k.BeamActive) break;
                if (!k.BeamActive && beamTicks > 0 && k.Glyphs < 2) { beamTicks = 0; Status = "Keep holding 2 and keep the beam on him. Try again when Disintegrate is ready."; }
                yield return 1;
            }
            Status = $"{Kit.Glyphs} glyphs. Good."; yield return 60;
        }
        IEnumerable<int> Sigil()
        {
            if (Kit.Glyphs == 0) Kit.DebugSetGlyphs(1);
            Show("Arcanist", "Rune Sigil: spend a glyph", "Point the cursor at him and press <b>3</b>. The Rune Sigil lands under the cursor and explodes 0.8 s later. It spends 1 glyph for full power (without one it deals only 35%).", "");
            yield return 30; Snap(StepId);
            foreach (var w in Until(() => Kit.LastSigil > 0)) yield return w;
            Status = (Kit.LastSigilPowered ? "Full power: " : "Faint (no glyph): ") + Kit.LastSigil + " damage."; yield return 75;
        }
        IEnumerable<int> Comet()
        {
            if (Kit.Glyphs == 0) Kit.DebugSetGlyphs(1);
            Show("Arcanist", "Arcane Comet: spend a glyph", "Press <b>4</b> to mark a spot under him. The Comet falls <b>3 seconds</b> later; the bright centre hits hardest. It spends 1 glyph for full power. Long cooldown: make it count.", "");
            yield return 30;
            int t = 0;
            while (Kit.LastComet <= 0) { if (++t == 100) Snap(StepId); yield return 1; }
            Status = (Kit.LastCometBullseye ? "Bullseye! " : "") + Kit.LastComet + " damage."; yield return 75;
        }
        IEnumerable<int> Surge()
        {
            Show("Arcanist", "Arcane Surge: cash in", "Build glyphs again with <b>Disintegrate (2)</b>, then press <b>5</b>. Surge spends <b>every</b> glyph for 2 s of power each: runes cost nothing and arm twice as fast, and missiles fly in fives. Then fire everything.", "");
            yield return 30;
            foreach (var w in Until(() => Kit.Surging)) yield return w;
            Snap(StepId);
            Status = $"Surging with {Kit.LastSurgeGlyphs} glyph{(Kit.LastSurgeGlyphs == 1 ? "" : "s")}. Spend it!";
            foreach (var w in Until(() => !Kit.Surging)) yield return w;
            yield return 30;
        }
        IEnumerable<int> Rotation() => Page("The rotation",
            "1. Magic Missiles while Disintegrate cools down.\n2. Disintegrate on him: 3 glyphs.\n3. Spend them: Rune Sigil and Arcane Comet, or Surge to cash in all at once.\n\nAnd always: move first, cast second. A missed dodge costs more than a missed cast.");
        IEnumerable<int> ClassLater() => Page("Class guide coming later",
            $"A guided section for the {(Player ? Player.ClassName : "")} is coming later. For now, pick the <b>Arcanist</b> in Select Class to see a full class walkthrough. Everything else in this tutorial applies to every class.");

        // ---------------- the Tempo Tyrant ----------------
        IEnumerable<int> TempoIntro() => Page("Haste and Normal",
            "The Tempo Tyrant fights to a metronome. In <b>HASTE</b> (warm light, the tempo label top right) his warnings are short; in <b>NORMAL</b> they are longer: that's your opening. Before he switches you'll hear three accented ticks.\n\nNow he'll show you each attack, one at a time. Get through it without being hit.");

        IEnumerable<int> RushDrill()
        {
            const string body = "If you stay far from him, he announces a <b>CLOSING RUSH</b> and dashes back to you. It does no damage: it just means you can't kite him for free. Run to the gold ring, far from him, and wait.";
            int r0 = boss.Rushes;
            return Drill("Closing Rush", body, () => RushAttempt(), () => boss.Rushes > r0 ? null : "He didn't need to rush: you were too close to him.");
        }
        IEnumerable<int> RushAttempt()
        {
            var bp = Sim.Flat(boss.transform.position);
            var away = Sim.Flat(Player.transform.position - bp); if (away.sqrMagnitude < 1) away = Vector3.forward;
            var goal = away.normalized * (CombatWorld.PlayerBound - 1.6f);
            if (Sim.Flat(goal - bp).magnitude < 9f) goal = -bp.normalized * (CombatWorld.PlayerBound - 1.6f);
            SetGoal(goal, 1.4f);
            for (int i = 0; i < Allies.Count; i++) Jobs[Allies[i]] = bp + Sim.Dir(120f * i + 60f) * 3.2f;   // allies stay by him: you're the far one
            for (int g = 0; g < 30 * Sim.Hz && !AtGoal(); g++) yield return 1;
            yield return 20;
            boss.ForcedNext = "Crown"; int b0 = boss.Blocks; bool shot = false;
            boss.BeginCombat();
            for (int g = 0; g < 30 * Sim.Hz; g++)
            {
                if (!shot && boss.Current == "Closing Rush (announced)") { shot = true; Snap(StepId); }
                if (boss.Current == "Closing Rush (stopped)") break;
                if (boss.Blocks > b0 && boss.Current == "between blocks") break;
                yield return 1;
            }
            boss.Halt(); SetGoal(null);
            for (int g = 0; g < 6 * Sim.Hz && W.Hazards.Count > 0; g++) yield return 1;
        }

        // ---------------- shared mechanics with the allies ----------------
        IEnumerable<int> AlliesIntro() => Page("Your allies",
            "Most bosses also use <b>party mechanics</b>: towers to soak, marks to spread, chains, burning floors. Three bot allies join you for these. They walk to their jobs (blue rings); yours is shown in <b>gold</b>.");

        IEnumerable<int> SoakDrill()
        {
            Hazard a = null, b = null;
            return Drill("Soak towers",
                "A tower with a number needs <b>that many players inside</b> when it lands. The damage is split between everyone inside, so more players means less each. An <b>empty tower wipes the party</b> in a real fight. Stand in the gold-ringed tower with one ally; the other two take the second tower.",
                () => SoakAttempt(h => a = h, h => b = h),
                () =>
                {
                    if (a == null || b == null) return "The towers never landed.";
                    if (!a.Hit.Contains(Player) && !b.Hit.Contains(Player)) return "You weren't inside a tower when it landed.";
                    if (a.Hit.Count < 2 || b.Hit.Count < 2) return $"A tower was short of players ({a.Hit.Count}/2 and {b.Hit.Count}/2).";
                    return null;
                });
        }
        IEnumerable<int> SoakAttempt(Action<Hazard> setA, Action<Hazard> setB)
        {
            var A = new Vector3(-5f, 0, 4f); var B = new Vector3(5f, 0, 4f);
            int resolve = 7 * Sim.Hz;
            var ta = Tower(A, 2, 40, resolve); var tb = Tower(B, 2, 40, resolve); setA(ta); setB(tb);
            FloorLabels.Add((A, "2")); FloorLabels.Add((B, "2"));
            SetGoal(A, 1.0f, "YOU");
            Vector3[] spots = { A + new Vector3(0.9f, 0, 0.7f), B + new Vector3(-0.8f, 0, 0.6f), B + new Vector3(0.8f, 0, -0.6f) };
            for (int i = 0; i < Allies.Count; i++) Jobs[Allies[i]] = i < spots.Length ? spots[i] : new Vector3(0, 0, -10f + i);
            yield return 150; Snap(StepId);
            yield return resolve - 150 + 5;
        }

        IEnumerable<int> SpreadDrill() => Drill("Spread marks",
            "Everyone gets a <b>spread</b> circle that follows them, then locks and hits <b>everyone else</b> inside it. Spread out: keep your circle off your allies and stay out of theirs. Your spot is the gold ring.",
            SpreadAttempt,
            () => PartyHits.Any(x => x.source == "Spread mark · yours") ? "Your circle caught an ally." :
                  StepHits.Any(x => x.source.StartsWith("Spread")) ? "You stood in an ally's circle." : null);
        IEnumerable<int> SpreadAttempt()
        {
            var bp = Sim.Flat(boss.transform.position);
            var spots = new List<Vector3>();
            for (int i = 0; i < 4; i++) { var s = bp + Sim.Dir(45f + 90f * i) * 7.5f; spots.Add(Inside(s, 1.5f)); }
            var me = Player.transform.position;
            int mine = Enumerable.Range(0, 4).OrderBy(i => Sim.Flat(spots[i] - me).sqrMagnitude).First();
            SetGoal(spots[mine], 1.0f);
            int k = 0;
            for (int i = 0; i < Allies.Count; i++) { if (k == mine) k++; Jobs[Allies[i]] = k < 4 ? spots[k] : new Vector3(0, 0, -10f); k++; }
            foreach (var p in W.Players) if (!p.Down) { SpreadCircle(p, p == Player, 210, 270); p.Marks.Add(MechanicIcons.Spread); }
            yield return 150; Snap(StepId);
            yield return 130;
        }

        IEnumerable<int> BurnDrill() => Drill("Burning pools",
            "Molten pools appear under you. They warn first, then <b>burn every second</b> you stand in them, and they stay a while. Walk out, and route around them, not through them.",
            BurnAttempt,
            () => { int burn = StepHits.Where(x => x.source == "Molten Pool").Sum(x => x.dmg); return burn > 15 ? $"You took {burn} burn damage." : null; });
        IEnumerable<int> BurnAttempt()
        {
            Pool(Player.transform.position, 420);
            Pool(Inside(Player.transform.position + new Vector3(4.5f, 0, 1f), 2f), 420);
            foreach (var a in Allies) Pool(a.transform.position, 420);
            yield return 120; Snap(StepId);
            yield return 30;
            Pool(Player.transform.position, 330);
            Pool(Inside(Player.transform.position + new Vector3(-4f, 0, -2f), 2f), 330);
            yield return 340;
        }

        IEnumerable<int> MarksDrill() => Drill("Shade and Sun marks",
            "A word over your head is <b>your</b> job, and only you can be hit by it. <b>SHADE</b>: be within 5.5 m of the boss when it lands. <b>SUN</b>: be more than 8.5 m away. The shape shows the area that will hit you. Two rounds: Shade, then Sun.",
            MarksAttempt, () => HitVerdict(s => s.EndsWith("verdict")));
        IEnumerable<int> MarksAttempt()
        {
            for (int round = 0; round < 2; round++)
            {
                bool shade = round == 0; var bp = Sim.Flat(boss.transform.position);
                Labels[Player] = shade ? "SHADE · stay close" : "SUN · get away";
                Verdict(Player, shade, bp, 330);
                for (int i = 0; i < Allies.Count; i++)
                {
                    bool s = (i + round) % 2 == 0;
                    Labels[Allies[i]] = s ? "SHADE" : "SUN";
                    Jobs[Allies[i]] = s ? bp + Sim.Dir(90f * i + 20f) * 3.5f : Inside(bp + Sim.Dir(90f * i + 20f) * 10.5f, 1f);
                }
                yield return 150; Snap(StepId + (shade ? "-shade" : "-sun"));
                yield return 190;
                Labels.Clear(); Jobs.Clear();
                yield return 30;
            }
        }

        IEnumerable<int> TetherDrill() => Drill("Chains",
            "A chain ties you to a post. When it resolves, the chain must be <b>6 to 10 m</b> long: too short or too long and it snaps for damage. The chain glows <b>green</b> when its length is right.",
            TetherAttempt, () => HitVerdict(s => s == "Chain snap"));
        IEnumerable<int> TetherAttempt()
        {
            var dir = Sim.Flat(Player.transform.position); if (dir.sqrMagnitude < 1) dir = Vector3.forward;
            var post = dir.normalized * (CombatWorld.PlayerBound - 0.6f);
            ShowTether(post); Player.Marks.Add(MechanicIcons.StretchTether);
            Chain(Player, post, 420);
            yield return 180; Snap(StepId);
            yield return 245;
        }

        IEnumerable<int> FollowDrill() => Drill("Following circles",
            "A circle chases you, then <b>locks</b> (its edge brightens) and lands a moment later. Keep moving: once it locks, step out of it. Three in a row.",
            FollowAttempt, () => HitVerdict(s => s == "Following circle"));
        IEnumerable<int> FollowAttempt()
        {
            for (int n = 0; n < 3; n++) { Follower(Player, 120, 165); yield return 70; if (n == 0) Snap(StepId); }
            yield return 110;
        }

        // ---------------- finale ----------------
        IEnumerable<int> Hours()
        {
            foreach (var w in Page("Broken Cadence and Falling Hours",
                "Below 20% health he enters <b>Broken Cadence</b>: his attacks chain, each starting while the last is still landing. Below 10% <b>the Hours fall</b>: orbs drop onto the floor and burst into bouncing orbs faster and faster until he dies.\n\nHe'll take you there now. Survive one chain."))
                yield return w;
            Show("Finale", "Survive", "Dodge his chained attacks and the falling orbs.", "");
            boss.HpFloor = Mathf.CeilToInt(boss.MaxHp * 0.03f);
            boss.DebugSetHpFraction(0.09f);
            StepHits.Clear();
            int b0 = boss.Blocks; bool shot = false;
            boss.BeginCombat();
            for (int g = 0; g < 50 * Sim.Hz; g++)
            {
                if (!shot && boss.OrbsOn && W.Tick - boss.OrbsStartTick > 4 * Sim.Hz) { shot = true; Snap(StepId); }
                if (boss.OrbsOn && boss.Blocks > b0 && boss.Current == "between blocks") break;
                yield return 1;
            }
            boss.Halt();
            current.Note = $"{StepHits.Count} hits taken";
            Status = StepHits.Count == 0 ? "Not a scratch!" : $"You were hit {StepHits.Count} time{(StepHits.Count == 1 ? "" : "s")}. In a real fight there's no mercy heal.";
            yield return 120;
        }
        IEnumerable<int> Finish()
        {
            FinaleOpen = true; boss.HpFloor = 0;
            Show("Finale", "Finish him!", "He can fall now. Use everything you've learned: keep moving, read the shapes, and spend your glyphs.", "");
            boss.BeginCombat();
            yield return 90; Snap(StepId);
            while (!boss.Dead) yield return 1;
        }

        static Vector3 Inside(Vector3 p, float margin)
        {
            p = Sim.Flat(p); float lim = CombatWorld.PlayerBound - margin;
            return p.magnitude > lim ? p.normalized * lim : p;
        }
    }
}
