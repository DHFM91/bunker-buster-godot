@echo off
rem Bunker Buster (Godot): repackage builds\BunkerBuster-mac.zip as the portable Mac download.
rem Result: builds\BunkerBuster-mac-portable.zip (folder "Bunker Buster" with the launcher, readme and game).
rem export-mac.bat runs this automatically; double-click it to repackage an existing Mac export.
rem Log: logs\package-mac.txt

setlocal
cd /d "%~dp0.."
if not exist logs mkdir logs
set LOG=logs\package-mac.txt
echo Bunker Buster Mac packaging %DATE% %TIME% > %LOG%
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package-mac.ps1" >> %LOG% 2>&1
if errorlevel 1 echo PACKAGE FAILED - see above >> %LOG%
type %LOG%
endlocal
