# Run-loop playtest and remaining decisions

October 21, 2026; foundation #6 documentation review. Current target: draft [#42](https://github.com/Risxhb/collab/pull/42), `feature/run-loop` at `18e89ed`, scene `Assets/LastBell/Run/RunArena.unity`. This checklist is prepared for humans; it records no completed human playtest. The [current handoff](https://github.com/Risxhb/collab/blob/18e89ed264eb3ba2cd0f18b50dfb7eaacc6f3149/docs/run-loop/README.md) reports build 15 **107/107**, after old-code removal. The earlier task-list snapshot's 108/108 belongs to an earlier test inventory. Independent foundation runtime evidence is recorded separately.

## Short playtest (record pass / fail / unclear)

Record tester, commit/build, class, difficulty, encounter and seed if available. Start in the run scene or the Windows build for that exact commit. Keep F9/F8 developer skips out of gameplay acceptance; label them if used just to inspect transitions.

1. **Brace / PF-02:** Vanguard key 4 holds Brace for at most 1.5 s, allows slow movement, lowers on release and restores control. Repeat, take a hit, die/retry and confirm no stuck stance. Front mitigation never protects against ground hazards or rear hits.
2. **Responsiveness / PF-05:** chain Basic 3 into Sundering Blow; try a buffered ability near recovery and dash before an unreleased windup. Check whether input feels prompt and a deliberate Last Word commitment remains readable. Report input and expected action when anything feels ignored.
3. **HUD / PF-03–04:** for all three classes inspect every skill, dash and potion icon, binding, cooldown, charge/state text and hover tooltip, including at screen edges. Prototype bindings are 1/LMB Basic, 2–4 attacks (Vanguard 4 Brace), 6 Chimesinger heal/Arcanist Anchor, 5/RMB dash, Space jump, Shift sprint and F potion. These supersede the older proposed Space-dodge mapping; remapping/controller acceptance is separate.
4. **Death recap / PF-06:** take distinguishable named hits, then die. Check chronological source/ability/damage and killing blow; retry and check no previous-attempt hits remain. The current recap shows the last six hits.
5. **Arcanist:** Seal A → three Clockbolts → empowered Lance → Seal B → three Clockbolts → empowered Lance → Convergence while both are ripe. Check two independent seal dials/inscriptions, then try movement, a missed Lance and dash cancellation. The author's measured A-to-B interval is 1.52 s (0.48 s shared ripe window); decide whether this leaves enough practical movement time.
6. **Recovery policy:** spend a potion and take damage before miniboss 1 ends. Before miniboss 2, health should be full and the potion still spent. Repeat through miniboss 3/intermission. Before Bellkeeper, health and potion stock should both be full. Fresh-run Retry begins at encounter 1 full; Practice never advances the run.
7. **No immunity / PF-01:** dash outside a locked footprint before resolve and expect avoidance; dash but remain inside and expect damage. Report any mismatch with the displayed boundary, resolve time, duplicate instance or wall/body collision. The no-immunity decision is settled.
8. **Meter / PF-07:** use Tab and results to check encounter versus run totals, ability shares, hits, largest hit and DPS. Report what additional breakdown is needed; the first pass has no retained segments, chart or damage-taken drill-down.

PF-02–PF-06 have reported implementations awaiting this human review. PF-07 remains an incomplete first pass; no Details! parity is claimed. Minibosses remain stand-ins; Chimesinger's pre-fix Unity export and procedural Hit/Down/GetUp gaps must not be mistaken for final asset quality. Co-op/networking remains held and untested here.

## Bellkeeper decision queue

