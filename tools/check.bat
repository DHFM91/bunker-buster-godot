@echo off
rem Bunker Buster (Godot): compile the C#, import the assets, run the self-tests.
rem Everything is written to logs\check.txt so it can be read afterwards.
rem Usage: double-click, or run from this folder. Set GODOT to the Godot .NET console exe if it isn't found.

setlocal
cd /d "%~dp0.."
if not exist logs mkdir logs
set LOG=logs\check.txt

if not defined GODOT if exist "%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" set "GODOT=%USERPROFILE%\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not defined GODOT (
  for /f "delims=" %%f in ('dir /s /b "C:\Godot\Godot*mono*console.exe" 2^>nul') do set "GODOT=%%f"
)
if not defined GODOT (
  for /f "delims=" %%f in ('dir /b /s "%USERPROFILE%\Desktop\Godot*mono*console.exe" 2^>nul') do set "GODOT=%%f"
)
if not defined GODOT (
  for /f "delims=" %%f in ('dir /s /b "%USERPROFILE%\Downloads\Godot*mono*console.exe" 2^>nul') do set "GODOT=%%f"
)

echo Bunker Buster check %DATE% %TIME% > %LOG%
echo GODOT=%GODOT% >> %LOG%
echo. >> %LOG%

echo ===== dotnet build ===== >> %LOG%
dotnet build BunkerBuster.csproj -nologo -v q >> %LOG% 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> %LOG%
  goto done
)
echo BUILD OK >> %LOG%

if not defined GODOT (
  echo Godot .NET console exe not found: put it in C:\Godot or set GODOT. >> %LOG%
  goto done
)

echo. >> %LOG%
echo ===== import ===== >> %LOG%
"%GODOT%" --headless --path . --import >> %LOG% 2>&1

echo. >> %LOG%
echo ===== self-test ===== >> %LOG%
"%GODOT%" --headless --path . -- --selftest >> %LOG% 2>&1

:done
type %LOG%
endlocal
