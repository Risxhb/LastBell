# Rebuilds the Unity Sylvara (unity/Assets/LastBell/Characters/Sylvara/Resources/Characters/Sylvara.glb) from the Meshy
# source with Blender: prep (4 m, seam merge, eye glow), then rig (four IK root legs) and clips.
# The Meshy file is named "Verdant Bellwarden" (Meshy's auto-title); it is Sylvara.
#   powershell -File build-sylvara.ps1 [-Source <meshy .glb>] [-Blender <blender.exe>]
param(
  [string]$Source = (Get-ChildItem (Join-Path $PSScriptRoot '..\..\..\..\unity-session\prototype\arena-3d\models\upload\Meshy') -Filter 'Meshy_AI_Verdant_Bellwarden_*texture.glb' | Select-Object -First 1).FullName,
  [string]$Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
)
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'lastbell-sylvara-build'; New-Item -ItemType Directory -Force $work | Out-Null
$glb = Join-Path $PSScriptRoot '..\..\..\unity\Assets\LastBell\Characters\Sylvara\Resources\Characters\Sylvara.glb'
& $Blender -b -P (Join-Path $PSScriptRoot 'blender\sylvara_prep.py') -- $Source $work | Select-String '^(FEET|TRIS|GLOW)|Error|Traceback'
& $Blender -b (Join-Path $work 'sylvara_prep.blend') -P (Join-Path $PSScriptRoot 'blender\sylvara_rig.py') -- $work $glb | Select-String '^(SEAM|PINNED|RIGID|UNWEIGHTED|CLIPS|EXPORTED)|Error|Traceback'
