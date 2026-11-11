#!/usr/bin/env bash
# Installs the Escapement Twins art-pass props (escapement_twins_props.py output) into Unity:
#   unity/Assets/LastBell/Characters/EscapementTwinsProps/Resources/EscapementTwinsProps/<Name>.glb
# Folder metas and .glb.meta are copied from Vesper Seedkeeper's under new GUIDs (same glTFast importer settings).
# Existing metas are kept, so re-running only replaces the GLBs.
#   tools/escapement-twins/install-props.sh <dir with PalletMarkBrass.glb, PalletMarkCobalt.glb, CounterpoiseWeight.glb>
set -e
src="$1"; root="$(cd "$(dirname "$0")/../.." && pwd)"; chars="$root/unity/Assets/LastBell/Characters"
guid() { python -c 'import uuid; print(uuid.uuid4().hex)'; }
folder_meta() { [ -f "$1.meta" ] || sed "s/^guid: .*/guid: $(guid)/" "$chars/VesperSeedkeeper.meta" > "$1.meta"; }
dir="$chars/EscapementTwinsProps/Resources/EscapementTwinsProps"; mkdir -p "$dir"
folder_meta "$chars/EscapementTwinsProps"; folder_meta "$chars/EscapementTwinsProps/Resources"; folder_meta "$dir"
for n in PalletMarkBrass PalletMarkCobalt CounterpoiseWeight; do
  cp "$src/$n.glb" "$dir/$n.glb"
  [ -f "$dir/$n.glb.meta" ] || sed "s/^guid: .*/guid: $(guid)/" "$chars/VesperSeedkeeper/Resources/Characters/VesperSeedkeeper.glb.meta" > "$dir/$n.glb.meta"
  echo "installed $n.glb ($(stat -c %s "$dir/$n.glb") bytes)"
done
