@echo off
rem Renders the soldier model from 8 angles in 6 poses (logs\modelshots\*.png) and exports a .glb.
cd /d "%~dp0.."
set "GODOT=%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not exist logs mkdir logs
dotnet build BunkerBuster.csproj -nologo -v q > logs\modelshots-build.txt 2>&1
"%GODOT%" --path . -- --modelshots > logs\modelshots.txt 2>&1
