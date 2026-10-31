# Prompt: make Aurelion much, much harder (ability set v3)

*A prompt for the Aurelion session, or a fresh session, in the Last Bell repo. Paste it as-is.*

## 1. Who you are and where things stand

You are the session for **Aurelion, the Meridian Regent**, the Skyward Spire area boss in Last Bell (Unity/C#; repo Risxhb/collab). The project root `C:\Users\BS_Ch\Documents\Codex\last bell` holds one git worktree per session.

**Today's boss:** ability set **v2.1**, built and passing the full autotest (587/0) on the local integration branch `feature/aurelion-abilities-v2`. The design is in `bosses/1-skyward-spire-aurelion/abilities-v2/ABILITIES.md` and `ABILITIES-v2.1.md`.

| Move | What it is |
|---|---|
| Meridian Lances | full-arena beams |
| Starfall Mirror | spread plus player mirror |
| Noon Shadow | half-arena cleave |
| Orrery Closing | rim + crown rings, then the lit "Horizon" edge |
| Zenith Collapse | everything except a disc at his feet |
| Hour Hands | clock spokes, the Hard named mechanic |

v2.1 added constant layering (two patterns most of the time, 0.3 s gaps) and Hard lock windows ×0.7.

**Player numbers:** HP 100. Potions: 5 × +40, 1 s between drinks, carried by the run, so effective HP is 300. Damage taken: Easy ×0.6, Hard ×1.3.

**Chzy18's verdict** (solo, ranged class, Hard, boss at 20 % HP via `-lastbellBossHp 0.2`):
- "way way too easy for hard difficulty. I had no problem dodging the mechanics"
- then, after v2.1: "still not hard enough solo"

**His instructions:**
- make it **much, much harder**
- **add new mechanics** if needed
- **if you have to replace mechanics we have now, do it**

**Bot allies exist:** run menu "Allies 0–7" or `-lastbellAllies N`. They are passive `Combat/AllyBot.cs` bots: they move, dodge after `reactTicks = 15 + (index×7)%10` and spread, and never attack.

## 2. Step 0: telemetry and a control measurement

**Log.** Add a per-run log (the autotest report plus `Application.persistentDataPath/aurelion-telemetry.csv`) of:
- hits taken per ability
- potions drunk
- time per phase
- each resolution's **escape margin in seconds**: (distance to the shape's edge at resolve) ÷ 6 m/s, negative = hit

**The control run:** the **reflex-perfect, memory-blind bot** (§7) solo on **v2.1 Hard** at 100 % HP for 3 loops.
- **Pass/fail:** if it takes **< 2 hits per loop**, the diagnosis below stands (reflexes beat v2.1).
- Record the numbers in the design doc.

## 3. Why v2.1 is still easy (confirm with Step 0)

1. **Every pattern is "see the shape, step out".** The telegraph shows the full answer. Nothing needs memory, planning ahead, or reading a number, mark or cue.
2. **Answers never conflict.** Paired patterns share one route.
3. **Speed isn't the lever.** Hard windows ×0.7 were still easy, because good reflexes answer any one shape. **Don't cut windows further** (rules §5: "don't uniformly speed attacks up").
4. **Ranged players stand still** for long stretches.
5. **Mistakes are cheap but uneven.** Zenith Collapse (45 × 1.3 = 58.5) is two hits to down, but chip hits of 30–35 × 1.3 plus 5 potions let you absorb about 7 mistakes per fight.
6. **Solo has no assignment load.**

## 4. Mechanics to draw on (research gathered; credit the source, make the design original)

From the Rabbit and Steel study (`research/rabbit-and-steel-boss-mechanics.md`):
- median telegraph 3.5 s; stack 4.35 s, cleave 4.0 s, clock spots 3.0–5.5 s
- **pairing:** one rule about **where you stand** plus one about **who stands together or apart**

The great WoW and FFXIV mechanics below are hard through **memory, ordering, conflict and assignment**, not speed. Every adaptation is rule-checked and has its solo and N rules. "Pattern" means a damaging pattern under the two-pattern limit.

