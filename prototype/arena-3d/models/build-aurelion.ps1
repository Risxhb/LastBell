# Rebuilds the Unity Aurelion (unity/Assets/LastBell/Characters/Aurelion/Resources/Characters/Aurelion.glb) from the
# Meshy source with Blender: prep (4.2 m, glow mask for the eyes and pendulum), then rig + clips.
#   powershell -File build-aurelion.ps1 [-Source <meshy .glb>] [-Blender <blender.exe>]
param(
  [string]$Source = (Get-ChildItem (Join-Path $PSScriptRoot 'upload\Meshy') -Filter 'Meshy_AI_Aurelion_*texture.glb' | Select-Object -First 1).FullName,
  [string]$Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
)
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'lastbell-aurelion-build'; New-Item -ItemType Directory -Force $work | Out-Null
$dir = Join-Path $PSScriptRoot '..\..\..\unity\Assets\LastBell\Characters\Aurelion\Resources\Characters'
New-Item -ItemType Directory -Force $dir | Out-Null
$glb = Join-Path $dir 'Aurelion.glb'
& $Blender -b -P (Join-Path $PSScriptRoot 'blender\aurelion_prep.py') -- $Source $work 50000 | Select-String '^(TRIS|GLOW|HEAD)|Error|Traceback'
& $Blender -b (Join-Path $work 'aurelion_prep.blend') -P (Join-Path $PSScriptRoot 'blender\aurelion_rig.py') -- $work $glb | Select-String '^(RIGID|CLIPS|EXPORTED)|Error|Traceback'
