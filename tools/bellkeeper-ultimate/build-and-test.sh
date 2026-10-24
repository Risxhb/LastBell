#!/bin/bash
# Bellkeeper Ultimate: build a worktree's Unity project and run the in-game autotest, holding the machine-wide
# Unity lock (only one Unity build or test at a time on this PC).
#
#   tools/bellkeeper-ultimate/build-and-test.sh <name> [player args...]
#
#   <name>          evidence folder name, e.g. ult-clocks-1 (output: <B>/bellkeeper-ultimate-evidence/<name>)
#   player args     extra LastBell.exe arguments. Fast loop for an ability session:
#                     -lastbellBellkeeperUltimate -lastbellTestAbility ult_pendulum_clocks
#                   Nothing extra = the full run-loop autotest (every check, ~20 min).
#   BK_SESSION      lock owner written into owner.txt (default: <name>)
#   BK_SKIP_BUILD=1 run the last build again without rebuilding
#   BK_TIMEOUT      game timeout in seconds (default 6000: the full Ultimate section with every mechanic built runs past 40 min)
#
# The worktree is the one this script lives in. The lock is <B>/.unity-build-lock (mkdir is atomic); it waits 60 s
# between tries, breaks a lock older than 45 min only when no Unity.exe / LastBell.exe is running, and always removes
# its own lock on exit.
set -u
NAME=${1:?usage: build-and-test.sh <name> [player args...]}; shift
WT="$(cd "$(dirname "$0")/../.." && pwd)"
B="/c/Users/BS_Ch/Documents/Codex/last bell"
LOCK="$B/.unity-build-lock"; OWNER="${BK_SESSION:-$NAME}"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe"
O="$B/bellkeeper-ultimate-evidence/$NAME"

while ! mkdir "$LOCK" 2>/dev/null; do
  age=$(( $(date +%s) - $(stat -c %Y "$LOCK" 2>/dev/null || date +%s) ))
  if [ "$age" -gt 2700 ] && ! tasklist 2>/dev/null | grep -qiE "^(Unity|LastBell)\.exe"; then
    echo "breaking a stale lock ($age s old, owner: $(cat "$LOCK/owner.txt" 2>/dev/null), no Unity running)"
    rm -rf "$LOCK"; continue
  fi
  echo "$(date +%H:%M:%S) waiting for the Unity lock (owner: $(cat "$LOCK/owner.txt" 2>/dev/null), ${age}s)"
  sleep 60
done
echo "$OWNER $(date -Iseconds) $WT" > "$LOCK/owner.txt"
trap 'rm -rf "$LOCK"' EXIT

rm -rf "$O"; mkdir -p "$O"
if [ "${BK_SKIP_BUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -projectPath "$WT/unity" -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -logFile "$O/build.log"
  echo build-exit=$?
  grep -E "error CS|Last Bell build" "$O/build.log" | sort -u | head -30
  grep -q "Last Bell build: Succeeded" "$O/build.log" || { echo "BUILD FAILED"; exit 1; }
fi
timeout ${BK_TIMEOUT:-6000} "$WT/unity/Builds/Windows/LastBell.exe" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 \
  -lastbellAutotest -lastbellOut "$(cygpath -w "$O")" -logFile "$(cygpath -w "$O")\\player.log" "$@" > /dev/null 2>&1
echo game-exit=$?
head -1 "$O/autotest-report.txt" 2>/dev/null || echo "NO REPORT"
grep -E "^FAIL" "$O/autotest-report.txt" 2>/dev/null | head -40
grep -E "Exception" "$O/player.log" 2>/dev/null | sort -u | head -10
rm -rf "$LOCK"; trap - EXIT
# frames -> MP4 (Blender's FFmpeg), after the lock: it isn't Unity
if ls -d "$O"/frames_* >/dev/null 2>&1; then
  "/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -P "$(cygpath -w "$WT/tools/run-loop/frames_to_mp4.py")" -- "$(cygpath -w "$O")" 2>&1 | grep -E "^VIDEO|Error|Traceback"
fi
echo "evidence: $O"