| Source | Why it's hard | Aurelion adaptation (proposal) | Solo / N = 2, 4, 8 |
|---|---|---|---|
| **FFXIV Limit Cut** (TEA) | Ordering and memory | **Ecliptic Order.** Ivory numerals over heads (friendly identity). **All** beam lines show at once in **amber** (≥ 3 s total warning); each turns **coral 1.0 s before** firing through its numbered player, in order, 1.0 s apart. Each line's aim **snapshots when it turns coral**. Strike, 1.8 m wide. | **Solo:** I–IV sit on **4 fixed arena anchors**. They are shown together for 2 s and then **hidden**; lines then fire from the anchors **in numeral order**, so you must remember the order. N = 2: I–IV shared alternately. N = 4: I–IV. N = 8: I–VIII. |
| **FFXIV Exaflare** | The safe space moves; you plan a path | **Precession.** Star-chart fronts march inward from the rim, one 2.5 m ring-cell per 0.7 s, from 2 directions. Each cell shows ≥ 2 s ahead in amber, coral when locked. Ground; one pattern. | N = 1–4: 2 fronts. N = 8: the gap cells are 2 cells wide so 8 bodies fit, **plus a third front direction**, so co-op isn't easier. |
| **FFXIV Dynamo / Chariot** read ahead | A binary read stacked on a job | **Equinox / Solstice.** While one pattern resolves, the halo shows *in* or *out* for the **next** hit, with its own sound. The cue itself isn't a pattern; the hit it calls is. | The same for every N. |
| **FFXIV towers + tethers** (Omega Program Loop, DSR Light Rampant) | Rotating assignments; an empty soak wipes | **Meridian Towers.** Numbered soaks (empty = wipe, settled), with a loud chime and a big numeral, **introduced alone in Perfect Order**. Shares split by N; one player taking all shares survives from full. **A tower plus its tether fills both pattern slots**, so nothing else may overlap. | **Solo:** 2 towers in order, with a 4 s gap; a missed tower costs one full share (**50** at Medium, kill tier), not a wipe. **List this under Conflicts as a written solo rule** (an exception to "empty soaks wipe"). N = 2: 2 towers + 1 tether. N = 4: 2 + 2. N = 8: 4 + 4. |
| **WoW Kil'jaeden Shadow Reflections** | Your past action becomes a hazard | **Afterimage.** One **marked** player's path from 4 s ago replays as a **coral** ring trail (Ground), lasting ≤ 5 s. **It is a pattern**; never pair it with Starfall Mirror. | Solo: you are marked. N = 2–4: 1 marked. N = 8: 2 marked, to keep the screen readable. |
| **WoW Lich King Defile** | Placing a hazard well | **Sundial Brand.** A marked player drops a **one-hit** coral pool that **grows on a fixed schedule** to 3 m in ≤ 6 s, then expires. It blocks routes: the best drop is at the rim, which pulls a ranged player out of position. **A pool is a pattern.** | Solo: you are branded. N = 2–4: 1 brand. N = 8: 2 brands. |
| **FFXIV debuff resolution** (DSR Wroth Flames, TOP Hello World) | Your position must suit a later resolution | **Zenith Marks.** Each player gets spread, stack or "in shade", resolving with the **next** pattern; it counts as that pattern's partner, so never a third. | **Solo:** alternating **"in shade" / "out of shade"**, a true two-state read; no spread. N = 2: 1 stack pair. N = 4: 2 pairs or 2 + 2 spreads. N = 8: 2 stacks of 3 + 2 spreads. |

**Picking:** 3 mechanics at most for v3. At least 2 must be **memory or assignment** mechanics: Ecliptic Order, Meridian Towers or Zenith Marks.

**Default cut candidates** in v2.1:
- **Noon Shadow** and the plain **Orrery Closing**: pure "step out" moves. Rework or cut them.
- Keep **Meridian Lances' geometry** as the base for Ecliptic Order.
- Keep **Zenith Collapse** (come close).

**Slot budget per phase:**
- Perfect Order: 1 new mechanic alone, plus 1 v2.1 move at a time.
- Misalignment: 2-pattern pairings, one new + one old.
- Exposed Meridian (second form): conflict pairings.

**Hard named mechanic:** replace Hour Hands with whichever new mechanic is hardest to read (recommended: Afterimage or Sundial Brand, active on Hard only). Hour Hands moves to Medium or is cut. Say which in the doc.

## 5. Targets (Hard, solo, 100 % boss HP, any class), all tested

1. **Decision density.** A **decision** is a hazard resolution where the **scripted perfect route's** position at the previous resolution is unsafe, and the nearest safe region is **≥ 3 m away** or the straight line to it **crosses a live or locking hazard**. Computed from hazard geometry in the autotest. Target: **≥ 1 decision per 3.0 s** outside the punish window, **and at least 1.5 × v2.1's figure**, measured in Step 0.
2. **Memory / planning.** At least **2 mechanics** whose correct answer depends on information shown **before** the current hazard.
3. **Conflict.** At least **2 pairings per loop** where the straight escape from A crosses B, with a correct route existing (escape check).
4. **Melee and ranged:**
   - **Settled:** ≥ 3/4 of core attacks hit a player standing still next to the boss **and** one at ≥ 8 m.
   - **Project target (not settled):** a ranged player standing still for more than 3 s outside the punish window is hit.
   - Both are tested.
