param([switch]$Run)
$ErrorActionPreference = 'Stop'
# Builds the Unity-free class-test window with the .NET Framework compiler that ships with Windows.
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$core = Join-Path $repo 'unity\Assets\LastBell\Core\ClassTest'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '.NET Framework 4 compiler not found' }
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe = Join-Path $out 'LastBellClassTest.exe'
$sources = @(Get-ChildItem -LiteralPath $core -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot 'ClassTestWindow.cs'
& $csc /nologo /target:winexe /optimize+ /warn:4 "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Output "Built $exe"
if ($Run) { Start-Process -FilePath $exe }
