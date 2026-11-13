@echo off
rem Bunker Buster (Godot): build the macOS release (one app for Intel and Apple Silicon Macs).
rem Result: builds\BunkerBuster-mac.zip containing BunkerBuster.app, plus "Mac - read me.txt".
rem Log: logs\export-mac.txt

setlocal
cd /d "%~dp0.."
if not exist logs mkdir logs
if not exist builds mkdir builds
set LOG=logs\export-mac.txt

if not defined GODOT if exist "%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" set "GODOT=%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not defined GODOT (
  for /f "delims=" %%f in ('dir /s /b "C:\Godot\Godot*mono*console.exe" 2^>nul') do set "GODOT=%%f"
)
if not defined GODOT (
  echo Godot .NET console exe not found: put it in C:\Godot or set GODOT. > %LOG%
  type %LOG%
  goto end
)

echo Bunker Buster macOS export %DATE% %TIME% > %LOG%
set "TPL=%APPDATA%\Godot\export_templates\4.7.2.stable.mono"
if not exist "%TPL%\macos.zip" echo macOS export template missing: run tools\export.bat once first. >> %LOG%
del /q builds\BunkerBuster-mac.zip 2>nul
"%GODOT%" --headless --path . --import >> %LOG% 2>&1
"%GODOT%" --headless --path . --export-release "macOS" builds\BunkerBuster-mac.zip >> %LOG% 2>&1
if not exist builds\BunkerBuster-mac.zip (
  echo EXPORT FAILED - see above >> %LOG%
  type %LOG%
  goto end
)
dir builds >> %LOG% 2>&1
echo EXPORT OK: builds\BunkerBuster-mac.zip >> %LOG%
type %LOG%

:end
endlocal
