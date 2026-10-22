# REA prompt: Rabbit and Steel boss mechanics study

Paste everything below the line into a fresh agent session (Claude Code or Codex) opened at `C:\Users\BS_Ch\Documents\Codex\last bell`.

---

## Goal

Use REA to study the boss and miniboss mechanics in **Rabbit and Steel**, a co-op raid-style boss game. Write a reference report that Last Bell can borrow **ideas** from. Don't copy assets, text or code.

The output is research for design. It isn't a boss design. Any Last Bell idea you write is a **proposal**.

## Read first

1. `gpt/08-design-rules.md`: Last Bell's settled rules. Every idea you suggest must pass them. Rabbit and Steel breaks several of them; see "Known clashes" below.
2. `gpt/04-bosses.md`: the current Last Bell boss roster, so you can tell which ideas are new.
3. `research/windblown-evidence/`: an example of how earlier REA research was saved.

## Target

- Install: `E:\SteamLibrary\steamapps\common\Rabbit and Steel` (read only; never write, patch or move anything in it)
- Engine: GameMaker. `RabbitSteel.exe` (~63 MB) and `data.win` (~59 MB).
- `data.win` has a `GEN8` chunk but **no `CODE`, `VARI` or `FUNC` chunk**. That means it's a **YYC (compiled) build**: the boss logic is native x64 code inside `RabbitSteel.exe`, not GML bytecode. UndertaleModTool-style GML decompiling won't work. Confirm this first, and record the chunk list as evidence.
- Plain-text data you can read directly, with no reverse engineering needed:
  - `Data\Titles_Enemy.csv`: enemy and boss names. Keys look like `en_<area>_<name>`, with a `level` column for difficulty.
  - `Data\Descriptions_Move.csv` (~500 KB) and `Data\Names_Move.csv`: move names and descriptions. Player moves are `mv_<class>_N`. Look for enemy or boss move keys too.
  - `Data\Strings_Dialog*.csv`, `Data\Strings_Effect.csv` (status effects), `Data\Strings_Menu.csv` (difficulty and mode names).
  - The CSVs are UTF-8. Read the `English` column only.
