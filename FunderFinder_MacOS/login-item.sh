#!/usr/bin/env bash
# Start Funder Finder quietly when you log in (minimised to the Dock, scans at 9:00).
#   ./login-item.sh install     — set it up (app must be in /Applications)
#   ./login-item.sh remove      — stop it starting at login
# Uses a per-user LaunchAgent, because macOS's own Login Items can't pass the --background flag.
set -euo pipefail

LABEL="io.github.bjolly97.funderfinder"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
BINARY="/Applications/Funder Finder.app/Contents/MacOS/FunderFinder"

case "${1:-}" in
  install)
    [ -x "$BINARY" ] || { echo "Couldn't find $BINARY — build the app and move it to Applications first."; exit 1; }
    mkdir -p "$HOME/Library/LaunchAgents"
    cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>            <string>$LABEL</string>
  <key>ProgramArguments</key> <array><string>$BINARY</string><string>--background</string></array>
  <key>RunAtLoad</key>        <true/>
</dict>
</plist>
PLIST
    launchctl unload "$PLIST" 2>/dev/null || true
    launchctl load "$PLIST"
    echo "Funder Finder will now start quietly when you log in."
    ;;
  remove)
    launchctl unload "$PLIST" 2>/dev/null || true
    rm -f "$PLIST"
    echo "Funder Finder will no longer start at login."
    ;;
  *)
    echo "Usage: $0 install | remove"; exit 1 ;;
esac
