@echo off
rem Builds (with the C# compiler built into Windows) and starts the class test arena. No Unity needed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\class-test-desktop\build.ps1" -Run
if errorlevel 1 pause
