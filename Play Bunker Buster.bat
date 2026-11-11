@echo off
rem Bunker Buster (Godot): build the C# and start the game. No export needed.
cd /d "%~dp0"
set "GODOT=%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%GODOT%" (
  echo Godot .NET not found at %GODOT%
  pause
  exit /b
)
if not exist logs mkdir logs
dotnet build BunkerBuster.csproj -nologo -v q > logs\play-build.txt 2>&1
if not exist .godot "%GODOT%" --headless --path . --import > logs\play-import.txt 2>&1
start "" "%GODOT%" --path . --log-file logs\play-log.txt
