# LAN multiplayer: transport foundation (unfinished)

## November 10: LAN ships in the main player (one player, one build path)

- There is one Windows player for everything: Start Run, Boss Rush and Multiplayer. `Play-Last Bell LAN/` is to be replaced by a README pointing at `Play-Last Bell/` when the new player is swapped in.
- One build path: `LanTestBuild.BuildWindows` now only calls `ConfrontationMenuBuild.BuildWindows` (the `LASTBELL_LAN_TEST` define is gone from the build too; no code used it since November 4). `LASTBELL_LAN_OUTPUT` still sets the output path for older scripts.
- Nothing network-related runs until the player opens Multiplayer: sockets open only from the lobby's Host/Connect (`LanConnection`), so the game's LAN code triggers the Windows Firewall prompt only when someone hosts. Caveat: the player is a Unity Development build (`BuildOptions.Development`, since the first Confrontation build), and Unity's player connection listens on TCP 0.0.0.0:55000 at startup, so Windows can still ask at first launch (Block rules exist for this morning's and earlier test players that never hosted). Removing that needs a non-Development build: a separate decision. Test hooks (`-lastbellLanMenuTest`, `-lastbellLanSmokeHost`/`-lastbellLanSmokeClient`) stay inactive without their flags.
- First build with this: branch `integrate/rebuild-20261110`, test player `builds/rebuild-20261110b/player` (see `docs/current-handoffs.md`, 10 Nov).

## November 4: LAN is part of the main player, and every boss but the Tutorial plays over LAN

User (3 Nov, night): the client should show everything the host sees, each computer running its own copy of the game, "just like how multiplayer should be".

**Model (unchanged, and the usual one for co-op):** every computer runs its own full game and draws everything itself. The host runs the only fight simulation; clients send input (60 Hz) and receive the fight state and the host's effect calls (30 snapshots a second), draw it about 1.5 snapshots behind, and predict their own movement.

**One player:** the `LASTBELL_LAN_TEST` gates are gone. The main menu has Start Run, Boss Rush, Multiplayer, Settings and Exit, and the main Windows build (`ConfrontationMenuBuild.BuildWindows`) includes the lobby scene. LAN encounter limits apply only while a LAN session runs (`RunDirector.Begin`).

**Shared plumbing** (`BossEncounter`, `Run/Networking`):
- `CaptureNetLook`/`ReplicaPresent`: a boss sends its presentation-only state (`BossState.Glow`, up to 64 `Look` numbers) and the client's replica applies it every frame.
- `NetFx`/`ReplayNetEffect`: a boss records its own presentation calls and replays them itself (no generated switch needed).
- Clients keep a stand-in for every host hazard in `World.Hazards.Live` (`HazardSystem.AddReplica`), so mechanic icons, JUMP labels and boss soak/tower numerals work on clients.
- `NetIdOf`/`NetPlayerOf`: lobby ids for per-player looks. `NetDrawnTick`: the host tick a client is drawing.
- Hostile bullets (`BulletSystem`, Sylvara) travel in the snapshot with their velocity (cap 600).
- Snapshot caps (160 hazards, 160 telegraphs); rooted players aren't predicted; telegraphs carry flash scale and lane stretch.
- Parry presses (Dial Warden) travel in the input with the tick the client saw; the host judges them there, within 15 ticks of lag. Protocol 5.

**Per boss:** looks for Bellkeeper (shell and phase), Aurelion, Tempo Tyrant (glow, wake fissure), Oracle, Pendulum Sentinel (plumb line); Crucivar (effects, rig, turrets, castings, Mould Towers, pour numerals); Sylvara (bullets, colour rings, pods, roots, buds, phase looks); Dial Warden (parry prompt and input, wave wall, counterweights, sounds); Escapement Twins (Tock, team marks, Escape Wheel, Counterpoise numerals); Rivet Cantor (glyphs, Chorus numeral, poses); Crucible Skater (ladles, numerals); Briar Metronome (season numerals, hedges, grafts, pendulum); Silt Carillon (bell numerals); Vesper Seedkeeper (stakes, cords, seed orbs, numerals). The Tutorial stays single-player (its bots and checks assume one player).

