#!/bin/bash
# Build this worktree's Windows player and run the autotest, under the machine-wide Unity lock.
#   bash tools/aurelion/build-and-test.sh <session-name> <n> [player args, e.g. -lastbellAurelionOnly -lastbellAurelionV2Only -lastbellAbilityTest aurelion.noon_shadow]
# Logs and the report go to "<project root>/aurelion-logs/<session-name>/autotest-<n>". Exit 0 = build OK and all checks passed.
# Lock: "<project root>/.unity-build-lock" (mkdir is atomic); waits 60 s between tries, removed on exit even on failure.
SESSION=$1; N=$2; shift 2
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; B="$(cd "$ROOT/.." && pwd)"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe"
L="$B/aurelion-logs/$SESSION"; mkdir -p "$L"
LOCK="$B/.unity-build-lock"
until mkdir "$LOCK" 2>/dev/null; do echo "lock held by $(cat "$LOCK/owner.txt" 2>/dev/null) - waiting 60 s"; sleep 60; done
echo "$SESSION $(date '+%Y-%m-%d %H:%M:%S') $ROOT" > "$LOCK/owner.txt"
trap 'rm -rf "$LOCK"' EXIT
"$UNITY" -batchmode -projectPath "$ROOT/unity" -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -logFile "$L/build$N.log"
echo build-exit=$?
grep -E "error CS|Last Bell build" "$L/build$N.log" | sort -u | head -20
grep -q "Last Bell build: Succeeded" "$L/build$N.log" || exit 1
O="$L/autotest-$N"; rm -rf "$O"; mkdir -p "$O"
timeout 1800 "$ROOT/unity/Builds/Windows/LastBell.exe" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -lastbellAutotest "$@" -lastbellOut "$(cygpath -w "$O")" -logFile "$(cygpath -w "$O")\player.log" > /dev/null 2>&1
echo game-exit=$?
head -1 "$O/autotest-report.txt" 2>/dev/null || { echo "NO REPORT"; exit 1; }
grep -E "^FAIL" "$O/autotest-report.txt" | head -60
! grep -q "^FAIL" "$O/autotest-report.txt"
