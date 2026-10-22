# Rabbit and Steel evidence

Raw output for `research/rabbit-and-steel-boss-mechanics.md`. Everything here was read statically from `E:\SteamLibrary\steamapps\common\Rabbit and Steel`. The game was never launched, and nothing in its folder was written to.

## REA output (`rea-*.json`)

| File | Command | Result |
|---|---|---|
| `rea-doctor.json` | `doctor` | Ran. Reports unhealthy: no Ghidra, Hopper, IDA or Kuna, and the Windows native controls are missing. |
| `rea-inspect-desktop-application.json` | `inspect-desktop-application <install>` | Worked. Family `gamemaker` ("A FORM container begins with a GEN8 chunk"). |
| `rea-inspect-artifact-exe.json` | `inspect-artifact RabbitSteel.exe` | Worked. PE, 63,302,656 bytes, sha256 `a251a3db…e0f0`, no child containers. |
| `rea-inspect-artifact-folder.json` | `inspect-artifact <install>` | Failed: `directory_requires_file`. |
| `rea-inspect-pe-resources-exe.json` | `inspect-pe-resources RabbitSteel.exe` | Worked (icons and version resources only). |
| `rea-search-exe-en_.json` | `search RabbitSteel.exe en_` | Failed: `provider_unavailable` (needs Ghidra, Hopper, IDA or Kuna). |
| `rea-analyze-exe.json` | `analyze RabbitSteel.exe` | Failed: `provider_unavailable`. |
| `rea-decompile-exe.json` | `decompile RabbitSteel.exe 0x140001000` | Failed: `provider_unavailable`. |
| `rea-inspect-binary-layout-*.json` | `inspect-binary-layout` | Failed: this profile supports Linux x64 only. |
| `rea-inspect-with-objdump-exe.json` | `inspect-with-objdump … section_headers` | Failed: objdump isn't configured. |

## Extracted with the Node scripts in `tools/`

- `datawin-chunks.json`: the `data.win` chunk list (`tools/scan.mjs`). It has no CODE, VARI or FUNC chunk.
- `strg-*.txt`: name lists from the `data.win` STRG string table, one file per prefix:
  - `bp_`: boss patterns
  - `scrbp_`: pattern helpers
  - `bpsw_`: pattern switches
  - `bpatt_`: pattern scheduling
  - `enc_`: encounters
  - `hbs_`: statuses
  - `ttrg_`: target pickers
  - `tcond_`, `tpat_`, `ipat_`: trigger conditions and item patterns
  - `eff_`: effect strings
  - `obj_`, `mv_`, `en_`: objects, moves and enemies
  - `strg-telegraph-sprites-sounds.txt`: telegraph-related sprite and sound names
- `exe-yyc-script-callgraph.json` (`tools/yyc.mjs`): 1,303 `bp_`/`scrbp_` functions from `RabbitSteel.exe` and the direct calls each one makes to other named scripts, with call counts. Each function is named by the `gml_Script_*` string it loads near its start and bounded by `.pdata`. Indirect calls aren't seen, so a missing call proves nothing.
- `exe-boss-helper-usage.json` (`tools/agg.mjs`): the call graph merged per boss variant, plus the shared patterns' helper calls.
- `boss-variant-summary.tsv`: one row per boss variant, with:
  - its phase-script count
  - whether it has solo (`_s`), `_h` or `_l` scripts
  - whether it has enrage, a hard timer, HP thresholds, a boss heal or a transform
  - the co-op helpers it calls
- `exe-solo-vs-multi-phase-scripts.json`: evidence that `*_s` scripts are the solo versions.
- **Ghidra decompile** (Ghidra 12.1.4 headless, five slow analysers disabled):
  - `rea-function-steeltooth0_pt2.json` and `rea-doctor-ghidra.json`: the REA route, one function.
  - `ghidra-decompiled/` and `ghidra-annotated/`: all 404 boss scripts, raw and with names and constants resolved.
  - `ghidra-headless.log`: the run log.
  - `.gitignore` keeps the decompiled code out of the public repo, because it's derived from the game's binary.
- **Built from the decompile:**
  - `boss-pattern-adds.json`: every `bpatt_add` with its pattern, settings and time block.
  - `boss-timing-summary.json`: per-pattern and per-boss telegraph statistics, plus per-difficulty switches.
  - `boss-heal-and-thresholds.json`: form-switch heals.
  - `boss-enrage-times.json`: enrage timers.
- **Lookup tables:**
  - `exe-yyc-function-names.json`: every named function start.
  - `exe-yyc-string-globals.json`: start-up string globals mapped to setting names.
  - `datawin-scpt-index.tsv`: script ID = 100000 + index.
  - `datawin-rooms.tsv`: room sizes.
- **Tools:**
  - `tools/DecompileList.java`, `tools/DisableSlowAnalyzers.java` and `tools/targets-all.txt`: the Ghidra scripts and the target list.
  - `tools/keymap.mjs`, `tools/annotate.mjs`, `tools/parse.mjs` and `tools/annotate-all.mjs`: resolve and parse.
  - `tools/summarise.mjs`, `tools/heals.mjs` and `tools/enrage.mjs`: the summaries.
- `mod-encounter-api-notes.md`: paraphrased notes from the developer's official Encounter API sheet. It's linked from `Mods\mod_readme.txt` and was read in a browser.
- `csv-*-english.tsv`: the English column (with key and level) from `Data\*.csv`:
  - enemy titles
  - statuses
  - effect and warning strings
  - menu and difficulty strings
  - tutorial strings
  - Defensive-move descriptions

To re-run the extraction: `node tools/scan.mjs "<install>" <outDir>`, then do the same with `csv.mjs <install>/Data`, `yyc.mjs <install>/RabbitSteel.exe` and `agg.mjs <callgraph> <out>`. The full STRG dump and the raw exe string dump were not kept here. They are large and are mostly engine strings.