**Verified on `builds/lan-all-20261104/c2/LastBell.exe`** (0 errors / 67 warnings; evidence `build-evidence/lan-all-20261104/`; one Windows computer, automated players): .NET harness 208/208; LAN menu 49/49 and 40/40; two-process fights for all 14 bosses (Tempo Tyrant also with 3 clients), each forcing its showcase mechanic, 42–94 checks each, 0 failures, 0 exceptions. Client checks include boss looks with no errors, boss-drawn overlays on every new boss, and 291 Sylvara bullets drawn from one snapshot.

Not verified: two real computers and people playing; the Dial Warden parry prompt on a client (the automated client never stands on a lit hour; the input path is covered by the harness); campaign runs over LAN (Multiplayer starts single boss fights).

## November 3: the LAN lobby (names, classes, Ready, start), on the Tollbreaker base

Branch `feature/lan-lobby` (worktree `lan-lobby/` in the workspace), planned in the workspace's `docs/lan-lobby-plan-20261102.md`. The LAN work above was committed as of 3 Nov 00:28 and merged with Azure PRs 14–20 (Tollbreaker replaces Vanguard) and the Tutorial.

- **Classes:** Tollbreaker, Chimesinger, Arcanist (`LanLobby.ClassList`, checked against `RunDirector.Classes`). The Vanguard's effect replay cases were dropped when its effects were removed.
- **Names:** `LanProfile` saves the player's name in `persistentDataPath/lan-profile.json`; `-lastbellProfile <name>` names one run without saving. Rules in `LanPlayerName` (2–16 ASCII letters, digits, space, _ or -). The host suffixes duplicates (" (2)").
- **Lobby rules (protocol 4), all enforced by the host:** everyone, host included, presses Ready; Ready locks your class; changing boss or arena un-readies everyone; a joiner starts not ready and others keep theirs; Start needs 2+ players, all ready (`CanStart`, `StartBlocker`).
- **Lobby scene** `Menu/LanLobby.unity` (`LanLobbyScreen`): name card → Host/Connect → lobby with the boss and arena at the top (host pickers; clients read-only with a change notice), four slots with each player's class model and name, Ready and Start. Controls and the full flow: [lan-lobby-playtest.md](lan-lobby-playtest.md).
- **Fights** show lobby names over players; the diagnostics check that the fight's players, names and classes match the lobby.

**Verified on `builds/lan-lobby-20261103/r6/LastBell.exe`** (workspace; 0 errors / 63 warnings; evidence `build-evidence/lan-lobby-20261103/r6/`), one Windows computer, automated players: .NET harness 201/201; menu diagnostic 49/49 and 40/40 with `-lastbellProfile` (host and client screenshots in `menu/`); every fight run readies, has the host change the boss and back, readies again and starts: Tempo Tyrant host + 1 Tollbreaker client and host + 1 Arcanist client (host 22/22, client 19/19), host + 3 Tollbreaker clients (22/22, 19/19 each), Bellkeeper host + 1 Chimesinger client (22/22, 19/19); 0 exceptions. Lobby 190–220 fps with four models at 2560×1440.

Not done: a person playing it, two computers, non-16:9 screens; after a fight everyone returns to the main menu rather than the lobby.

## November 3: clients see the host's effects and their own movement responds at once

Goal (user): a client should play the game the way the host does.

