#!/usr/bin/env bash
# Fast compile check for unity/Assets (runtime + editor scripts) with Unity's bundled Roslyn: no Unity editor and no
# build lock needed. Needs unity/Library/ScriptAssemblies (copied or imported once) for the package assemblies.
#   bash tools/aurelion/compile-check.sh            (from the worktree root)
# Exit 0 = both assemblies compile. It does not replace the Play Mode autotests.
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
U="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Data}"
CSC="$U/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
DOTNET="$U/DotNetSdk/dotnet.exe"
SA="${SCRIPT_ASSEMBLIES:-$ROOT/unity/Library/ScriptAssemblies}"; [ -d "$SA" ] || SA="$ROOT/../run-loop/unity/Library/ScriptAssemblies"
OUT="${TMP:-/tmp}/lastbell-compile-check-$$"; mkdir -p "$OUT"
RSP="$OUT/runtime.rsp"; ERSP="$OUT/editor.rsp"
refs() {
  { echo "$U/NetStandard/ref/2.1.0/netstandard.dll"; ls "$U/Managed/UnityEngine/"*.dll
    ls "$SA/"*.dll | grep -v "/Assembly-CSharp"; } | cygpath -w -f - | sed 's/^\(.*\)$/-r:"\1"/'
}
HEAD="-nologo -noconfig -nostdlib+ -target:library -langversion:9.0 -nowarn:0169,0414,0649,0618,0067,0168,0219,0162,0414
-define:UNITY_EDITOR;UNITY_EDITOR_WIN;UNITY_STANDALONE_WIN;UNITY_6000_0_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2021_1_OR_NEWER;ENABLE_INPUT_SYSTEM;ENABLE_LEGACY_INPUT_MANAGER"
cd "$ROOT/unity"
{ echo "$HEAD"; echo "-out:\"$(cygpath -w "$OUT/Assembly-CSharp.dll")\""; refs
  find Assets -name "*.cs" -not -path "*/Editor/*" | cygpath -w -f - | sed 's/^\(.*\)$/"\1"/'; } > "$RSP"
{ echo "$HEAD"; echo "-out:\"$(cygpath -w "$OUT/Assembly-CSharp-Editor.dll")\""; refs; echo "-r:\"$(cygpath -w "$OUT/Assembly-CSharp.dll")\""
  find Assets -name "*.cs" -path "*/Editor/*" | cygpath -w -f - | sed 's/^\(.*\)$/"\1"/'; } > "$ERSP"
echo "== runtime"
"$DOTNET" "$CSC" "@$(cygpath -w "$RSP")" > "$OUT/runtime.log" 2>&1 || { grep -E "error" "$OUT/runtime.log" | sort -u; echo "RUNTIME COMPILE FAILED"; exit 1; }
echo "== editor"
"$DOTNET" "$CSC" "@$(cygpath -w "$ERSP")" > "$OUT/editor.log" 2>&1 || { grep -E "error" "$OUT/editor.log" | sort -u; echo "EDITOR COMPILE FAILED"; exit 1; }
rm -rf "$OUT"
echo "COMPILE OK"
