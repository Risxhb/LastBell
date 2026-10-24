#!/bin/bash
# Bellkeeper Ultimate: build this worktree once under the machine-wide Unity lock, then run several game instances of
# that build at the same time (Chzy18: "do my unity sessions at once so this will go faster"). Builds stay one at a time;
# only the game instances run in parallel.
#
#   tools/bellkeeper-ultimate/parallel-runs.sh <name> "<label>|<player args>" ["<label>|<player args>" ...]
#
#   e.g. tools/bellkeeper-ultimate/parallel-runs.sh clears-2 \
#          "perfect12|-lastbellBellkeeperUltimate -lastbellTestAbility bots -lastbellBotRuns p1,p2" \
#          "perfect48|-lastbellBellkeeperUltimate -lastbellTestAbility bots -lastbellBotRuns p4,p8"
#
#   Each instance gets its own small window (960x540), evidence folder <B>/bellkeeper-ultimate-evidence/<name>/<label>
#   and player.log. An instance needs about 1 GB of RAM: one is started only while at least BK_MIN_FREE_MB (default
#   1500) MB are free; otherwise it waits for the running ones. BK_SESSION = lock owner, BK_TIMEOUT = per-instance game
#   timeout (default 6000 s), BK_SKIP_BUILD=1 reuses the last build, BK_KEEP_BUILD=1 keeps unity/Builds afterwards.
set -u
NAME=${1:?usage: parallel-runs.sh <name> "<label>|<args>" ...}; shift
WT="$(cd "$(dirname "$0")/../.." && pwd)"
B="/c/Users/BS_Ch/Documents/Codex/last bell"
LOCK="$B/.unity-build-lock"; OWNER="${BK_SESSION:-$NAME}"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe"
ROOT="$B/bellkeeper-ultimate-evidence/$NAME"
EXE="$WT/unity/Builds/Windows/LastBell.exe"

while ! mkdir "$LOCK" 2>/dev/null; do
  age=$(( $(date +%s) - $(stat -c %Y "$LOCK" 2>/dev/null || date +%s) ))
  if [ "$age" -gt 2700 ] && ! tasklist 2>/dev/null | grep -qiE "^(Unity|LastBell)\.exe"; then
    echo "breaking a stale lock ($age s old, owner: $(cat "$LOCK/owner.txt" 2>/dev/null), no Unity running)"
    rm -rf "$LOCK"; continue
  fi
  echo "$(date +%H:%M:%S) waiting for the Unity lock (owner: $(cat "$LOCK/owner.txt" 2>/dev/null), ${age}s)"
  sleep 60
done
echo "$OWNER $(date -Iseconds) $WT (parallel: $#)" > "$LOCK/owner.txt"
trap 'rm -rf "$LOCK"' EXIT

rm -rf "$ROOT"; mkdir -p "$ROOT"
if [ "${BK_SKIP_BUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -projectPath "$WT/unity" -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -logFile "$ROOT/build.log"
  grep -E "error CS|Last Bell build" "$ROOT/build.log" | sort -u | head -30
  grep -q "Last Bell build: Succeeded" "$ROOT/build.log" || { echo "BUILD FAILED"; exit 1; }
fi

free_mb() { powershell -NoProfile -Command "[int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024)" 2>/dev/null | tr -d '\r'; }
pids=(); labels=()
for spec in "$@"; do
  label=${spec%%|*}; args=${spec#*|}; O="$ROOT/$label"; mkdir -p "$O"
  while [ "$(free_mb)" -lt "${BK_MIN_FREE_MB:-1500}" ] && [ ${#pids[@]} -gt 0 ]; do
    echo "$(date +%H:%M:%S) $label waits: $(free_mb) MB free"; wait -n; sleep 5
  done
  # shellcheck disable=SC2086
  timeout "${BK_TIMEOUT:-6000}" "$EXE" -screen-width 960 -screen-height 540 -screen-fullscreen 0 \
    -job-worker-count 1 -lastbellAutotest -lastbellOut "$(cygpath -w "$O")" -logFile "$(cygpath -w "$O")\\player.log" $args > /dev/null 2>&1 &
  pids+=($!); labels+=("$label")
  echo "$(date +%H:%M:%S) started $label (pid $!, $(free_mb) MB free): $args"
  sleep 20   # stagger the start-up loads
done
for i in "${!pids[@]}"; do wait "${pids[$i]}"; echo "${labels[$i]}: game-exit=$?"; done

for label in "${labels[@]}"; do
  echo "== $label: $(head -1 "$ROOT/$label/autotest-report.txt" 2>/dev/null || echo 'NO REPORT')"
  grep -E "^FAIL" "$ROOT/$label/autotest-report.txt" 2>/dev/null | head -20
done
rm -rf "$LOCK"; trap - EXIT
[ "${BK_KEEP_BUILD:-0}" = "1" ] || rm -rf "$WT/unity/Builds"
echo "evidence: $ROOT"
