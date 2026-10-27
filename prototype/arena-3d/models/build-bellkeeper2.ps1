# Rebuilds Bellkeeper 2.0 (unity/Assets/LastBell/Characters/Bellkeeper2/Resources/Characters/Bellkeeper2.glb) from the
# Meshy source with Blender: prep (5 m, ~60k tris, glow mask), then rig + clips. Sheet: 04-bellkeeper-v1.png.
#   powershell -File build-bellkeeper2.ps1 [-Source <meshy .glb>] [-Blender <blender.exe>]
param(
  [string]$Source = (Get-ChildItem (Join-Path $PSScriptRoot '..\..\..\..\unity-session\prototype\arena-3d\models\upload\Meshy') -Filter 'Meshy_AI_Clockwork_Sentinel_*texture.glb' | Select-Object -First 1).FullName,
  [string]$Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
)
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'lastbell-bellkeeper2-build'; New-Item -ItemType Directory -Force $work | Out-Null
$glb = Join-Path $PSScriptRoot '..\..\..\unity\Assets\LastBell\Characters\Bellkeeper2\Resources\Characters\Bellkeeper2.glb'
New-Item -ItemType Directory -Force (Split-Path $glb) | Out-Null
# glow regions (x, y, z, radius, min red, glow): the mask's eye line and the chest clapper
$env:BK2_GLOW = '0,-0.68,4.0,0.18,0.55,1.0;0,-0.6,2.27,0.22,0.35,0.3'
& $Blender -b -P (Join-Path $PSScriptRoot 'blender\bellkeeper2_prep.py') -- $Source $work 60000 | Select-String '^(CENTRE|TRIS|GLOW|BRASS)|Error|Traceback'
& $Blender -b (Join-Path $work 'bellkeeper2_prep.blend') -P (Join-Path $PSScriptRoot 'blender\bellkeeper2_rig.py') -- $work $glb | Select-String '^(STANCE|SPLIT|UNWEIGHTED|CLIPS|EXPORTED)|Error|Traceback'
