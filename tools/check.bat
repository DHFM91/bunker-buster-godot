@echo off
rem Bunker Buster (Godot): compile the C#, import the assets, run the self-tests.
rem Everything is written to logs\check.txt so it can be read afterwards.
rem Usage: double-click, or run from this folder. Set GODOT to the Godot .NET console exe if it isn't found.

setlocal
cd /d "%~dp0.."
if not exist logs mkdir logs
set LOG=logs\check.txt

if not defined GODOT (
  for /r "C:\Godot" %%f in (Godot*mono*console.exe) do set "GODOT=%%f"
)
if not defined GODOT (
  for /r "%USERPROFILE%\Desktop" %%f in (Godot*mono*console.exe) do set "GODOT=%%f"
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
