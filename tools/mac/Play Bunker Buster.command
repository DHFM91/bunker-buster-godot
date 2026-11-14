#!/bin/bash
# Bunker Buster launcher for macOS.
# Double-click this file to play. The first time, macOS asks you to approve it
# (see "Mac - read me first.txt"). After that it starts the game straight away.

cd "$(dirname "$0")" || exit 1
HERE="$(pwd)"
APP="$HERE/game/Bunker Buster.app"
BIN="$APP/Contents/MacOS/Bunker Buster"
LOG="$HERE/bunker-buster-log.txt"
READY="$HERE/game/.ready"

say() { echo "$*"; echo "$*" >> "$LOG"; }

{
  echo "Bunker Buster launcher log - $(date)"
  sw_vers 2>&1
  echo "Machine: $(uname -m)"
  echo "Folder: $HERE"
  echo
} > "$LOG"

if [ ! -f "$BIN" ]; then
  say "Can't find game/Bunker Buster.app next to this launcher."
  say "Keep the whole Bunker Buster folder together (unzip it again if needed)."
  read -n 1 -s -r -p "Press any key to close."
  exit 1
fi

# Warn if this macOS is older than the game supports.
OSVER="$(sw_vers -productVersion 2>/dev/null)"
OSMAJOR="${OSVER%%.*}"
case "$OSMAJOR" in ''|*[!0-9]*) OSMAJOR="" ;; esac
ARCH="$(uname -m)"
if [ -n "$OSMAJOR" ]; then
  if [ "$ARCH" = "arm64" ] && [ "$OSMAJOR" -lt 13 ]; then
    say "Warning: this Mac runs macOS $OSVER; Bunker Buster needs macOS 13 or newer on Apple Silicon."
  elif [ "$OSMAJOR" -lt 11 ]; then
    say "Warning: this Mac runs macOS $OSVER; Bunker Buster needs macOS 11 or newer."
  fi
fi

if [ ! -f "$READY" ]; then
  say "First run: preparing the game (this takes a few seconds)..."

  # 1. Clear the download quarantine from the whole game folder.
  xattr -dr com.apple.quarantine "$HERE" >> "$LOG" 2>&1

  # 2. Make sure the game itself is executable.
  chmod +x "$BIN" >> "$LOG" 2>&1

  # 3. Check the app's signature; if it doesn't hold up on this Mac, re-sign it here.
  echo "--- signature check" >> "$LOG"
  if codesign --verify --deep --strict "$APP" >> "$LOG" 2>&1; then
    echo "signature OK" >> "$LOG"
  else
    say "Re-signing the game for this Mac..."
    codesign --force --deep -s - "$APP" >> "$LOG" 2>&1
    codesign --verify --deep --strict -v "$APP" >> "$LOG" 2>&1
  fi
  touch "$READY" 2>/dev/null
fi

echo "--- game output" >> "$LOG"
say "Starting Bunker Buster. Leave this window open while you play; closing it quits the game."
"$BIN" "$@" 2>&1 | tee -a "$LOG"
STATUS=${PIPESTATUS[0]}
echo "--- game exited with code $STATUS" >> "$LOG"

if [ "$STATUS" -ne 0 ]; then
  # Run the preparation again next time, in case something changed.
  rm -f "$READY"
  echo
  echo "Bunker Buster stopped with an error (code $STATUS)."
  echo "Please send David the file bunker-buster-log.txt from the Bunker Buster folder."
  read -n 1 -s -r -p "Press any key to close."
fi
exit 0
