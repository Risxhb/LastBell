@echo off
setlocal
set "LASTBELL_PLAYER=%~dp0unity\Builds\Player\LastBell.exe"
if not exist "%LASTBELL_PLAYER%" set "LASTBELL_PLAYER=%~dp0..\builds\confrontation-menu\LastBell.exe"
if not exist "%LASTBELL_PLAYER%" (
  echo In Unity, build Last Bell ^> Current Player ^> Build Windows Player.
  pause
  exit /b 1
)
start "" "%LASTBELL_PLAYER%" -screen-fullscreen 0 -screen-width 1600 -screen-height 900
