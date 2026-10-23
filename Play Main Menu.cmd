@echo off
setlocal
set "MENU_PLAYER=%~dp0..\builds\menu-showcase-current\LastBell.exe"
if not exist "%MENU_PLAYER%" set "MENU_PLAYER=%~dp0unity\Builds\Menu\LastBell.exe"
if not exist "%MENU_PLAYER%" (
  echo Build the menu player in Unity: Last Bell ^> Main Menu ^> Build Windows Player.
  pause
  exit /b 1
)
start "" "%MENU_PLAYER%"
