Last Bell - the main player (November 10, 2026)

One player for everything: Start Run (the campaign), Boss Rush, Multiplayer (LAN) and Settings.
There is no separate LAN player any more; "Play-Last Bell LAN" is replaced by this folder.

Playing alone
- Run LastBell.exe and choose Start Run or Boss Rush. The game's own LAN code starts nothing until you open
  Multiplayer. Note: this is a Unity development build, and Unity's debug connection listens on port 55000 at
  startup, so Windows Firewall may ask once at the first launch. "Cancel" is fine for solo play; for LAN hosting,
  allow Private networks.
- Boss practice: LastBell.exe -lastbellPractice <boss> (e.g. rivet, briar, twins, tempo), or the
  "Play <Boss> Practice.cmd" launchers in the workspace.

Playing over LAN (up to 4 players)
1. Copy this WHOLE folder to each computer. Every computer must run this exact build; a different build is
   refused when it tries to join.
2. Run LastBell.exe and choose Multiplayer.
3. The host: Host LAN, then allow LastBell through Windows Firewall on Private networks (asked only now).
   The lobby shows the address and port for others to type.
4. Everyone else: type the host's address and port, then Connect.
5. The host picks the boss and arena; everyone picks a class and presses Ready; the host presses Start fight.
   Every boss except the Tutorial plays over LAN. Multiplayer starts single boss fights; the campaign is single-player.

New in this build
- Wing music: each area plays 1-2 wing songs through its miniboss fights; each area boss keeps its own song.
  Skyward Spire has its real songs; Brass Foundry and Verdant Chimes still use stand-in songs.
- Rigged models for Rivet Cantor, Briar Metronome and the Escapement Twins (Tick, Tock and the escape wheel).
  Crucible Skater and Silt Carillon are still stand-in bodies.

Logs: %USERPROFILE%\AppData\LocalLow\Last Bell\Last Bell\Player.log (lines starting [LAN] cover a LAN session;
copy it from every computer before the next launch overwrites it).
Source: branch integrate/rebuild-20261110 (to become feature/confrontation-menu); docs/play-last-bell-README.txt.
