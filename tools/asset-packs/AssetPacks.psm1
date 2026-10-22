# Shared helpers for the asset-pack scripts. Works in Windows PowerShell 5.1 and PowerShell 7.

Set-StrictMode -Version 3.0

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:ManifestName = 'asset-packs.json'

function Get-RepoRoot {
    $root = (& git rev-parse --show-toplevel 2>$null)
    if (-not $root) { throw 'Run this from inside the collab repository.' }
    return [IO.Path]::GetFullPath($root)
}

function Get-ManifestPath([string]$RepoRoot) {
    return Join-Path $RepoRoot $script:ManifestName
}

function Read-Manifest([string]$RepoRoot) {
    $path = Get-ManifestPath $RepoRoot
    if (-not (Test-Path $path)) {
        return [pscustomobject]@{ schema = 1; packs = @() }
    }
    $manifest = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $manifest.packs) { $manifest | Add-Member packs @() -Force }
    $manifest.packs = @($manifest.packs)
    return $manifest
}

function Write-Manifest([string]$RepoRoot, $Manifest) {
    $Manifest.packs = @($Manifest.packs | Sort-Object name)
    $json = $Manifest | ConvertTo-Json -Depth 6
    # UTF-8 without BOM and LF endings, so the file diffs cleanly for everyone.
    $json = $json -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Get-ManifestPath $RepoRoot), $json + "`n", (New-Object Text.UTF8Encoding $false))
}

function ConvertTo-RepoPath([string]$RepoRoot, [string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path $RepoRoot $Path }
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($RepoRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$Path' is outside the repository."
    }
    return $full.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

function Get-PackFiles([string]$RepoRoot, [string[]]$Paths) {
    # Returns repo-relative file paths (forward slashes) for every file under $Paths,
    # plus the .meta file Unity keeps beside each packed folder or file.
    $files = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($rel in $Paths) {
        $full = Join-Path $RepoRoot $rel
        if (Test-Path $full -PathType Container) {
            Get-ChildItem $full -Recurse -File -Force | ForEach-Object {
                [void]$files.Add((ConvertTo-RepoPath $RepoRoot $_.FullName))
            }
        } elseif (Test-Path $full -PathType Leaf) {
            [void]$files.Add($rel)
        } else {
            throw "Pack path not found: $rel"
        }
        $meta = "$($full.TrimEnd('\', '/')).meta"
        if (Test-Path $meta -PathType Leaf) { [void]$files.Add((ConvertTo-RepoPath $RepoRoot $meta)) }
    }
    return @($files)
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function New-PackZip([string]$RepoRoot, [string[]]$Files, [string]$ZipPath) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $zip = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($rel in $Files) {
            # Forward-slash entry names; PowerShell 5.1's Compress-Archive writes backslashes.
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, (Join-Path $RepoRoot $rel), $rel, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally {
        $zip.Dispose()
    }
}

function Expand-PackZip([string]$ZipPath, [string]$Destination) {
    $dest = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    $count = 0
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $dest $entry.FullName))
            if (-not $target.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to extract '$($entry.FullName)': it points outside $dest"
            }
            [void](New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            $count++
        }
    } finally {
        $zip.Dispose()
    }
    return $count
}

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    return '{0:N1} MB' -f ($Bytes / 1MB)
}

Export-ModuleMember -Function *
