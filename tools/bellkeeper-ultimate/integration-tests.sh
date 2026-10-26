#!/bin/bash
# Integration check for the Bellkeeper Ultimate batch (the Azure migration): build the game and the menu players from this
# worktree and run every suite PR 1 reported, plus the Ultimate's own bot checks, holding the machine-wide Unity lock for
# the whole run (one Unity build or test at a time).
#   tools/bellkeeper-ultimate/integration-tests.sh <name>     evidence: <B>/bellkeeper-ultimate-evidence/<name>/
# Summary lines go to <evidence>/SUMMARY.txt.
set -u
NAME=${1:?usage: integration-tests.sh <name>}
WT="$(cd "$(dirname "$0")/../.." && pwd)"
B="/c/Users/BS_Ch/Documents/Codex/last bell"
LOCK="$B/.unity-build-lock"; OWNER="${BK_SESSION:-$NAME}"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe"
O="$B/bellkeeper-ultimate-evidence/$NAME"

while ! mkdir "$LOCK" 2>/dev/null; do
  age=$(( $(date +%s) - $(stat -c %Y "$LOCK" 2>/dev/null || date +%s) ))
  if [ "$age" -gt 2700 ] && ! tasklist 2>/dev/null | grep -qiE "^(Unity|LastBell)\.exe"; then rm -rf "$LOCK"; continue; fi
  echo "$(date +%H:%M:%S) waiting for the Unity lock (owner: $(cat "$LOCK/owner.txt" 2>/dev/null), ${age}s)"; sleep 60
done
echo "$OWNER $(date -Iseconds) $WT" > "$LOCK/owner.txt"
trap 'rm -rf "$LOCK"' EXIT
rm -rf "$O"; mkdir -p "$O"; S="$O/SUMMARY.txt"; : > "$S"
w() { cygpath -w "$1"; }
say() { echo "$*" | tee -a "$S"; }

# 1. the game player
"$UNITY" -batchmode -projectPath "$WT/unity" -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -logFile "$O/build-run.log"
if grep -q "Last Bell build: Succeeded" "$O/build-run.log"; then say "RunBuild.BuildWindows: Succeeded, $(grep -c 'error CS' "$O/build-run.log") compile errors"; else say "RunBuild.BuildWindows: FAILED"; grep "error CS" "$O/build-run.log" | sort -u | head -20 | tee -a "$S"; exit 1; fi
EXE="$WT/unity/Builds/Windows/LastBell.exe"
run() {   # run <dir> <timeout> <args...>
  local d="$O/$1" t=$2; shift 2; mkdir -p "$d"
  timeout "$t" "$EXE" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -lastbellOut "$(w "$d")" -logFile "$(w "$d")\\player.log" "$@" > /dev/null 2>&1
  echo $?
}
# 2. the suites from PR 1
e=$(run runloop 6000 -lastbellAutotest); say "Run-loop autotest (exit $e): $(head -1 "$O/runloop/autotest-report.txt" 2>/dev/null || echo NO REPORT)"
grep -E "^FAIL" "$O/runloop/autotest-report.txt" 2>/dev/null | head -30 | sed 's/^/   /' | tee -a "$S"
e=$(run tempo 1800 -tempoTyrantTest); say "Tempo Tyrant (exit $e): $(grep -hE 'passed|failed|RESULT' "$O"/tempo/*report*.txt 2>/dev/null | head -1 || echo NO REPORT)"
e=$(run oracle 1800 -chimeboundOracleTest); say "Chimebound Oracle (exit $e): $(grep -hE 'passed|failed|RESULT' "$O"/oracle/*report*.txt 2>/dev/null | head -1 || echo NO REPORT)"
# 3. the Ultimate's own checks (work in progress: reported as they are)
e=$(run ult-p5 3000 -lastbellAutotest -lastbellBellkeeperUltimate -lastbellTestAbility bots_p5v2); say "Ultimate P5 + full fight bots (exit $e): $(head -1 "$O/ult-p5/autotest-report.txt" 2>/dev/null || echo NO REPORT)"
grep -E "^\[autotest\] v2 P" "$O/ult-p5/player.log" 2>/dev/null | cut -c1-200 | sed 's/^/   /' | tee -a "$S"
e=$(run ult-p4 3000 -lastbellAutotest -lastbellBellkeeperUltimate -lastbellTestAbility bots_p4v2); say "Ultimate P4 bots (exit $e): $(head -1 "$O/ult-p4/autotest-report.txt" 2>/dev/null || echo NO REPORT)"
grep -E "^\[autotest\] v2 P" "$O/ult-p4/player.log" 2>/dev/null | cut -c1-200 | sed 's/^/   /' | tee -a "$S"
# 4. the menu player and the Confrontation test
"$UNITY" -batchmode -projectPath "$WT/unity" -executeMethod LastBell.Menu.EditorTools.MenuBuild.BuildWindows -logFile "$O/build-menu.log"
if grep -qiE "Build (Succeeded|succeeded)|Last Bell menu build: Succeeded" "$O/build-menu.log" && [ -f "$WT/unity/Builds/Menu/LastBell.exe" ]; then say "MenuBuild.BuildWindows: Succeeded"; else say "MenuBuild.BuildWindows: check build-menu.log"; grep "error CS" "$O/build-menu.log" | sort -u | head | tee -a "$S"; fi
d="$O/confrontation"; mkdir -p "$d"
timeout 1800 "$WT/unity/Builds/Menu/LastBell.exe" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -lastbellConfrontationTest -lastbellOut "$(w "$d")" -logFile "$(w "$d")\\player.log" > /dev/null 2>&1
say "Confrontation menu test (exit $?): $(grep -hE 'passed|failed|RESULT' "$d"/*.txt 2>/dev/null | head -1 || echo NO REPORT)"
rm -rf "$LOCK"; trap - EXIT
say "done $(date -Iseconds)"
