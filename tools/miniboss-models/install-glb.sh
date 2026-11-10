#!/usr/bin/env bash
# Copies a rigged miniboss GLB into the Unity project the way Vesper Seedkeeper's is set up:
#   unity/Assets/LastBell/Characters/<Name>/Resources/Characters/<Name>.glb
# with folder metas and a .glb.meta copied from VesperSeedkeeper.glb.meta (glTFast, animationMethod: 2 = Mecanim, which
# the clips need) under new GUIDs. Existing metas are kept, so re-running only replaces the GLB.
#   tools/miniboss-models/install-glb.sh <Name> <path/to/Name.glb>
set -e
name="$1"; src="$2"
root="$(cd "$(dirname "$0")/../.." && pwd)"; chars="$root/unity/Assets/LastBell/Characters"
guid() { python -c 'import uuid; print(uuid.uuid4().hex)'; }
folder_meta() { [ -f "$1.meta" ] || sed "s/^guid: .*/guid: $(guid)/" "$chars/VesperSeedkeeper.meta" > "$1.meta"; }
mkdir -p "$chars/$name/Resources/Characters"
folder_meta "$chars/$name"; folder_meta "$chars/$name/Resources"; folder_meta "$chars/$name/Resources/Characters"
dst="$chars/$name/Resources/Characters/$name.glb"
cp "$src" "$dst"
[ -f "$dst.meta" ] || sed "s/^guid: .*/guid: $(guid)/" "$chars/VesperSeedkeeper/Resources/Characters/VesperSeedkeeper.glb.meta" > "$dst.meta"
grep -q "animationMethod: 2" "$dst.meta" || { echo "animationMethod is not 2 in $dst.meta"; exit 1; }
echo "installed $dst ($(stat -c %s "$dst") bytes)"