- **Effect replay.** Every presentation call on the host is recorded and sent with the next snapshot as an event: the method name, the host tick and its arguments. Players, the boss and projectiles travel as references. The client replays each call on its replica when its drawn timeline reaches that tick, so effects line up with the smoothed characters. Covered:
  - all 48 player ability effects (`PlayerFx`: Vanguard, Chimesinger and Arcanist, including beams, sigils, comets, glyphs, blink, shields and trails);
  - shared boss effects (`BellkeeperFx`: sweep, toll and chime resolves, charge trail and landing, boss and player hits, potion heals);
  - camera shake and damage numbers;
  - custom projectiles: Arcanist missiles follow a stand-in driven by the host's recorded moves and ends;
  - boss parts: the Tempo Tyrant and Oracle ribbon effects (`TempoTyrantFx`), and the Oracle's and Pendulum Sentinel's sounds.

  The recording line at the top of each entry point and the client's dispatch switch (`LanFxReplay.Generated.cs`) are produced by a small tool from the methods' signatures. Regenerate the switch if a signature changes. Outside a host session the hook is null, which costs one null check per effect. A replay failure is counted and logged and never ends the session. If a snapshot would grow too large, that batch of effects is dropped. Protocol 3.
- **Own-movement prediction.** The host reports the newest input it has applied for each client and its current walk-speed factor (0 while stunned, dashing, down or rooted). The client draws its own character at the host's position plus the moves the host hasn't applied yet, and eases onto the host's answer. Dashes, knockback and casting roots still come from the host.
- Fixed an error when a client leaves (the replica cleanup touched an already destroyed boss).

**Verified on `builds/lan-test-20261102/r21/LastBell.exe`** (0 compile errors; evidence `build-evidence/lan-session-20261102/r21-*`; one Windows computer, automated Arcanist clients):
- All five encounters with host + 1 client: host 13/13 and client 16/16 each (Tempo Tyrant, Chimebound Oracle, Bellkeeper, Aurelion, Pendulum Sentinel), including retry. Tempo Tyrant with host + 3 clients: host 13/13, each client 16/16.
- Clients replayed 98–468 effects per run with 0 failures. Effects seen on clients:
  - Arcanist beam, missiles, sigils and comets; the host's Chimesinger Crescendo;
  - boss hits, player hits, damage numbers and shake;
  - Bellkeeper sweep, toll and charge effects;
  - Tempo Tyrant and Oracle arcs, streaks and rims;
  - Oracle lane and toll sounds; Pendulum Sentinel tick, ratchet, swing and rush sounds.
- Prediction drew the client's own character 0.2–0.9 m ahead of the host's last state while moving. The snapshot frame-rate log shows hosts at 21–44 fps on this machine.
- The .NET harness passes 171/171 (two new effect wire checks).

Not covered yet: Aurelion's effect components (Last Light floor, Total Eclipse, Zenith Collapse, Orrery Closing), boss-specific HUD details that read private boss state, and effects any other boss draws without these entry points. (Covered since 3 Nov, `3bfa524`; see the end of this file.)

## November 2 (evening): client HUD, abilities and lag after the first playtest

The first person-played test (two computers) showed the client with only a debug text box, apparently only a basic attack, and heavy lag. Changes:

