# LAN lobby: two-computer playtest checklist

For the LAN test player built from `feature/lan-lobby` (`LASTBELL_LAN_TEST`). Plan and decisions: workspace `docs/lan-lobby-plan-20261102.md`. Tick each line and note anything odd with the time, so it can be matched to the logs.

## Before you start

- [ ] **Same build on every computer.** Copy the whole player folder (`LastBell.exe` plus `LastBell_Data/`), not just the exe. A different build is refused at join ("the build differs").
- [ ] All computers are on the **same home/office network** (same Wi-Fi or router). Not a guest network, VPN or phone hotspot with client isolation.
- [ ] Note each computer's Windows network type: **Private** is needed for the firewall rule below. (Settings → Network & internet → your network → Network profile type.)
- [ ] Logs: after each session, collect `%USERPROFILE%\AppData\LocalLow\Last Bell\Last Bell\Player.log` from every computer before the next launch overwrites it (rename each copy with the computer and the time). Lines starting `[LAN]` cover the session and the per-5-second performance numbers.

## 1. First launch and name

- [ ] Start the game, choose **Multiplayer**. The **Choose a name** card appears.
- [ ] Confirm stays disabled for an empty name, a single letter, and something like `Bob!`.
- [ ] Enter a name with extra spaces (`  Bell   Ringer `) and Confirm. The top right says **Playing as Bell Ringer**.
- [ ] Back to the main menu, Multiplayer again: no name card this time.
- [ ] **Edit name** opens the card with your name; change it and Confirm.

## 2. Host and firewall

- [ ] Computer A: **Host LAN** (default port 7777).
- [ ] Windows Firewall asks whether to allow LastBell: allow it on **Private networks**. If no prompt appears and nobody can join, check Windows Security → Firewall → Allow an app.
- [ ] The lobby shows **Others connect to …** with A's address and the port. Write it down.
- [ ] A's slot 1: Tollbreaker model, A's name underneath, HOST tag, gold frame. Slots 2–4 say **Waiting for player…**

## 3. Join

- [ ] Computer B: Multiplayer → type A's address and port → **Connect**.
- [ ] B appears in slot 2 on **both** screens with B's name and model. B's own slot has the gold frame and the class arrows; A's does not on B's screen.
- [ ] If B can't connect: note the message on B's screen, check the firewall on A, try A's other listed address.
- [ ] Optional: a third and fourth computer join (slots 3 and 4). A fifth is refused with "the group may be full".
- [ ] Two players with the same name: the second shows as **Name (2)**.

## 4. Classes and Ready

- [ ] On each computer cycle the class (Left/Right, the < > arrows, or the D-pad). The model and class name change on **every** screen.
- [ ] Press **Ready**. The slot shows READY on every screen; the class arrows lock; the button reads **Not ready**.
- [ ] While ready, Left/Right does nothing and the status says to press Not ready first.
- [ ] Host: **Start fight** stays disabled and says how many still need to ready ("Waiting for 1 to ready.").
- [ ] Host alone (everyone else left): Start says "Need at least 2 players."

## 5. Host changes the boss or arena

- [ ] Everyone Ready. Host changes the **boss** (< > or Q/E, LB/RB). On clients the boss name changes and the notice **"The host changed the boss or arena. Press Ready again."** appears; everyone is **not ready**; Start locks.
- [ ] Same for the **arena** (< > or Z/C, LT/RT); the arena picture changes.
- [ ] Clients have no boss/arena arrows and the keys do nothing for them.

## 6. Start and play

- [ ] Everyone Ready, host presses **Start fight**. Every computer loads the same arena and boss.
- [ ] In the fight, the name over each other player matches their lobby name; each player is the class they picked.
- [ ] Play a full fight. Note lag, missing effects, anything that looks different between host and client.
- [ ] At the results screen the host can Retry; clients follow. **Leave session** returns a client to the main menu.

## 7. Leaving and losing the host

- [ ] In the lobby, a client presses **Back**: a "Leave the lobby?" card appears. **Stay** keeps you in; **Leave** returns to the main menu and the slot empties on the other screens.
- [ ] A client leaves while others are ready: the others **stay ready**; a player who joins later starts **not ready**, and Start locks until they ready.
- [ ] The **host** leaves (or closes the game) while clients are in the lobby: clients stay on the Multiplayer screen with a reason ("Connection lost …"), not a crash or a frozen screen.
- [ ] The host closes the game mid-fight: note what clients see.

## Controls (lobby)

| Action | Keyboard | Gamepad |
|---|---|---|
| Change class | Left/Right or A/D | D-pad or left stick left/right |
| Ready / Not ready | Enter (on Ready) | A |
| Host: change boss | Q / E | LB / RB |
| Host: change arena | Z / C | LT / RT |
| Move between buttons | Up / Down | D-pad up/down |
| Back / leave | Esc | B |

## Known limits of this test build

- Not an installer; trusted LANs only (no encryption or passwords).
- After a fight everyone goes back to the main menu, not the lobby.
- Only 16:9 screens were checked.
- Boss list is the five LAN bosses: Bellkeeper, Aurelion, Tempo Tyrant, Chimebound Oracle, Pendulum Sentinel.
