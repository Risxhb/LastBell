# Large asset packs

Big binary files don't go in Git: large models, textures, audio and video. They go into versioned ZIP **asset packs** attached to GitHub Releases. The repository only tracks a small manifest, [`asset-packs.json`](../asset-packs.json), which lists each pack's download URL, SHA-256 checksum and extraction location. A setup script downloads, verifies and extracts the packs.

## Setup (everyone)

Run this from the repository root, in Command Prompt, PowerShell or by double-clicking:

```bat
setup-assets.cmd
```

It is the same as:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/asset-packs/Get-AssetPacks.ps1
```

**When to run it:**
- right after cloning;
- again whenever `asset-packs.json` changes: after a `git pull`, a merge, or switching to a branch with different packs;
- **before opening the Unity project.** If Unity opens first and finds a missing asset folder, it may create new `.meta` files with different GUIDs. If that happens, close Unity and run `setup-assets.cmd -Force`.

Rerunning is cheap. Packs already installed at the listed version are skipped (`[ok] … is already installed`).

Options:
- `setup-assets.cmd -Force` re-extracts every pack, overwriting local copies (it reuses the cached ZIP if its checksum matches).
- `setup-assets.cmd -Name arena-assets` installs just that pack.

No GitHub login is needed: the repository is public, so the Release URLs download directly.

## Where assets are extracted

Every pack extracts into the **repository root** (`extractTo: "."` in the manifest). The ZIP stores repo-relative paths, so a pack built from `unity/Assets/LastBell/Areas/Area3` lands back at `unity/Assets/LastBell/Areas/Area3` in your checkout. Each manifest entry's `paths` field lists exactly which folders a pack owns.

The script also uses a local `.asset-packs/` folder, which Git ignores:
- `.asset-packs/cache/` holds downloaded ZIPs.
- `.asset-packs/installed/` records which version of each pack is installed and its file list.
- `.asset-packs/out/` holds ZIPs built by `New-AssetPack.ps1`.

When a pack moves to a new version, files the old version had but the new one dropped are deleted. Nothing outside the pack's own files is touched.

## Unity `.meta` files

Packs include Unity's **original** `.meta` files: the one beside every file and folder, and the `.meta` beside each packed top-level folder (for example `Area3.meta`). Extracting them keeps every asset's GUID identical on every machine, so scene and prefab references stay intact. Don't let Unity regenerate them. That's why setup must run before Unity opens the project.

## When something fails

The script stops with a red `[FAIL]` line and exit code 1. It never extracts a file it couldn't verify.

- **Download failed:** it prints the URL and the reason (for example `(404) Not Found` or a network error). Check your connection and that the release still exists, then rerun.
- **Checksum mismatch:** it prints the expected and actual SHA-256, deletes the bad download and extracts nothing. Rerun once. If it fails again, the release file and `asset-packs.json` disagree; tell whoever published the pack.

## Stable, versioned URLs

Each pack version is its own release, tagged `<name>-v<version>`, with one ZIP named `<name>-v<version>.zip`. The download URL is therefore:

```text
https://github.com/Risxhb/collab/releases/download/<name>-v<version>/<name>-v<version>.zip
```

A published version is **never replaced or edited**. `New-AssetPack.ps1` refuses to reuse a version that's already in the manifest or already released. To change a pack, publish the next version. Old versions stay downloadable, so older branches and commits keep working.

## Publishing a pack (maintainers)

You need the asset files on disk, and the [GitHub CLI](https://cli.github.com/) logged in with push access.

1. On a branch, do a dry run. It builds the ZIP in `.asset-packs/out/` and changes nothing else:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/asset-packs/New-AssetPack.ps1 -Name arena-assets -Version 1 -Path unity/Assets/LastBell/Areas/Area1,unity/Assets/LastBell/Areas/Area3
   ```

2. Publish it by running the same command with `-Publish` added. This:
   - creates the release `arena-assets-v1` and uploads the ZIP;
   - adds the pack to `asset-packs.json`;
   - adds its paths (and their folder `.meta` files) to the managed block in `.gitignore`;
   - removes them from the Git index. The files stay on your disk.

3. Commit `asset-packs.json`, `.gitignore` and the index removals, then open a PR as usual.

To update a pack, rerun with the same `-Name`, the same paths and `-Version` raised by one.

Removing files from the index stops Git tracking them from that commit onward, but they stay in the history of older commits. Shrinking the repository's history is a separate, deliberate job.

## Files

| File | Purpose |
|---|---|
| `asset-packs.json` | Manifest: name, version, file, url, sha256, size, extractTo, paths, files |
| `setup-assets.cmd` | One-step setup wrapper |
| `tools/asset-packs/Get-AssetPacks.ps1` | Download, verify, extract |
| `tools/asset-packs/New-AssetPack.ps1` | Package, publish, update the manifest and `.gitignore` |
| `tools/asset-packs/AssetPacks.psm1` | Shared helpers (ZIP, hashing, paths) |

The scripts work in Windows PowerShell 5.1 and PowerShell 7.
