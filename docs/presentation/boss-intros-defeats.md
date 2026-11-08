# Boss intros and defeats: audit and changes (proposal)

Status: **proposal, done as recommended, review later** (Presentation lane, 8 Nov 2026). Sources read: root `AGENTS.md`,
`gpt/08-design-rules.md`, `gpt/02-visual-style-guide.md`, `gpt/04-bosses.md`, `gpt/06-icons-ui-vfx.md`, and the code
named below. Current boss versions only: Aurelion v4, Crucivar v4, Sylvara v3, the Bellkeeper Ultimate.

## 1. Audit: what each main boss showed before this change

Shared by all four (`RunDirector`, `RunHud`):

- **Start.** `RunState.Intro` lasts 120 ticks (2 s). `RunHud.IntroCard()` fades in a band with an eyebrow (`Practice`, or
  the run's stage label such as "Skyward Spire · Area boss"), the boss name in spaced capitals, gold rules, the epithet
  (the part of `DisplayName` after the comma), the lesson line and a countdown. The camera stays on the player. Players
  can't attack; the boss isn't stepped, so any clip started at spawn plays out.
- **Death.** `BossEncounter.Die()` cancels pending hazards the same tick and calls the boss's `OnDefeat()`. `RunState.Cleared`
  lasts 180 ticks (3 s). `RunHud.ClearedCard()` fades in at once: the name in gold, "Falls silent", and the next fight.
  The boss bar fades to 40 %. No freeze, flash, slow motion or camera change; the card covers the Defeat clip's first beat.

| Boss | At the start | At death |
|---|---|---|
| **Aurelion v4** | `Entrance` clip stretched over the 120-tick intro (straightens, plants the glaive). Card: "Aurelion", "the Meridian Regent". | `Defeat` clip over 150 ticks, core glow drops, gold Crescendo burst. Clip ends at 2.5 s and holds (the boss isn't stepped after death). |
| **Crucivar v4** | `Intro` clip, 2 s (rises from the gantry crouch, one hammer beat), started when he is built. Card: "Crucivar", "The Furnace Heart". | `Defeat` clip over 3 s (kneels, final pour, locks solid), rig shuts down, gold Crescendo burst. The clip is still playing when Cleared ends. |
| **Sylvara v3** | `Intro` clip, 2 s (wakes, lifts the fork), started from her `Update` during the intro (the same block appears twice; harmless, guarded by `introPlayed`). Card: "Sylvara", "the Rootbound Choir". | Props and emitters cleared, mask closes, `Defeat` clip over 2.5 s, gold Crescendo burst. |
| **Bellkeeper Ultimate** | **No entrance clip:** he idles through the intro. Card: "The Bellkeeper" with **no title** (no comma in the name). | `Defeat` clip over 2.5 s, abilities stopped, gold Crescendo burst. |

Gaps: the card doesn't say where you are or what the fight is (in practice it just says "Practice"); the Bellkeeper has no
entrance and no title; nothing frames the boss; the kill has no beat before the card; the card says "Falls silent" and
nothing about the fight.

## 2. What was built

All in the shared presentation layer: `Run/Presentation/BossMoments.cs` (new component on the director),
`RunHud.IntroCard()` / `ClearedCard()`, and two small presenter clocks in `CharacterPresenter` (shared with the hit-stop).
**No boss file changed** and no new animation was authored. Fight timing is unchanged: the intro is still 120 ticks and
Cleared 180 (checked by the suite).

- **Name card.** Eyebrow line with role and area: "Area boss · The Skyward Spire", "Final boss · The Throne of the Final
  Toll", "Practice · The Verdant Chimes" (minibosses in a run keep their stage label). The Bellkeeper gets his title from
  `gpt/04-bosses.md`: "Sovereign of the Shattered Hour". Cinematic bars slide in and out within the intro.
- **Entrance.** The camera frames the boss together with the player (the existing `RunDirector.FrameAlso` hook) until
  75 % of the intro, so it is back on the player before combat. Aurelion, Crucivar and Sylvara keep their own entrance clips.
  A boss whose code starts none plays its first existing clip of `Entrance`, `Intro`, `PhaseShift` (for the Bellkeeper:
  his transformation clip, if his model has it).
- **Defeat.** On the kill every character freezes for 0.1 s (the hit-stop clock), a soft gold flash lifts off the screen,
  the boss's Defeat clip runs at 0.3 speed for 0.5 s and eases back, then holds its last frame to the end of Cleared. The
  camera frames the fallen boss. The card arrives after this beat: role line, the name in gold, "Defeated", the fight
  time, and the next fight.
- **Off switch:** `PresentationSettings.BossMoments` (PlayerPrefs, on by default), `-lastbellNoBossMoments` for one launch.
- **Sound hooks:** `BossMoments.IntroStarted`, `NameLanded`, `DefeatMoment` (C# events) for the Sound lane.
- **LAN:** clients poll the replicated run state, so they get the same card, framing, flash and slow motion; the host's
  entrance clip reaches them through the replicated pose.

## 3. Open questions

1. Should the Bellkeeper's entrance use a dedicated clip later (the model has none named `Entrance`/`Intro`)?
2. Is "Sovereign of the Shattered Hour" the title you want on his card?
3. Should the defeat camera also pull back a little (`RunDirector.CameraPull`), or is framing enough?
