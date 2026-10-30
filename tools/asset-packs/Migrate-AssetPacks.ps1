<#
.SYNOPSIS
Uploads the unchanged manifest archives to the project Azure Artifacts feed.
.DESCRIPTION
Run only after the feed exists and the owner has signed in. Existing packages are
downloaded and hash-checked, then skipped. New versions are published once.
The archive directory may be a cache copied from an older worktree; missing
archives are fetched from the legacy URL and verified before publication.
#>
[CmdletBinding()]
param([string]$ArchiveDirectory = '.asset-packs/cache')

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AssetPacks.psm1') -Force
$root = Get-RepoRoot
$manifest = Read-Manifest $root
$legacy = Get-Content (Join-Path $PSScriptRoot 'legacy-pack-inventory.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$archiveRoot = if ([IO.Path]::IsPathRooted($ArchiveDirectory)) { $ArchiveDirectory } else { Join-Path $root $ArchiveDirectory }
$scratch = Join-Path $root '.asset-packs/migrate'
[void](New-Item -ItemType Directory -Force $archiveRoot, $scratch)

foreach ($pack in @($manifest.packs) + @($legacy.packs)) {
    $source = Get-AzureSource $pack
    if (-not $source) { throw "No Azure source for $($pack.name)." }
    $archive = Join-Path $archiveRoot $pack.file
    if (-not (Test-Path $archive)) {
        if (-not $pack.url) { throw "Missing archive $($pack.file) and no legacy URL." }
        Write-Host "[get]  $($pack.file) from legacy release"
        $partial = "$archive.part"
        try { (New-Object Net.WebClient).DownloadFile($pack.url, $partial); Move-Item $partial $archive -Force }
        catch { if (Test-Path $partial) { Remove-Item $partial -Force }; throw "Could not obtain $($pack.file) from the legacy release." }
    }
    $actual = Get-Sha256 $archive
    if ($actual -ne $pack.sha256) { throw "Checksum mismatch for $($pack.file). Expected $($pack.sha256); actual $actual. No upload attempted." }

    $existing = Join-Path $scratch "$($pack.name)-existing"
    if (Test-Path $existing) { Remove-Item $existing -Recurse -Force }
    try {
        Invoke-UniversalDownload $source $pack.file $existing
        $remoteHash = Get-Sha256 (Join-Path $existing $pack.file)
        if ($remoteHash -ne $pack.sha256) { throw "Existing Azure package has the wrong checksum: $($pack.name) $($source.packageVersion). Never replace it." }
        Write-Host "[ok]   $($pack.name) $($source.packageVersion) already published and verified."
        continue
    } catch {
        if (Test-Path (Join-Path $existing $pack.file)) { throw }
        Write-Host "[new]  $($pack.name) $($source.packageVersion) is not downloadable yet. Publishing verified archive."
    }

    $publishDir = Join-Path $scratch "$($pack.name)-publish"
    [void](New-Item -ItemType Directory -Force $publishDir)
    Copy-Item -LiteralPath $archive -Destination (Join-Path $publishDir $pack.file) -Force
    Invoke-UniversalPublish $source $publishDir
    $verifyDir = Join-Path $scratch "$($pack.name)-verify"
    if (Test-Path $verifyDir) { Remove-Item $verifyDir -Recurse -Force }
    Invoke-UniversalDownload $source $pack.file $verifyDir
    $remoteHash = Get-Sha256 (Join-Path $verifyDir $pack.file)
    if ($remoteHash -ne $pack.sha256) { throw "Uploaded Azure package failed checksum verification: $($pack.name)." }
    Write-Host "[ok]   $($pack.name) $($source.packageVersion) uploaded and verified."
}
Write-Host 'Migration complete. Run setup-assets.cmd -Force on a fresh clone and confirm Azure is used.'