- **Real HUD on clients.** The client no longer hides `RunHud`. The idle `RunDirector` holds the replica (`AttachLanReplica`/`ApplyLanState`/`DetachLanReplica`), so clients get the normal boss bar, health and stamina, all ability tiles with keys, cooldown sweeps and slot text, class-state pills, party frames and mechanic marks, filled from the host's numbers (`PlayerController.ApplyNetworkState`, `PlayerKit.NetCooldowns/NetSlotStates/NetClassState` read through `Shown*`, `BossEncounter.ApplyNetworkState`/`HudExtraShown`). Snapshots now carry each slot's text (protocol 2). In client view `RunHud` never shows the menu, the Ready button or the Retry/Main Menu shortcuts; results show "Waiting for the host to retry or leave" with Leave session. The old debug box is gone; other players keep a name tag.
- **Abilities.** They did reach the host, but nothing on the client showed it. The HUD now shows them going on cooldown. The diagnostic's automated client presses every slot (holding Disintegrate), and the host checks that a remote player's class action (slots 2–5) actually fires.
- **Movement direction.** The host turned remote WASD with the host's own camera. Clients now send world-space movement (`PlayerInput.WorldMove`).
- **Lag.** Clients drew characters only when a snapshot arrived (20 a second, no smoothing; the game's render smoother doesn't run on the replica). The host now sends 30 a second, and the client keeps a short host-tick-stamped history per actor and draws about 1.5 snapshot intervals in the past, interpolating between snapshots. A first version that glided from the drawn pose froze when a snapshot arrived every frame; the diagnostic caught it.
- **Performance log.** Every 5 s the player log gets `[LAN] host: … fps, … snapshots/s, … bytes each, capture+send … ms/s` and `[LAN] client: … fps, … snapshots/s, … bytes each, apply … ms/s, draw … ms/s`. On a heavily loaded machine, snapshots were 300–650 bytes and networking cost a few ms per second, so frame rate (not bandwidth) limits snapshot rate: the host sends at most one snapshot per frame.

**Verified on `builds/lan-test-20261102/r17/LastBell.exe`** (0 compile errors; evidence `build-evidence/lan-session-20261102/r17-*`), on one Windows computer that was heavily loaded by another game at the time (2–8 fps per player process):
- Tempo Tyrant, host + 1 client: host 13/13; client 11/12. Bellkeeper, host + 1 client: host 13/13, client 12/12.
- Tempo Tyrant, host + 3 clients: host 13/13; each client 11/12.
- The only failure is "client receives player projectile state" on Tempo Tyrant. It passed on r13 at higher frame rates. At 2–8 fps the host sends at most one snapshot per frame, so a short missile flight can fall between snapshots. Not re-verified at normal frame rates.
- New checks pass everywhere: a remote player's class abilities fire on the host; the client HUD shows the host's health, cooldowns and boss; the client sees its own abilities go on cooldown; boss motion is drawn between snapshots (for example 25 drawn frames across 12 moving snapshots at 8 fps). `r17-tempo/client/client-combat.png` shows the client HUD mid-fight.
- No runtime exceptions in these logs. The .NET harness still passes 169/169.

Still not done: ability visual effects on clients (beams, sigils, slashes, rings run only on the host; clients see projectiles, telegraphs and hazards), client-side prediction of the local player (the client sees its own movement after the round trip plus the interpolation delay, about 50–80 ms on a good LAN), damage numbers on clients, and hazards are drawn at the newest snapshot while actors are drawn slightly behind.

## November 2: group size is now four players

The user changed the target from eight to **up to four total players (host + three clients)**. `LanLobby.Capacity = 4` is the single source; `LanLobby.MaxClients` (3) is derived from it, and the listener's pending queue, world-frame player bounds, lobby roster label, the session diagnostic's `-lastbellLanPeerCount` clamp and `tools/lan-test/Run-TwoProcess.ps1 -PeerCount` (1–3) all follow it. A client the host turns away before admission (group full, fight started, different build) now gets "The host closed the connection (the group may be full at 4 players, …)" instead of a bare "Connection lost". Sections below that mention eight players or seven clients describe the earlier target and are kept for history.

The .NET harness (`dotnet run --project tools/lan-transport-check/LanTransportCheck.csproj`) now checks the four-player limit: three clients join a full roster, a fourth is rejected with that message and never enters the roster, a freed slot can be refilled, the listener refuses a fourth pending socket, and world snapshots with five players or an ID outside 0–3 are rejected. It also checks that a connection survives ten seconds of undrained 60 Hz input and drains it in order. 169/169 checks pass (repeated runs); loopback worked inside the sandbox this time.