- `EnAnimations\`: enemy sprite sheets. Use them only to identify bosses and their shapes and telegraph colours; don't copy them.
- `Mods\mod_readme.txt` mentions official mod support. If the game loads boss patterns from editable data, that's the richest source; check for it.

Boss keys already in `Titles_Enemy.csv` (one group per area, roughly):

- `en_wolf_*`
- `en_dragon_*`
- `en_mouse_*`
- `en_frog_*`
- `en_bird_*`
- `en_queens_*` / `en_rabbit_queen` / `en_rabbit_hero`
- `en_depths_*`
- `en_geode_*`
- `en_sanct_*`
- `en_aurum_*`
- `en_darkhall_spelllock`
- `en_heart_witch`
- `en_toybox_*`

## How to run REA

- Use the **local build**, not npx: `node rea/scripts/rea.mjs <command> --json`. If it isn't compiled yet, run `npm ci --ignore-scripts && npm run compile` inside `rea/` first.
- If `node` isn't found, refresh PATH from the Machine and User environment (Node 24 was installed with winget).
- Bash here has no `strings` or `python`; write small Node scripts for parsing CSVs and scanning bytes.
- Start with `node rea/scripts/rea.mjs doctor --json`. Then run `inspect-desktop-application` and `inspect-artifact` on the install folder and on `RabbitSteel.exe`.
- Then use `analyze` / `search` on `RabbitSteel.exe` for strings and procedure names (`bp_`, `pattern`, `en_`, `mv_`, `hbs`, `warning`, `spread`, `stack`, `tether`, `enrage`, `invuln`, `knockback` and similar), and `function` / `decompile` on the most promising functions.
- Static analysis only: **don't launch the game**, attach Frida or a debugger, or run it under REA's native-UI capture. Steam's DRM and anti-tamper rules apply.
- Keep each REA call bounded. If a command needs Ghidra or Rizin and that tool isn't installed, note it and keep going with what works. Don't install large tools without asking.
- Save raw REA JSON and extracted text lists under `research/rabbit-and-steel-evidence/`, as the Windblown study did. Cite those files in the report.

## What to find (focus: boss mechanics)

For **every boss and miniboss** you can identify, write down:

1. **Identity:** key, display name, area, and whether it's a miniboss, area boss or final boss. Note whether it fights alone or as a group (for example, the wolf pack or the Queen's weapons).
2. **Attack list:** for each attack, give:
   - its name
   - its shape (cone, line, ring, donut, circle, arena-wide or bullet pattern)
   - how it picks targets (nearest, random, everyone, marked player, or the player with aggro)
   - its warning time, if you can find it
   - its hit count
   - what it does on hit: damage, status or knockback
3. **Co-op mechanics:** this is the most important part. Look for:
   - spread (each player has to stand apart)
   - stack (share the damage)
   - tethers and links between players
   - soaks / towers that need a set number of players
   - marks that move from one player to the next
   - role swaps
   - "everyone must move" patterns
   - and anything that scales with player count
4. **Pattern structure:** whether attacks come in a fixed scripted order, a random draw or a looping timeline. Note phase thresholds and what changes at each phase. Note any final "enrage" or timer.
5. **Punish windows:** when the boss stops attacking, and for how long.
6. **Difficulty scaling:** what the `level` column and the difficulty modes change. Is it new mechanics, or only numbers?
7. **Telegraph language:** how warnings look (colour, fill-up timer, outline, sound), going by sprite names, effect names and descriptions.

Then write a cross-boss summary of the **recurring building blocks**, meaning the 10–20 mechanic types the game reuses. Show how it mixes two of them at once without the screen becoming unreadable.

## Known clashes with Last Bell rules

Rabbit and Steel is likely to use things Last Bell has ruled out. Record them accurately, but **don't suggest them** for Last Bell without saying so:

- **Invulnerability on Defensive moves or dashes, and post-hit immunity.** Last Bell has no iframes at all. Note how R&S tunes around its iframes, then say what Last Bell would need instead: a real escape route, a jump for low attacks, or Brace on Strike attacks only.
- **Fixed roles** (tank, healer, DPS) or mechanics that need a given class. In Last Bell, no encounter may require a class, a healer or Brace.
- **Enrage timers** or soft-enrage floor fill. Last Bell has none, and its pools expire.
- **Revive and downed rules, shops, upgrades and new resources.** Don't invent these for Last Bell. List them as open questions if an idea needs one.
- **Healing between fights or full refills.** Last Bell refills HP before each boss after the first. Potions carry over between minibosses and refill only before the area boss.
- **Overlap.** Last Bell allows at most two damaging patterns at once, and every boss needs a 3–4 s major recovery.

## Report

Write `research/rabbit-and-steel-boss-mechanics.md` with these sections:

1. **Status:** "research + proposals". List what you read and which REA commands worked or failed.
2. **Method and evidence:** engine and build type, chunk list, where each kind of fact came from, and how confident you are. Mark each fact **confirmed** (from data or code) or **inferred** (from names or descriptions).
3. **Boss catalogue:** one section per boss, using the fields above.
4. **Mechanic building blocks:** the cross-boss summary.
5. **Ideas for Last Bell (proposal):** 8–15 ideas. For each one, give:
   - the R&S source mechanic
   - how it would look in Last Bell's bell and clockwork theme
   - which Last Bell boss or slot it could fit
   - its Strike or Ground tag
   - its escape route without iframes
   - how it scales from 1 to 8 players and still works solo
   - which rule it touches, if any

   Put co-op ideas (spread, soak, tether, pass-the-mark) first.
6. **Conflicts:** each R&S mechanic that breaks a settled rule, and the Last Bell alternative.
7. **Open questions for Chzy18.**

Also save a one-paragraph summary at the top of the report that can be pasted into the team channel.

## Limits

- Ideas only. Don't copy names, sprites, music, dialogue or code. Last Bell designs must be original.
- Quote game text sparingly: short fragments only, enough to identify a move.
- Don't change Last Bell game code or specs in this session. Research files only.
- If you can't tell from the data and code how an attack works, say so. Don't fill the gap from memory of playing or watching the game unless you label it "from general knowledge, unverified".
