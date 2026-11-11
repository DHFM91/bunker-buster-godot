@echo off
rem Bunker Buster (Godot): build a Windows release and zip it for sharing.
rem Result: builds\BunkerBuster-windows.zip (unzip anywhere, run BunkerBuster.exe).
rem Needs Godot's export templates once: in Godot, Editor > Manage Export Templates > Download and Install.
rem Log: logs\export.txt

setlocal
cd /d "%~dp0.."
if not exist logs mkdir logs
if not exist builds\windows mkdir builds\windows
set LOG=logs\export.txt

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
if not defined GODOT (
  echo Godot .NET console exe not found: put it in C:\Godot or set GODOT. > %LOG%
  type %LOG%
  goto end
)

echo Bunker Buster export %DATE% %TIME% > %LOG%
del /q builds\windows\* 2>nul
for /d %%d in (builds\windows\*) do rmdir /s /q "%%d"
"%GODOT%" --headless --path . --import >> %LOG% 2>&1
"%GODOT%" --headless --path . --export-release "Windows Desktop" builds\windows\BunkerBuster.exe >> %LOG% 2>&1
if not exist builds\windows\BunkerBuster.exe (
  echo EXPORT FAILED - see above >> %LOG%
  type %LOG%
  goto end
)
del /q builds\BunkerBuster-windows.zip 2>nul
powershell -NoProfile -Command "Compress-Archive -Path 'builds\windows\*' -DestinationPath 'builds\BunkerBuster-windows.zip'" >> %LOG% 2>&1
echo EXPORT OK: builds\BunkerBuster-windows.zip >> %LOG%
type %LOG%
explorer builds

:end
endlocal