**Retry fix.** Host Retry rebuilds `CombatWorld`, whose tick restarts at zero. Before, the host pump stopped sending (`World.Tick - lastSent < 3`) and clients would have rejected older ticks as out of order. The host now sends a session sequence that only increases and resets its send cadence when the world changes; the client no longer requires the envelope sequence to equal the frame's world tick. The client's results banner now says it is waiting for the host to retry or leave. The two-process diagnostic now presses Retry after results and checks the host gets the same remote players, reaches a new fight with the session intact, and that the client follows into it.

**Stall fix.** Clients send input at 60 Hz and the host's receive queue held 256 messages, so a host stall over ~4 s (a scene load on a busy machine) overflowed it and the host dropped the client ("A player could not receive the fight state", seen on a loaded machine). Receive queues now hold up to 1,024 messages with an 8 MB byte cap, and the client sends no input until the host's first fight snapshot arrives. The send queue stays at 256.

**Oracle diagnostic.** The Chimebound Oracle holds at 50 % HP until its phase gate opens (by design), so the diagnostic's forced kill doesn't end its fight; the party wipes ~14 s later and results follow 1.7 s after that, which sometimes fell just past the old 15 s wait. The host check now accepts whichever ending actually happened (party wipe → defeat, boss cleared → victory) and waits up to 45 s. The diagnostic also logs timestamped trace lines, `Run-TwoProcess.ps1` passes `-timestamps`, the session logs why it closed, and a rejected world snapshot names the failing part.

**Verified on `builds/lan-test-20261102/r12/LastBell.exe`** (0 errors / 64 warnings, same warnings as earlier builds; evidence in `build-evidence/lan-session-20261102/`):
- Menu diagnostic 21/21 (`menu-r12/`).
- Two processes (host + 1 client), `matrix-r12/`: Bellkeeper, Aurelion, Tempo Tyrant, Chimebound Oracle and Pendulum Sentinel each host 12/12, client 9/9, including retry into a new fight. Oracle ends in a real party defeat on host and client; the others in the diagnostic victory. Oracle repeated twice more, all passing.
- **Four processes (host + 3 clients)**: Bellkeeper and Chimebound Oracle, host 12/12 and each client 9/9 (`bellkeeper-4p/`, `oracle-4p/`).
- No runtime exceptions in these player logs.

Builds r7–r11 were intermediate (r7 found the Oracle/stall issues, r8–r11 added diagnostics). All runs are on one Windows computer over loopback with automated input (every client plays an Arcanist that walks to the boss and attacks); the machine was shared with other Unity builds and tests at the time.

**Still not verified or not done:** two separate computers on a real LAN (firewall prompts, Wi-Fi latency, host-machine IP entry); a human playing a client; visual readability on clients; natural victory over a full fight; encounter-specific presentation beyond the shared telegraph/hazard/projectile set (sounds, class/ability effects, tethers and some custom warnings, full boss animation); client-side results UI beyond the "Victory/Defeat — waiting for the host" banner (clients cannot vote retry or leave the results screen except via "Leave session"); a host leaving mid-fight shows clients "Connection lost" rather than a friendlier message. Not an installer; not distributed.

## Test-edition integration in progress

An isolated `LASTBELL_LAN_TEST` Windows build now routes from Confrontation into a LAN lobby, removes Start Run/Boss Rush, restricts the encounter catalog, and keeps all six arenas and three classes. The session coordinator waits for every client to load, then attaches the host roster to the existing combat world. A client-side replica consumes host snapshots and sends input; common hazards can be drawn. This is still under development: client ability/projectile effects, encounter-specific warnings and tethers, full HUD, result/retry behavior and live encounter coverage are not complete or verified. Do not distribute this as a finished test player.

