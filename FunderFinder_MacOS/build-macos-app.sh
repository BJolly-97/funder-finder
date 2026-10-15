#!/usr/bin/env bash
# Builds "Funder Finder.app" for this Mac (Apple Silicon or Intel) into ./dist.
# Run from Terminal on a Mac with the .NET 8 SDK installed:   ./build-macos-app.sh
set -euo pipefail
cd "$(dirname "$0")"

case "$(uname -m)" in
  arm64)  RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "Unsupported Mac architecture: $(uname -m)"; exit 1 ;;
esac

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' FunderFinder.Mac.csproj)
APP="dist/Funder Finder.app"

echo "Building Funder Finder $VERSION for $RID..."
# Self-contained: the finished app doesn't need .NET installed to run.
dotnet publish FunderFinder.Mac.csproj -c Release -r "$RID" --self-contained true -o "build/$RID"

echo "Assembling $APP..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "build/$RID/." "$APP/Contents/MacOS/"
iconutil -c icns Packaging/FunderFinder.iconset -o "$APP/Contents/Resources/FunderFinder.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>               <string>Funder Finder</string>
  <key>CFBundleDisplayName</key>        <string>Funder Finder</string>
  <key>CFBundleIdentifier</key>         <string>io.github.bjolly97.funderfinder</string>
  <key>CFBundleExecutable</key>         <string>FunderFinder</string>
  <key>CFBundleIconFile</key>           <string>FunderFinder</string>
  <key>CFBundlePackageType</key>        <string>APPL</string>
  <key>CFBundleShortVersionString</key> <string>$VERSION</string>
  <key>CFBundleVersion</key>            <string>$VERSION</string>
  <key>LSMinimumSystemVersion</key>     <string>11.0</string>
  <key>NSHighResolutionCapable</key>    <true/>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/FunderFinder"
# Ad-hoc signature: enough for an app you built yourself to run on Apple Silicon.
codesign --force --deep --sign - "$APP" >/dev/null

echo
echo "Done: $APP"
echo "Drag it into Applications, then open it from there."
