<#
.SYNOPSIS
Packages large asset folders into a versioned ZIP, publishes it as a GitHub Release
and records it in asset-packs.json.

.DESCRIPTION
Run from a checkout that has the asset files on disk. The script:
  1. zips the given paths, keeping Unity's original .meta files (including the .meta
     beside each packed folder) with repo-relative paths;
  2. computes the ZIP's SHA-256.
Without -Publish it stops there (a dry run: check the ZIP in .asset-packs/out/).
With -Publish it also:
  3. creates the GitHub Release <Name>-v<Version> and uploads the ZIP (needs gh, logged in);
  4. adds or updates the pack's entry in asset-packs.json;
  5. adds the paths to .gitignore and removes them from the Git index
     (the files stay on disk).
Then commit asset-packs.json and .gitignore.

A published version is never replaced, so its download URL stays stable.
To change a pack, publish a new version.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/asset-packs/New-AssetPack.ps1 -Name arena-assets -Version 1 -Path unity/Assets/LastBell/Areas/Area1,unity/Assets/LastBell/Areas/Area3 -Publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9][a-z0-9-]*$')] [string]$Name,
    [Parameter(Mandatory)] [ValidateRange(1, 9999)] [int]$Version,
    [Parameter(Mandatory)] [string[]]$Path,
    [string]$Repo = 'Risxhb/collab',
    [string]$Notes,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AssetPacks.psm1') -Force

$root = Get-RepoRoot
$manifest = Read-Manifest $root
$tag = "$Name-v$Version"
$zipName = "$tag.zip"

$existing = @($manifest.packs | Where-Object { $_.name -eq $Name })
if ($existing -and $existing[0].version -ge $Version) {
    throw "asset-packs.json already has $Name v$($existing[0].version). Use -Version $($existing[0].version + 1)."
}

$paths = @($Path | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | ForEach-Object { ConvertTo-RepoPath $root $_.Trim() })
$files = Get-PackFiles $root $paths
if ($files.Count -eq 0) { throw 'No files found under the given paths.' }
$metaCount = @($files | Where-Object { $_ -like '*.meta' }).Count

$outDir = Join-Path $root '.asset-packs\out'
[void](New-Item -ItemType Directory -Force $outDir)
$zipPath = Join-Path $outDir $zipName
Write-Host "Packing $($files.Count) files ($metaCount .meta) into $zipName ..."
New-PackZip $root $files $zipPath
$sha = Get-Sha256 $zipPath
$size = (Get-Item $zipPath).Length
Write-Host "  $(Format-Size $size), sha256 $sha"

$url = "https://github.com/$Repo/releases/download/$tag/$zipName"

if ($Publish) {
    & gh release view $tag --repo $Repo *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Release $tag already exists on $Repo. Published versions are never replaced; use a new -Version."
    }
    $bt = [string][char]96
    $pathList = ($paths | ForEach-Object { $bt + $_ + $bt }) -join ', '
    $sizeText = Format-Size $size
    $body = (@(
        "Asset pack **$Name** v$Version for Last Bell.",
        '',
        "- File: $bt$zipName$bt ($sizeText, $($files.Count) files, $metaCount Unity .meta files)",
        "- SHA-256: $bt$sha$bt",
        "- Extracts to the repository root. Paths: $pathList",
        '',
        "Install with ${bt}setup-assets.cmd$bt (see docs/asset-packs.md). Don't edit or replace this release; publish a new version instead."
    ) -join "`n")
    if ($Notes) { $body += "`n`n$Notes" }
    $notesFile = Join-Path $outDir "$tag-notes.md"
    [IO.File]::WriteAllText($notesFile, $body, (New-Object Text.UTF8Encoding $false))
    Write-Host "Publishing release $tag on $Repo ..."
    & gh release create $tag $zipPath --repo $Repo --title "Asset pack: $Name v$Version" --notes-file $notesFile --target main
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed for $tag." }
} else {
    # A dry run leaves asset-packs.json and .gitignore alone, so nobody commits a URL that doesn't exist yet.
    Write-Host "Dry run: built $zipPath only. Re-run with -Publish to create release $tag and update asset-packs.json."
    return
}

$entry = [pscustomobject][ordered]@{
    name      = $Name
    version   = $Version
    file      = $zipName
    url       = $url
    sha256    = $sha
    size      = $size
    extractTo = '.'
    paths     = $paths
    files     = $files.Count
}
$manifest.packs = @($manifest.packs | Where-Object { $_.name -ne $Name }) + $entry
Write-Manifest $root $manifest
Write-Host 'Updated asset-packs.json.'

# Keep the packed files out of Git from now on.
$ignorePath = Join-Path $root '.gitignore'
$ignore = if (Test-Path $ignorePath) { [IO.File]::ReadAllText($ignorePath) -replace "`r`n", "`n" } else { '' }
$lines = @($ignore -split "`n")
$begin = '# >>> asset packs (managed by tools/asset-packs; do not edit by hand)'
$end = '# <<< asset packs'
$start = [Array]::IndexOf($lines, $begin)
$stop = [Array]::IndexOf($lines, $end)
$block = @()
if ($start -ge 0 -and $stop -gt $start) {
    $block += $lines[($start + 1)..($stop - 1)] | Where-Object { $_ }
    $lines = @($lines[0..($start - 1)]) + @($lines[($stop + 1)..($lines.Count - 1)])
}
foreach ($p in $paths) { $block += "/$p"; $block += "/$p.meta" }
$block = @($block | Select-Object -Unique)
$kept = (($lines -join "`n").TrimEnd("`n"))
$text = $kept + "`n`n" + (@($begin) + $block + @($end) -join "`n") + "`n"
[IO.File]::WriteAllText($ignorePath, $text, (New-Object Text.UTF8Encoding $false))
Write-Host 'Updated .gitignore.'

& git -C $root rm -r -q --cached --ignore-unmatch -- @($paths + ($paths | ForEach-Object { "$_.meta" }))
Write-Host ''
Write-Host "Done. Commit asset-packs.json and .gitignore (and the index removals) on your branch."
Write-Host "Download URL: $url"
