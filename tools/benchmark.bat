@echo off
rem Frame-rate benchmark: builds, then flies a fixed path through a fixed level with vsync off.
rem Results are appended to logs\benchmark.txt.
cd /d "%~dp0.."
set "GODOT=%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not exist logs mkdir logs
dotnet build BunkerBuster.csproj -nologo -v q > logs\benchmark-build.txt 2>&1
"%GODOT%" --path . -- --benchmark > logs\benchmark-run.txt 2>&1
