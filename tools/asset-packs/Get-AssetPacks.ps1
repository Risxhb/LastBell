<#
.SYNOPSIS
Downloads, verifies and extracts every asset pack listed in asset-packs.json.

.DESCRIPTION
Run this after cloning, and again whenever asset-packs.json changes (after a pull,
merge or branch switch). Packs that are already installed at the listed version are
skipped, so rerunning is cheap.

For each pack the script:
  1. downloads the ZIP into .asset-packs/cache/ (reusing a cached copy if its hash matches);
  2. checks the SHA-256 against asset-packs.json and stops with an error on a mismatch;
  3. extracts it into the repository root, overwriting older copies;
  4. removes files that an older installed version had but the new version dropped;
  5. records the installed version in .asset-packs/installed/.

Unity's original .meta files come from the ZIP, so GUID references match everyone
else's. Run this before opening the Unity project; if Unity opens first it creates new
.meta files with different GUIDs. If that happens, close Unity and run with -Force.

.EXAMPLE
setup-assets.cmd
.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/asset-packs/Get-AssetPacks.ps1 -Force
#>
[CmdletBinding()]
param(
    [string[]]$Name,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AssetPacks.psm1') -Force

# Windows PowerShell 5.1 defaults to older TLS versions that GitHub refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Get-RepoRoot
$manifest = Read-Manifest $root
$packs = @($manifest.packs)
if ($Name) { $packs = @($packs | Where-Object { $Name -contains $_.name }) }
if ($packs.Count -eq 0) {
    Write-Host 'No asset packs to install.'
    exit 0
}

$cacheDir = Join-Path $root '.asset-packs\cache'
$stateDir = Join-Path $root '.asset-packs\installed'
[void](New-Item -ItemType Directory -Force $cacheDir, $stateDir)

$failed = @()
foreach ($pack in $packs) {
    $label = "$($pack.name) v$($pack.version)"
    $statePath = Join-Path $stateDir "$($pack.name).json"
    $state = if (Test-Path $statePath) { Get-Content $statePath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }

    if (-not $Force -and $state -and $state.sha256 -eq $pack.sha256) {
        Write-Host "[ok]   $label is already installed."
        continue
    }

    try {
        $zipPath = Join-Path $cacheDir $pack.file
        if ((Test-Path $zipPath) -and (Get-Sha256 $zipPath) -ne $pack.sha256) { Remove-Item $zipPath -Force }
        if (-not (Test-Path $zipPath)) {
            $partial = "$zipPath.part"
            if (Test-Path $partial) { Remove-Item $partial -Force }
            $primaryError = $null
            try {
                if ($env:LAST_BELL_ASSET_SOURCE_DIR) {
                    Write-Host "[get]  $label from local source override"
                    Copy-Item -LiteralPath (Join-Path $env:LAST_BELL_ASSET_SOURCE_DIR $pack.file) -Destination $partial -ErrorAction Stop
                } elseif ($source = Get-AzureSource $pack) {
                    Write-Host "[get]  $label from Azure Artifacts $($source.feed)/$($source.package) $($source.packageVersion)"
                    $downloadDir = Join-Path $cacheDir "$($pack.file).azure"
                    Invoke-UniversalDownload $source $pack.file $downloadDir
                    Move-Item -LiteralPath (Join-Path $downloadDir $pack.file) -Destination $partial -Force
                } else {
                    throw 'No primary source configured.'
                }
            } catch {
                $primaryError = $_.Exception.Message
                if (Test-Path $partial) { Remove-Item $partial -Force }
            }
            if ($primaryError) {
                if (-not $pack.url) { throw "Download failed for $label. $primaryError" }
                Write-Host "[warn] $primaryError Falling back to the legacy GitHub release."
                try { (New-Object Net.WebClient).DownloadFile($pack.url, $partial) }
                catch {
                    if (Test-Path $partial) { Remove-Item $partial -Force }
                    throw "Download failed for $label from Azure and legacy URL. $primaryError Legacy URL: $($pack.url)."
                }
            }
            Move-Item $partial $zipPath -Force
        }

        $actual = Get-Sha256 $zipPath
        if ($actual -ne $pack.sha256) {
            Remove-Item $zipPath -Force
            throw "Checksum mismatch for $label; the download was deleted and nothing was extracted.`n  Expected: $($pack.sha256)`n  Actual:   $actual`n  Run setup again. If it keeps failing, the release file or asset-packs.json is wrong: tell whoever published the pack."
        }

        $dest = Join-Path $root $pack.extractTo
        Write-Host "[unzip] $label into $([IO.Path]::GetFullPath($dest))"
        $count = Expand-PackZip $zipPath $dest

        # Remove files the previous installed version had that this version no longer ships.
        $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try { $newFiles = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
        if ($state -and $state.fileList) {
            $keep = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            foreach ($f in $newFiles) { [void]$keep.Add($f) }
            foreach ($old in @($state.fileList)) {
                if (-not $keep.Contains($old)) {
                    $oldPath = Join-Path $dest $old
                    if (Test-Path $oldPath -PathType Leaf) { Remove-Item $oldPath -Force }
                }
            }
        }

        $record = [pscustomobject][ordered]@{
            name = $pack.name; version = $pack.version; sha256 = $pack.sha256
            installed = (Get-Date).ToString('s'); fileList = $newFiles
        }
        [IO.File]::WriteAllText($statePath, ($record | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
        Write-Host "[ok]   $label installed ($count files)."
    } catch {
        Write-Host "[FAIL] $($_.Exception.Message)" -ForegroundColor Red
        $failed += $label
    }
}

if ($failed.Count -gt 0) {
    Write-Host ''
    Write-Host "Asset setup FAILED for: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host ''
Write-Host 'All asset packs are installed and verified.'