5. **Lethality.** Write damage at **Medium** (rules §10); Hard = ×1.3. Effective HP is 300 with potions.
   - **The mix:** choose a damage mix so that **300 ÷ (the mean Hard hit, weighted by the hits the memory-blind bot actually takes in the hazard log) ≤ 5 mistakes per fight**. That means a weighted mean Hard hit of at least 60, about 46 at Medium.
   - **Kill share:** at least **60 %** of logged bot hits must be kill-tier.
   - **Memory and assignment mechanics may be kill-tier** when their cue is shown at least 2 s ahead, with its own sound.
   - **Show the mix:** list it next to v2.1's figure (about 7).
   - **Tiers:** every hit is either **punish (≤ 30)** or **kill (≥ 50)** at Medium; **no values in between**. Zenith Collapse moves from 45 to **50**, or is reworked.
   - Show where in the loop a 1 s drink fits.
6. **Hit recovery.** A player hit at the worst moment (0.25 s stun, 1.5 m knockback) still has a legal route through the next pattern. **No guaranteed chain deaths.** This is tested.
7. **Bot proof, with Medium windows.** Add a test switch, **`-lastbellWindowsMedium`**: Hard damage and Hard mechanics, with Medium window lengths.
   - **Control:** the reflex-perfect, memory-blind bot (§7) on v2.1 Hard takes **< 2 hits per loop and survives 3 loops**. This is the same criterion as Step 0.
   - **Test:** the same bot **goes down within 2 loops of v3 Hard** with `-lastbellWindowsMedium`.
     - **It must die for the right reason:** log which hazard landed each hit. The hits that down it come from **at least 2 different memory or assignment mechanics**, with **none** from a soak, from Precession, or from the Hard-only named mechanic.
   - **Fairness:** a **per-block scripted perfect route** survives every v3 block.
8. **Margins.** After the fastest legal route (walk, sprint or dash, with finite stamina): **Hard ≥ 0.3 s, Medium ≥ 0.5 s, Easy ≥ 0.8 s**.
   - **Re-measure every v2.1 pairing and lengthen any window below that.** The 0.06 s sprint figure in `ABILITIES-v2.1.md` §2 was the value before that pairing was fixed (now 0.32 s).
   - Keep the project convention of **no total warning under 2 s** (a v2.1 convention, not a settled rule).
   - Then **check stamina over the whole loop timeline**.
9. **Co-op.** N = 2, 4 and 8 each at least as demanding as solo: assignments scale with N, not HP. Check:
   - **arena space:** 8 players plus towers and tethers in the 13.9 m bound, with the boss inside 9.5 m
   - the **assignment-follower bots** doing the jobs (§7)

**Difficulty levels:**
- The new core mechanics apply to **Medium and Hard**; this raises Medium. Put it under Conflicts, and make it **Open question 1** for Chzy18.
- **Easy:** the solo-safe subset with long windows.
- **Hard:** adds **one named extra mechanic** (provisional rule, `gpt/08-design-rules.md` §5).

## 6. Rules you must keep (`gpt/08-design-rules.md`; settled unless marked)

- **Survival:**
  - No iframes, grace windows or post-hit immunity.
  - No block, parry or counter (Brace −60 % on Strike only).
  - No teleporting players.
- **Patterns and hazards:**
  - **At most two damaging patterns at once.** Towers, tethers, pools, Afterimage and resolving marks all count.
  - One hit per hazard instance.
  - Pools always expire; no enrage or soft enrage.
- **Escape routes:** every attack has a real escape route at 4.2 and 6 m/s with finite stamina. Check it with `tools/encounter-escape/aurelion-v2-escape-check.ps1`, extended.
- **Co-op:**
  - Every co-op attack has a written solo rule; solo demands the same skill.
  - Empty soaks wipe; their telegraph must be impossible to miss, and introduced first.
  - One player taking every share survives from full.
- **Look:**
  - Hostile telegraphs are coral/amber with crisp real shapes.
  - Friendly identity is ivory numerals and brass/cobalt rims; decor never looks like a hazard.
- **Classes:** no mechanic may require a class or ability.
- **Phases:**
  - The second form is a harmless transformation: same HP bar, no heal, one queued transition.
  - A 3–4 s major punish window.
  - Three HP phases: **provisional**.
- **Provisional:**
  - slows and roots, if every route still checks at 4.2 m/s
  - a passed mark skips downed players
- **New ideas:** new statuses (Zenith Marks, numerals) are new: list them under Open questions.

## 7. Design craft (required)

