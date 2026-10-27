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
    $stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($Path))
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hash.Dispose() }
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

function Invoke-Native([scriptblock]$Command) {
    # Windows PowerShell 5.1 turns a native tool's stderr into a terminating error under
    # $ErrorActionPreference = 'Stop'. Run the tool with errors relaxed and return its exit code.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command 2>&1 | ForEach-Object { "$_" } | Write-Host
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    return '{0:N1} MB' -f ($Bytes / 1MB)
}

function Get-AzureSource($Pack) {
    if ($Pack.PSObject.Properties['source'] -and $Pack.source) { return $Pack.source }
    return $null
}

function Get-AzExecutable {
    $command = Get-Command az -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $standardPath = Join-Path ${env:ProgramFiles} 'Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
    if (Test-Path $standardPath -PathType Leaf) { return $standardPath }
    throw 'Azure CLI is missing. Install Azure CLI and its azure-devops extension before using the private asset feed.'
}

function Get-AssetPat {
    if ($env:AZURE_DEVOPS_EXT_PAT) { return $env:AZURE_DEVOPS_EXT_PAT.Trim() }
    $path = Join-Path $env:USERPROFILE '.lastbell\ado-pat.txt'
    if (Test-Path $path -PathType Leaf) { return [IO.File]::ReadAllText($path).Trim() }
    return ''
}

function Get-SafeAzError([string[]]$Lines, [string]$Pat) {
    $line = @($Lines | Where-Object { $_ -match 'TF400813|(?i)ERROR|(?i)error:' } | Select-Object -First 1)
    if (-not $line) { $line = @($Lines | Where-Object { $_.Trim() } | Select-Object -Last 1) }
    if (-not $line) { return 'Azure CLI returned an error without details.' }
    $safe = [string]$line[0]
    if ($Pat) { $safe = $safe.Replace($Pat, '[redacted]') }
    $safe = [regex]::Replace($safe, '(?i)(authorization\s*[:=]\s*(?:basic|bearer)\s+)\S+', '$1[redacted]')
    $safe = [regex]::Replace($safe, '(?i)((?:token|pat|sig|password)\s*[:=]\s*)\S+', '$1[redacted]')
    if ($safe.Length -gt 500) { $safe = $safe.Substring(0, 500) + '...' }
    return $safe
}

function Invoke-AzArtifact([string[]]$Arguments) {
    $az = Get-AzExecutable
    $pat = Get-AssetPat
    $oldPat = $env:AZURE_DEVOPS_EXT_PAT
    $oldConfig = $env:AZURE_CONFIG_DIR
    $oldExtensions = $env:AZURE_EXTENSION_DIR
    $cleanConfig = $null
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($pat) {
            # Azure CLI favors an existing az login identity over AZURE_DEVOPS_EXT_PAT.
            # A fresh profile makes the PAT authoritative without changing the user's login.
            $cleanConfig = Join-Path ([IO.Path]::GetTempPath()) ("lastbell-az-" + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $cleanConfig)
            $env:AZURE_CONFIG_DIR = $cleanConfig
            $env:AZURE_DEVOPS_EXT_PAT = $pat
            if (-not $oldExtensions) {
                $extensions = Join-Path $env:USERPROFILE '.azure\cliextensions'
                if (Test-Path $extensions -PathType Container) { $env:AZURE_EXTENSION_DIR = $extensions }
            }
        } else {
            & $az account show --output none --only-show-errors *> $null
            if ($LASTEXITCODE -ne 0) {
                throw 'Azure sign-in missing. Personal Microsoft accounts should use a Packaging PAT in AZURE_DEVOPS_EXT_PAT or %USERPROFILE%\.lastbell\ado-pat.txt.'
            }
        }
        $lines = @(& $az @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $code = $LASTEXITCODE
        if ($code -ne 0) {
            $detail = Get-SafeAzError $lines $pat
            if ($detail -match 'TF400813') {
                throw "Azure DevOps rejected the CLI identity (TF400813). For a personal Microsoft account, use a Packaging PAT; this script gives it a clean Azure profile. Azure CLI: $detail"
            }
            throw "Azure CLI failed (exit $code). Azure CLI: $detail"
        }
    } finally {
        $ErrorActionPreference = $previous
        $env:AZURE_DEVOPS_EXT_PAT = $oldPat
        $env:AZURE_CONFIG_DIR = $oldConfig
        $env:AZURE_EXTENSION_DIR = $oldExtensions
        if ($cleanConfig) {
            $resolved = [IO.Path]::GetFullPath($cleanConfig)
            $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
            if ($resolved.StartsWith($tempRoot + 'lastbell-az-', [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Invoke-UniversalDownload($Source, [string]$FileName, [string]$Destination) {
    if ($Source.type -ne 'azure-universal') { throw "Unsupported asset source: $($Source.type)" }
    if ($env:LAST_BELL_ASSET_STORE_DIR) {
        [void](New-Item -ItemType Directory -Force $Destination)
        $local = Join-Path $env:LAST_BELL_ASSET_STORE_DIR "$($Source.package)/$($Source.packageVersion)/$FileName"
        Copy-Item -LiteralPath $local -Destination (Join-Path $Destination $FileName) -ErrorAction Stop
        return
    }
    [void](New-Item -ItemType Directory -Force $Destination)
    $args = @('artifacts','universal','download','--organization',$Source.organization,'--project',$Source.project,
        '--scope','project','--feed',$Source.feed,'--name',$Source.package,'--version',$Source.packageVersion,
        '--path',$Destination,'--file-filter',$FileName,'--only-show-errors','--output','none')
    Invoke-AzArtifact $args
    if (-not (Test-Path (Join-Path $Destination $FileName))) {
        throw "Azure Artifacts download returned no $FileName for $($Source.package) $($Source.packageVersion)."
    }
}

function Invoke-UniversalPublish($Source, [string]$Directory) {
    if ($env:LAST_BELL_ASSET_STORE_DIR) {
        $target = Join-Path $env:LAST_BELL_ASSET_STORE_DIR "$($Source.package)/$($Source.packageVersion)"
        if (Test-Path $target) { throw "Package version already exists: $($Source.package) $($Source.packageVersion)." }
        [void](New-Item -ItemType Directory -Force $target)
        Get-ChildItem -LiteralPath $Directory -Filter '*.zip' -File | Copy-Item -Destination $target
        return
    }
    $args = @('artifacts','universal','publish','--organization',$Source.organization,'--project',$Source.project,
        '--scope','project','--feed',$Source.feed,'--name',$Source.package,'--version',$Source.packageVersion,
        '--path',$Directory,'--only-show-errors','--output','none')
    Invoke-AzArtifact $args
}

function New-UniversalSource([string]$Name, [int]$Version, [string]$Feed) {
    return [pscustomobject][ordered]@{
        type = 'azure-universal'
        organization = 'https://dev.azure.com/ChzandZum'
        project = 'Last Bell'
        feed = $Feed
        package = $Name
        packageVersion = "$Version.0.0"
    }
}

Export-ModuleMember -Function *
