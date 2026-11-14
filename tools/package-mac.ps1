# Bunker Buster (Godot): turn Godot's macOS export into the portable Mac download.
#
#   Bunker Buster/
#     Play Bunker Buster.command   (launcher: clears quarantine, checks the signature, starts the game)
#     Mac - read me first.txt
#     game/Bunker Buster.app
#
# Zip entries get Unix permissions (755 for the launcher and the app's executables) so they
# survive unzipping on a Mac. Windows PowerShell 5.1 is enough; nothing to install.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\package-mac.ps1 [source.zip] [output.zip]

param(
  [string]$Src = 'builds\BunkerBuster-mac.zip',
  [string]$Out = 'builds\BunkerBuster-mac-portable.zip'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$macDir = Join-Path $PSScriptRoot 'mac'
$Src = [System.IO.Path]::GetFullPath((Join-Path $root $Src))
$Out = [System.IO.Path]::GetFullPath((Join-Path $root $Out))
$top = 'Bunker Buster/'

# Read every entry's name and Unix mode from a zip's central directory.
function Get-ZipModes([byte[]]$b) {
  $eocd = -1
  for ($i = $b.Length - 22; $i -ge [Math]::Max(0, $b.Length - 65557); $i--) {
    if ($b[$i] -eq 0x50 -and $b[$i+1] -eq 0x4b -and $b[$i+2] -eq 0x05 -and $b[$i+3] -eq 0x06) { $eocd = $i; break }
  }
  if ($eocd -lt 0) { throw 'Not a zip file (no end-of-central-directory record).' }
  $count = [BitConverter]::ToUInt16($b, $eocd + 10)
  $p = [int][BitConverter]::ToUInt32($b, $eocd + 16)
  $list = New-Object System.Collections.Generic.List[object]
  for ($n = 0; $n -lt $count; $n++) {
    if ([BitConverter]::ToUInt32($b, $p) -ne 0x02014b50) { throw "Bad central directory entry at $p." }
    $nameLen = [BitConverter]::ToUInt16($b, $p + 28)
    $extraLen = [BitConverter]::ToUInt16($b, $p + 30)
    $commentLen = [BitConverter]::ToUInt16($b, $p + 32)
    $list.Add([pscustomobject]@{
      Offset = $p
      Host   = $b[$p + 5]
      Mode   = [BitConverter]::ToUInt32($b, $p + 38) -shr 16
      Name   = [System.Text.Encoding]::UTF8.GetString($b, $p + 46, $nameLen)
    })
    $p += 46 + $nameLen + $extraLen + $commentLen
  }
  return $list
}

if (-not (Test-Path $Src)) { throw "Missing $Src - run tools\export-mac.bat first." }
foreach ($f in 'Play Bunker Buster.command', 'Mac - read me first.txt') {
  if (-not (Test-Path (Join-Path $macDir $f))) { throw "Missing tools\mac\$f" }
}

# Modes of the files in Godot's export (Godot writes them as Unix entries).
$srcModes = @{}
foreach ($e in (Get-ZipModes ([System.IO.File]::ReadAllBytes($Src)))) {
  if ($e.Host -ne 3) { throw "Godot's zip entry is not a Unix entry: $($e.Name)" }
  $srcModes[$e.Name] = $e.Mode
}

if (Test-Path $Out) { Remove-Item $Out -Force }
$wanted = @{}   # entry name -> Unix mode
$srcZip = [System.IO.Compression.ZipFile]::OpenRead($Src)
$fs = [System.IO.File]::Open($Out, [System.IO.FileMode]::CreateNew)
$dst = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($d in $top, ($top + 'game/')) {
    [void]$dst.CreateEntry($d)
    $wanted[$d] = 0x41ED          # drwxr-xr-x
  }
  foreach ($f in @(@('Play Bunker Buster.command', 0x81ED), @('Mac - read me first.txt', 0x81A4))) {
    # LF line endings: a launcher with Windows line endings won't run on a Mac.
    $text = [System.IO.File]::ReadAllText((Join-Path $macDir $f[0])) -replace "`r`n", "`n"
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($text)
    $e = $dst.CreateEntry($top + $f[0], [System.IO.Compression.CompressionLevel]::Optimal)
    $s = $e.Open(); $s.Write($bytes, 0, $bytes.Length); $s.Dispose()
    $wanted[$top + $f[0]] = $f[1]
  }
  foreach ($se in $srcZip.Entries) {
    if ($se.FullName.EndsWith('/')) { continue }
    $name = $top + 'game/' + $se.FullName
    $e = $dst.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $e.LastWriteTime = $se.LastWriteTime
    $in = $se.Open(); $o = $e.Open(); $in.CopyTo($o); $o.Dispose(); $in.Dispose()
    $mode = $srcModes[$se.FullName]
    if (-not $mode) { $mode = 0x81A4 }
    $wanted[$name] = $mode
  }
} finally {
  $dst.Dispose(); $fs.Dispose(); $srcZip.Dispose()
}

# Windows writes its entries as "made on Windows", which Macs read without permissions.
# Rewrite each central directory entry as a Unix entry with the right mode.
$b = [System.IO.File]::ReadAllBytes($Out)
$fixed = 0
foreach ($e in (Get-ZipModes $b)) {
  if (-not $wanted.ContainsKey($e.Name)) { throw "Unexpected entry $($e.Name)" }
  $mode = [uint32]$wanted[$e.Name]
  $b[$e.Offset + 4] = 20          # version made by: 2.0
  $b[$e.Offset + 5] = 3           # ... on Unix
  $attr = $mode -shl 16
  if ($e.Name.EndsWith('/')) { $attr = $attr -bor 0x10 }
  $ab = [BitConverter]::GetBytes([uint32]$attr)
  [Array]::Copy($ab, 0, $b, $e.Offset + 38, 4)
  $fixed++
}
[System.IO.File]::WriteAllBytes($Out, $b)

# Check the result.
$check = Get-ZipModes ([System.IO.File]::ReadAllBytes($Out))
$exec = @($check | Where-Object { ($_.Mode -band 0x49) -ne 0 -and -not $_.Name.EndsWith('/') })
$launcher = $check | Where-Object { $_.Name -eq ($top + 'Play Bunker Buster.command') }
$binary = $check | Where-Object { $_.Name -eq ($top + 'game/Bunker Buster.app/Contents/MacOS/Bunker Buster') }
if (-not $launcher -or $launcher.Mode -ne 0x81ED) { throw 'Launcher is missing or not executable in the zip.' }
if (-not $binary -or $binary.Mode -ne 0x81ED) { throw 'Game executable is missing or not executable in the zip.' }
"Entries: $($check.Count) (from Godot: $($srcModes.Count)); executables: $($exec.Count); fixed: $fixed"
"PACKAGE OK: $Out ($([Math]::Round((Get-Item $Out).Length / 1MB, 1)) MB)"