Source: [spec v0.2 §13](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md#13-human-decisions-needed), PR #16 `995478f`. Human choices below remain pending unless explicitly marked settled. Numerical defaults are recommendations, not new approvals.

| ID | Current question / state | Recommended default and reason |
| --- | --- | --- |
| D1 | 6.5 m / 80-degree Sweep versus 8 m with longer warnings | **6.5 m**, retaining authored tier windows. The spec's sampled wall-aware worst escape is 3.84 m versus 5.12 m at 8 m; 8 m with the old 39-tick post-lock window fails its model at every tier. Validate actual shared movement/collision and human readability before balance acceptance. |
| D2 | Damage tuning under **full health per boss plus potions and approved healing**; the old carry-health/no-heal premise is superseded | Keep provisional Steady Ticking 30 / 25 / 30 / 15 HP (Final Toll Sweep 45) as the first measured baseline. Compare hit count, potion use and completion time; neither restore high reference damage nor lower it again solely on the obsolete carry-health argument. |
| D3 | **Policy settled:** no dodge/post-hit immunity; per-instance dedup; potions and Mending Chorus approved | Retain current policy. Do not ask humans again whether to remove approved potions or add iframes. Potion amounts/lockout remain tuning; verify distinct attacks can still damage separately. |
| D4 | Extra recovery while a co-op player is down | **+1.5 s after a Phase 2 block on First Chime/Steady Ticking only**, as proposed. Helps revive opportunities; test channel/collision/safe-route rules when networking resumes. Does not require a mandatory revive mechanic to clear. |
| D5 | Memory of Midnight return placement | **As spec §10:** one recorded locked cone; next Phase 2 Block A with Hollow Toll, resolving 30 ticks later after a 90-tick warning; in Phase 3 during Final Toll's Chime step. Preserve visible memory mark and two-pattern cap; test combinations. |
| D6 | Boss HP versus party size | Start with **base × (1 + 0.6 × (n−1))**; tune base from measured class DPS/fight length and verify every count 1–8. HP scaling does not validate assignment density or difficulty. Current shortened test HP is not final. |

Spec recommendations relying on no approved healing, no potions or carried health are historical. The escape checker samples individual patterns; it does not establish all combined sequences, all starting stamina states or human acceptance.

## Meter scope decision

Recommended next scope: authoritative damage/heal accounting, explicit encounter/run/attempt segments, player/ability/target totals, damage taken and killing-blow drill-down, with duplicate rejection and visible metric definitions. Use the [foundation accounting proposal](../contracts/combat-accounting.md) for exact event keys and metrics: effective damage per eligible combat second (paused/intermission/results time excluded), frozen encounter duration, last 20 completed segments / 5 run summaries and bounded event detail. These are review defaults, not consumer adoption. Accept the current per-ability meter only as a first increment. Charts, uptime, critical/overheal/mitigation views should follow supported authoritative events rather than invented data. Human choice: which of these views is essential for the next playtest; no full addon parity promise.

## Area and final boss identities

The [#44 four-area packet](https://github.com/Risxhb/collab/issues/44) describes four arenas/13 encounters as reference scope. The [#47 packet](https://github.com/Risxhb/collab/issues/47), draft #46 `d478b55`, now supplies identities and three phases each; identity delivery is no longer an unanswered content request.

| Area | Delivered concept / phases | Recommendation pending human review |
| --- | --- | --- |
| Skyward Spire | **Aurelion, The Meridian Regent:** Perfect Order → Misalignment → Exposed Meridian (65%/30%) | Review this concept as the area-boss candidate. |
| Brass Foundry | **Crucivar, The Furnace Heart:** Sealed Furnace → Casting Heat → Open Pressure (65%/30%) | Review candidate with readable heat/pressure routing. |
| Verdant Chimes | **Sylvara, The Rootbound Choir:** Dormant Choir → Awakening Bloom → Hollow Refrain (65%/30%) | Use corrected **four-root-leg v2** reference, preserving distinct silhouette. |
| Shattered Hour | **The Bellkeeper, Sovereign of the Shattered Hour:** Last Guardian → Broken Hour → Final Toll (70%/35%) | Preserve existing Bellkeeper work/reference; reconcile the campaign evolution before replacing mechanics or art. |

Recommended default: retain all four as review candidates, keep the one-arena engineering slice and its stand-ins until the content lane agrees a replacement plan. Human choice is concept acceptance/change, not authorization to silently implement 13 fights. Names/lore/numbers are proposals; sheets are not rigged models or tested fights. Bosses should remain targetable through ordinary phase transitions without automatically cleansing class setup, as proposed by #47.

## Completed / Next / Blocked

**Completed:** reconciled the approved direction, prepared this checklist and consolidated decisions from the exact v0.2 spec and newer handoffs. **Next:** humans execute the checklist and answer pending D1/D2/D4/D5/D6, meter priorities and concept review; owners apply findings through reviewed contracts/content. **Blocked:** human acceptance and future co-op validation cannot be supplied by this documentation task. **Validation:** source/Markdown consistency only; no runtime, playtest, final balance or model acceptance claimed here.
