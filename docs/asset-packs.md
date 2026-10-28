# Large asset packs

Large original models, textures, audio and video stay outside Git. `asset-packs.json` records each ZIP's SHA-256, size, extraction paths and preferred source. The private, project-scoped Azure Artifacts Universal Package feed `last-bell-assets` holds the published packs. Existing GitHub Release URLs remain in `url` as a temporary fallback. Never replace a published version.

## Setup

After cloning or changing the manifest, and **before opening Unity**, run:

```bat
setup-assets.cmd
```

Install [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-windows) and its Azure DevOps extension (`az extension add --name azure-devops`). Teammates need Feed Reader access. For **personal Microsoft accounts**, use a personal access token (PAT): `az login` can sign into an Azure directory identity that Azure DevOps treats as a different user, producing `TF400813` even when the personal account has feed permission.

Create the PAT in the browser:

1. Sign into `https://dev.azure.com/ChzandZum` as the personal account that has access to the `Last Bell` project.
2. Select the avatar/**User settings** at the top right → **Personal access tokens** → **+ New Token**.
3. Name it for Last Bell assets, choose organization **ChzandZum**, set an expiration, choose **Custom defined**, then **Show all scopes**.
4. Select **Packaging: Read & write** for publishing, or **Packaging: Read** if the token is only for `setup-assets.cmd`. Select **Create** and copy the token once.
5. Store it outside every Git checkout, for example in the current Windows user's `%USERPROFILE%\.lastbell\ado-pat.txt` file. Keep that file private to the user. The asset scripts read this file automatically when `AZURE_DEVOPS_EXT_PAT` is unset. Alternatively, set the variable for the current PowerShell session without displaying it:

   ```powershell
   $env:AZURE_DEVOPS_EXT_PAT = (Get-Content -Raw "$env:USERPROFILE\.lastbell\ado-pat.txt").Trim()
   ```

Run `setup-assets.cmd`. When a PAT is present, each Azure CLI call uses a temporary clean `AZURE_CONFIG_DIR` so a previous `az login` session cannot override the PAT. The scripts never print the token or change the user's normal Azure profile. The script downloads from Azure first, then the legacy URL on a source failure, and checks SHA-256 before extraction. A `TF400813` error now includes the sanitized Azure CLI line and explains the PAT route. Do not place the token, an Azure CLI profile, or any credential in Git.

`setup-assets.cmd -Name arena-assets` selects a pack. `setup-assets.cmd -Force` re-extracts, using a verified cached archive when possible. Rerunning without `-Force` skips installed versions. If Unity opened before setup and generated new `.meta` files, close it and use `-Force`.

The ignored `.asset-packs/cache/` stores downloaded ZIPs, `.asset-packs/installed/` stores installed versions, and `.asset-packs/out/` stores newly built ZIPs. Packs extract into the repository root and include the original Unity `.meta` files. A newer version removes only paths listed in the old version that it no longer carries. Failed checksum validation deletes the bad archive and extracts nothing from it.

For a local source test, set `LAST_BELL_ASSET_STORE_DIR` to a folder containing `<package>/<packageVersion>/<zip filename>`; the same folder also supports local publish testing. `LAST_BELL_ASSET_SOURCE_DIR` points directly to a folder of ZIPs and overrides the preferred download source. Neither variable should be used for ordinary team setup.

## Publishing

Only Chzy18 or Risx publishes. Obtain Feed Publisher access and use a PAT with Packaging: Read & write. Use a new integer pack version. Universal Package version `N.0.0` maps to ZIP `<name>-vN.zip`. Azure rejects reuse of a published version, including one later deleted. The script checks that the manifest is advancing and leaves the manifest untouched unless the publish succeeds.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/asset-packs/New-AssetPack.ps1 -Name arena-assets -Version 3 -Path unity/Assets/LastBell/Areas/Area1,unity/Assets/LastBell/Areas/Area3
powershell -NoProfile -ExecutionPolicy Bypass -File tools/asset-packs/New-AssetPack.ps1 -Name arena-assets -Version 3 -Path unity/Assets/LastBell/Areas/Area1,unity/Assets/LastBell/Areas/Area3 -Publish
```

The first command creates only a local ZIP. `-Publish` uploads that ZIP, then updates `asset-packs.json`, the managed `.gitignore` block and the Git index. The source files remain on disk. Review the resulting diff and commit on the feature branch. New Azure-only packs have no legacy URL; migrated packs retain theirs.

## Migration record

Chzy18 and Claude ran the first migration on 27 October 2026. The project-scoped private feed holds `current-player-assets` 1.0.0, `sylvara-assets` 1.0.0, `verdant-chimes-art` 1.0.0 and `arena-assets` 1.0.0 and 2.0.0. All five were checksum-verified after upload. The idempotent command remains available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/asset-packs/Migrate-AssetPacks.ps1 -ArchiveDirectory "..\merge-arenas\.asset-packs\cache"
```

The script verifies the three current manifest ZIPs and both archived arena versions listed in `tools/asset-packs/legacy-pack-inventory.json`. If a ZIP is absent it downloads the legacy URL and verifies it. For each Azure package, it downloads an existing version and compares hashes, skipping a match. Otherwise it publishes the original ZIP and downloads it again for hash verification. A conflicting existing version fails. A fresh clone at `d4be3d9` downloaded all three current packs from Azure, passed SHA-256 checks, and skipped them on rerun. The release URLs remain as fallback during this PR.

## Manifest format

`source` selects `azure-universal` with organization, project, feed, package and semantic `packageVersion`. `url` is the legacy fallback. `sha256` remains the content authority for both sources. The ZIP contents and original Unity `.meta` files do not change during migration.
