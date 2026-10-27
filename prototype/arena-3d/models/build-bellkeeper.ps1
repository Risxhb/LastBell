# Rebuilds the Unity Bellkeeper (unity/Assets/LastBell/Characters/Bellkeeper/Resources/Characters/Bellkeeper.glb)
# from the Meshy source with Blender: prep (5 m, ~55k tris, glow mask), then rig + idle.
#   powershell -File build-bellkeeper.ps1 [-Source <meshy .glb>] [-Blender <blender.exe>]
param(
  [string]$Source = (Get-ChildItem (Join-Path $PSScriptRoot 'upload\Meshy') -Filter 'Meshy_AI_Bellwarden_*texture.glb' | Select-Object -First 1).FullName,
  [string]$Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
)
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'lastbell-bellkeeper-build'; New-Item -ItemType Directory -Force $work | Out-Null
$glb = Join-Path $PSScriptRoot '..\..\..\unity\Assets\LastBell\Characters\Bellkeeper\Resources\Characters\Bellkeeper.glb'
& $Blender -b -P (Join-Path $PSScriptRoot 'blender\bellkeeper_prep.py') -- $Source $work 55000 | Select-String '^(TRIS|GLOW)|Error|Traceback'
& $Blender -b (Join-Path $work 'bellkeeper_prep.blend') -P (Join-Path $PSScriptRoot 'blender\bellkeeper_rig.py') -- $work $glb | Select-String '^(PROXY|PART|ROD|EXPORTED)|Error|Traceback'