`builds/lan-test-20261101/r2/LastBell.exe` is an internal development candidate, built with 0 errors and 64 warnings (1,884,815,712 bytes on disk). Its opt-in player menu diagnostic passed 21/21 checks in `build-evidence/lan-session-20261101/menu-r2/`, including six arenas, five enabled encounters, greyed-out others and a local host lobby. It does not validate visuals or a fight. A sandboxed startup probe failed to write Unity preferences; the successful diagnostic ran outside that sandbox. A two-process gameplay diagnostic is being prepared on a subsequent candidate. None of these builds is an installer; nothing has been pushed or published.

`builds/lan-test-20261101/r4/LastBell.exe` built with 0 errors / 64 warnings (1,884,825,952 bytes). Two separate Windows player processes on one computer passed a Bellkeeper diagnostic: host 8/8, client 7/7, in `build-evidence/lan-session-20261101/two-process-r4/`. The remote Arcanist moved in the host's actual world and damaged its boss; the client received combat, health, common hazard and shared-FX telegraph snapshots. An existing diagnostic defeat hook ended the encounter and results reached the client; this is not a natural victory, a visual-readability check, an eight-player gameplay test, or a separate-computer LAN test. The earlier r3 run passed movement/damage/result checks but ended before the client observed a hazard; r4 waits for a hazard before the diagnostic defeat. No runtime exceptions were found in the r4 player logs.

The host now captures the actual shared `BellkeeperFx` telegraph inventory, including styled previews and regions drawn outside `HazardSystem`, and the client reconstructs those shapes. This is not a claim that every encounter-specific visual, animated boss pose, sound, projectile, class effect or mechanic is replicated; those and a full client HUD remain outstanding. Do not ship this candidate as the requested complete player.

On r5, the same two-process diagnostic passed for Bellkeeper, Aurelion, Tempo Tyrant and Pendulum Sentinel (host 9/9, client 8/8 each), including projectile-state delivery. The Chimebound Oracle run passed movement, damage, hazard, telegraph and projectile checks, but the party wiped and the test incorrectly required the diagnostic-victory path (host 8/9, client 7/8); logs show both reached defeat results. The next diagnostic accepts real defeat and records its outcome. Client HUD/marks and initial pose replication have been added since r5 but require a new build and verification. Evidence: `build-evidence/lan-session-20261101/matrix-r5/`. These tests use two processes on one computer, an automated remote input, and, when possible, a debug defeat for result routing. They do not prove natural victory, visual readability or multi-machine/eight-player play.

## Lobby and session framing follow-up

`LanLobby.cs` provides a main-thread lobby for up to four total peers (eight at the time of writing): matching build/protocol handshake, server-assigned IDs, class selection, host-only arena and permitted encounter selection, roster snapshots, disconnect detection, and start/input/world-frame envelopes. After start, selections freeze; the host reports a player disconnect as an error. Input and world payloads are opaque: this code does not serialize or simulate a fight, wire a Unity UI, or determine results. Session errors must be handled and disposed by the future Unity integration. Build IDs must identify the actual compatible player and assets.

The standalone .NET 10 harness passed loopback checks with seven clients for roster and selection propagation, class updates, rejection of an incompatible build, start notification, input/world envelope delivery and disconnect reporting. An initial incompatible-build test found an uncaught `InvalidDataException`; it was fixed and the full suite passed. These checks are neither a Unity build nor a multiplayer combat test. No installer has been created and no source was pushed.

`LanInputFrame.cs` defines a bounded intent packet and validates movement magnitude, finite coordinates and supported flags. Its buffer latches pressed actions until consumed and clears stale held movement; `LanUnityInput.cs` maps it to `PlayerInput` and provides an `IInputSource` for a remote actor. `LanHostRoster` freezes remote identities/classes and, when explicitly activated, the existing `RunDirector` spawns those remote-controlled allies instead of bots. Nothing currently activates this context or pumps it during a live fight. Round-trip, invalid-input, buffer, conversion and roster-to-input checks passed in the standalone harness (Unity types stubbed for conversion). The pinned Unity editor compiled these changes successfully, with existing warnings; see `build-evidence/lan-session-20261101/compile-roster.log` in the workspace. A sandboxed editor attempt crashed after licensing initialization timed out; the successful compile ran outside the sandbox. World-state serialization, client presentation, session lifetime, menu and live gameplay remain to be implemented.

