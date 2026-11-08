#!/usr/bin/env bash
# Builds this worktree's unity/ project into builds/miniboss-silt-carillon/<run>/ and runs the Silt Carillon acceptance
# test plus our usual checks (Arcanist-only, menu flow quick, the -lastbellPractice silt launch). Chzy18's suites are skipped.
# Usage: tools/silt-carillon/build-and-test.sh <run-name> [--no-build]
# Other miniboss sessions build at the same time: it waits while 2 or more Last Bell Unity editors are running, and runs
# a player only while fewer than 2 other LastBell.exe are running.
set -u
ROOT="/f/Last Bell/Collab"; WT="$ROOT/minibosses/silt-carillon"
R="$1"; NOBUILD="${2:-}"
OUT="$ROOT/builds/miniboss-silt-carillon/$R"; E="$ROOT/build-evidence/miniboss-silt-carillon/$R"; mkdir -p "$E"
count() { powershell -NoProfile -c "@(Get-CimInstance Win32_Process -Filter \"Name='$1'\" | Where-Object { \$_.CommandLine -match '$2' -and \$_.CommandLine -notmatch 'AssetImportWorker' }).Count" | tr -d '\r'; }
if [ "$NOBUILD" != "--no-build" ]; then
  while [ "$(count Unity.exe 'Last Bell')" -ge 2 ]; do sleep 30; done
  echo "== build $(date +%T)"
  LASTBELL_WINDOWS_OUTPUT="$OUT/LastBell.exe" "$ROOT/.unity-tools/6000.6.6f1/Editor/Unity.exe" -batchmode -quit -projectPath "$WT/unity" \
    -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile "$E/build.log"
  echo "build exit=$? $(date +%T)"
  grep -E "Confrontation build:|error CS" "$E/build.log" | head -30
  [ -f "$OUT/LastBell.exe" ] || { echo "BUILD FAILED"; exit 1; }
fi
run() {   # name width height args...
  local name="$1" w="$2" h="$3"; shift 3
  while [ "$(count LastBell.exe 'LastBell')" -ge 2 ]; do sleep 10; done
  local d="$E/$name"; mkdir -p "$d"
  echo "== $name $(date +%T)"
  timeout 1500 "$OUT/LastBell.exe" -screen-fullscreen 0 -screen-width "$w" -screen-height "$h" "$@" -lastbellOut "$(cygpath -w "$d")" -logFile "$(cygpath -w "$d")/player.log"
  echo "$name exit=$? $(date +%T)"
}
run silt 1920 1080 -siltCarillonTest
head -1 "$E/silt/silt-carillon-report.txt" 2>/dev/null; grep -n "FAIL" "$E/silt/silt-carillon-report.txt" 2>/dev/null | head -40
run arcanist 1600 900 -lastbellAutotest -lastbellArcanistOnly
run flow 1920 1080 -lastbellFlowStage 8 -lastbellFlowQuick
for s in arcanist flow; do echo "-- $s: PASS $(grep -c '^PASS ' "$E/$s/player.log") FAIL $(grep -c '^FAIL ' "$E/$s/player.log")"; grep -h 'RESULT\|^FAIL ' "$E/$s"/*.txt "$E/$s/player.log" 2>/dev/null | head -10; done
# the practice launch: 30 s in the fight, no exceptions
d="$E/launch"; mkdir -p "$d"
while [ "$(count LastBell.exe 'LastBell')" -ge 2 ]; do sleep 10; done
"$OUT/LastBell.exe" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice silt -logFile "$(cygpath -w "$d")/player.log" &
sleep 30; powershell -NoProfile -c "Get-CimInstance Win32_Process -Filter \"Name='LastBell.exe'\" | Where-Object { \$_.CommandLine -match 'miniboss-silt-carillon' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }"
echo "launch exceptions=$(grep -c Exception "$d/player.log")"; grep -m3 "lastbellPractice\|Silt" "$d/player.log"
