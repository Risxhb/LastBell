@echo off
rem Downloads, verifies and extracts the large asset packs listed in asset-packs.json.
rem Run after cloning and whenever asset-packs.json changes. See docs/asset-packs.md.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\asset-packs\Get-AssetPacks.ps1" %*
exit /b %ERRORLEVEL%
