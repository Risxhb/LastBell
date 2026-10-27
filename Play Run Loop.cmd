@echo off
rem Launches the Windows build of the Last Bell run loop (build it first: Unity menu Last Bell > Run Loop > Build Windows
rem Player, or the batch command in docs/run-loop/README.md).
set EXE=%~dp0unity\Builds\Windows\LastBell.exe
if not exist "%EXE%" (
  echo Build not found: %EXE%
  echo Build it with Last Bell ^> Run Loop ^> Build Windows Player in Unity.
  pause
  exit /b 1
)
start "" "%EXE%" -screen-fullscreen 0 -screen-width 1600 -screen-height 900