`LanWorldFrame.cs` now defines a bounded, validated snapshot for common player state, boss health/phase, run state and hazard geometry; its codec passed round-trip and malformed-data checks in .NET. `LanWorldCapture.cs` reads those values from the host's current `RunDirector`/`CombatWorld`, and `LanHazardView.cs` can render common hazard shapes from a snapshot without running damage rules. The pinned Unity editor compiled these files successfully (`build-evidence/lan-session-20261101/compile-world.log`). They are not yet driven by a real session or validated in a player. The schema does not yet cover encounter-specific presentation (including tethers, custom visual warnings, projectiles and attack animation/effects), so it is not sufficient to claim all five encounters are synchronized.

## November 1 follow-up: eight-player transport foundation (superseded: now four)

At the time, the user confirmed a target of up to eight total players per LAN group; on November 2 this became four. `LanListener` now accepts multiple peers, with a bounded pending queue; a future session must enforce the seven-client roster limit. `LanConnection.Send` now copies into a bounded queue and returns without socket writes on the calling thread. A successful return means queued, not delivered; a full queue rejects the send and connection failures remain observable. The .NET 10 harness passed actual loopback checks with seven clients and one listener, ordered framed messages, disconnect handling and buffer ownership. Its first seven-client test incorrectly assumed TCP accept order matched connection initiation order; that test was corrected and rerun successfully. The sandbox denied loopback access; the passing run was outside it.

These are transport and lobby checks only. The listener's pending queue is not a total-player limit; the lobby enforces the eight-player roster, while the legacy `LanConnection.Host` still accepts one client. See the newer integration note above for player-build progress. Do not distribute before live combat validation and required presentation work.

This is not yet a validated multiplayer mode. Relay is outside this LAN test's scope.

`Run/Networking/LanConnection.cs` contains an `IConnection` seam and per-peer LAN TCP connections with ordered, length-prefixed messages, bounded queues, connection status and cleanup. The transport does not define a gameplay protocol. It binds to all local interfaces; it has no encryption or authentication and is intended only for trusted LAN testing. Relay would require an adapter and appropriate session establishment; no Unity services or costs have been configured.

The original `RunDirector` creates a local player and bot allies and advances its own `CombatWorld`. The new host/client integration is intended to use one host simulation, remote input and replicated presentation; this needs live validation and the presentation gaps above must be closed. Sending transforms alone or running two unsynchronized worlds is not enough to represent a shared boss fight. Bots cannot validate multiplayer.

## Check

`dotnet run --project tools/lan-transport-check/LanTransportCheck.csproj` exercises actual host/client TCP sockets on loopback in one process, including framing/order, reply, invalid sizes, disconnect, connection failure and port reuse. It uses the installed .NET 10 SDK; this is not a Unity player test. The network sandbox denied the first loopback connection; the test passed outside it. Still needed: a Unity build, actual two-instance gameplay and a two-machine LAN test, including firewall and disconnect behavior.

November 3, 2026 (later): LAN clients now play boss music (the replica's state changes reach `MenuMusic`), and the Tempo Tyrant's music follows the host's tempo from the beat sent in each world frame (`36c8adc`).

November 3, 2026 (evening): clients now draw what only the boss drew on the host: boss props (Pendulum Sentinel orbs and counterweights, Oracle orbs, Falling Hours orbs), the boss clock for the Tempo Tyrant and Oracle (Metronome Court, tempo HUD), the Tempo banner and strike numbers, and Aurelion's Zenith Collapse, Orrery Closing and Last Light floor, replayed on stand-ins for the host's hazards (`3bfa524`). The session test forces those mechanics and covers all five bosses (r13 all pass).