- **Escalation:** Perfect Order introduces each new mechanic **alone** once; Misalignment pairs them; Exposed Meridian adds the conflict pairings.
- **Readability budget:** at most **one memory-cue type** on screen at a time (numerals, marks or in/out). Per-player cues of that type are fine. Each type has its own sound.
- **Variation without randomness:** 2–3 fixed loop orders, plus a per-pull pick of number or tower order from a small authored set. Written as a rule, never a randomness tax.
- **Damage profile:** label each mechanic punish or kill. Kill-tier mechanics must be the readable ones. Chip damage must never cause a co-op wipe.
- **Bots** (in `AllyBot`):
  - **reflex-perfect, memory-blind:** used for target 7.
    - It reacts on the tick **each hostile shape appears**, amber warnings included, not at lock.
    - It **enters any visible soak ring**.
    - It drinks potions as today.
    - It ignores **only** numerals, marks and in/out cues.
  - **assignment-follower:** perfect knowledge, takes its number, tower or mark. Used for target 9.
  - Keep today's passive mode as the default.
- **Rollback, decided up front:** if Chzy18 can't clear Hard in **15 pulls** at 100 % HP, relax in this order:
  1. Afterimage / Sundial / Precession uptime
  2. move one kill-tier mechanic down to punish tier (30)
  3. one conflict pairing

  **Never by shortening windows.**
- **Playtest success:** at 20 % HP on Hard, Chzy18 should go down at least once in his first 3 pulls, and the answer to "died without knowing why?" should be no.

## 8. Scope and milestones

- **Milestone A — this session alone; no new sessions:**
  - Step 0 telemetry and the control run
  - the diagnosis
  - **2 new mechanics** behind **`-lastbellAbilitySet v3`**: **Ecliptic Order and Zenith Marks**. The bot proof forbids soak downs, so Meridian Towers can't carry Milestone A.
  - **Memory must matter:** for every memory mechanic, the cue visible at resolve time (Ecliptic's coral line, the shade at resolve) appears **later than the reflex escape time from the worst spot + 0.25 s**. The early memory cue (the anchors, the mark) supplies the ≥ 2 s total warning.
    - Ecliptic's **amber lines are hidden with the anchors**, so only the numeral order tells you which line fires next.
  - their escape checks
  - the **decision-density test** and the **bot-proof control/test pair**
  - a playable build for Chzy18 (Hard, `-lastbellBossHp 0.2`, A for allies)
  - **The Milestone A interim loop:** the v2.1 phase plan, with the Noon Shadow and plain Orrery Closing slots replaced by the two new mechanics and every other block unchanged. Targets 1, 3, 5, 6, 7 and 8 are measured on this loop; targets 2, 4 and 9 wait for Milestone B.
- **Milestone B — split into ability sessions:**
  - the third mechanic, the full phase plan for targets 1–9, and the co-op assignment tests
  - use the existing tree: the Abilities session (`abilities-aurelion`) integrates on `feature/aurelion-abilities-v2`, with one ability session and brief per mechanic
- **Old set:** keep v2.1 reachable with `-lastbellAbilitySet v2`.
- **Builds:**
  - Unity runs in batch mode (`-lastbellAutotest`, plus `-lastbellAurelionOnly` for quick runs) under the **machine-wide build lock** (`last bell/.unity-build-lock`).
  - Binaries go through asset packs.
  - Commit locally with `git -c user.name=Chzy18 -c user.email=345140526+Chzy18@users.noreply.github.com`; push if GitHub works again.

## 9. Deliverables

1. **`bosses/1-skyward-spire-aurelion/abilities-v3/ABILITIES-v3.md`** ("proposal"):
   - the Step 0 numbers and the diagnosis
   - what was replaced, and why
   - each mechanic with:
     - fantasy and look (celestial orrery: ivory, gold, cobalt; hazards coral/amber)
     - shape and size; Strike or Ground
     - timings per level; Medium damage and tier
     - escape numbers; melee and ranged threat
     - solo and N = 1/2/4/8
     - the source, credited
     - the worst case
   - a phase plan with a timing table meeting targets 1–9
   - the Hard named mechanic
   - the damage mix (target 5)
   - Conflicts and Open questions
2. **Escape check:** the extended script; every new shape and pairing, and the loop-level stamina, at all three levels; all pass.
3. **Tests:** the standard ability checks, plus decision density, no-safe-spot, hit recovery, the bot-proof control/test, and N = 8 assignments.
4. **Playtest:** the build launched for Chzy18, and `abilities-v3/PLAYTEST.md` with:
   - what changed and what to watch for
   - the keys: F6 single ability, F8 phase, F9 defeat, A allies
   - the success questions (§7)
5. **Report:** end with Completed / Next / Blocked, with evidence.
