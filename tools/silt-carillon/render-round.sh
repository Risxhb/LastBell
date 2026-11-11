#!/usr/bin/env bash
# Silt Carillon art pass (9 Nov 2026): Blender review renders + labelled contact sheets for one round.
#   bash tools/silt-carillon/render-round.sh <round dir name, e.g. model-r2>
# Expects <ev>/<round>/silt_carillon_rig.blend and <ev>/<round>/props/{silt_bell_rig,silt_mound}.blend from the build scripts.
set -u
R="$1"
HERE="$(cd "$(dirname "$0")/../.." && pwd)"
BLD="$HERE/prototype/arena-3d/models/blender"
EV="/f/Last Bell/Collab/build-evidence/miniboss-art-20261109/silt-carillon/$R"
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
CLIPS="Idle:0 Walk:0.4 TideRaise:0.45,0.75 TideLoop:0.6 BellToll:0.1,0.3 SiltPour:0.7 PourLoop:0.08 PealWind:0.9 PealLoop:0.3 PealStrike:0.2,0.5 DrainSlump:0.75 DrainedLoop:1.0 DrainRecover:0.45 PhaseShift:0.3,1.0,1.72 Defeat:1.2,2.5"
BCLIPS="Surface:0.3,0.6 BellLoop:0.5 Toll:0.12,0.4 Sink:0.5"
"$BL" -b "$EV/silt_carillon_rig.blend" -P "$BLD/silt_carillon_shots.py" -- "$EV/shots" turn > "$EV/shots-turn.log" 2>&1 &
"$BL" -b "$EV/silt_carillon_rig.blend" -P "$BLD/silt_carillon_shots.py" -- "$EV/shots" clips $CLIPS > "$EV/shots-clips.log" 2>&1 &
( SHOT_SCALE=0.35 "$BL" -b "$EV/props/silt_bell_rig.blend" -P "$BLD/silt_carillon_shots.py" -- "$EV/shots_bell" turn > "$EV/shots-bell.log" 2>&1
  SHOT_SCALE=0.35 "$BL" -b "$EV/props/silt_bell_rig.blend" -P "$BLD/silt_carillon_shots.py" -- "$EV/shots_bell" clips $BCLIPS >> "$EV/shots-bell.log" 2>&1
  SHOT_SCALE=0.6 "$BL" -b "$EV/props/silt_mound.blend" -P "$BLD/silt_carillon_shots.py" -- "$EV/shots_mound" turn > "$EV/shots-mound.log" 2>&1 ) &
wait
cd "$EV"
S="$BLD/silt_sheet.py"
python "$S" turnaround.png 3 420 shots/turn*.png
python "$S" clips_close.png 5 300 shots/clip_*_close.png
python "$S" clips_game.png 5 300 shots/clip_*_game.png
python "$S" props.png 6 260 shots_bell/turn*.png shots_bell/clip_*_close.png shots_mound/turn*.png
ls -la "$EV"/*.png
