#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
VERSION="1.1.0-mac-preview.3"
OUTPUT="$PWD/../dist/mac"
APP="$OUTPUT/SideTodo.app"
mkdir -p "$OUTPUT" "$APP/Contents/MacOS" "$APP/Contents/Resources" .build/universal
swift build -c release --arch arm64 --arch x86_64
BIN_DIR="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"
cp "$BIN_DIR/SideTodo" "$APP/Contents/MacOS/SideTodo"
cp Info.plist "$APP/Contents/Info.plist"
swift tools/Icon.swift .build/SideTodo.iconset
iconutil -c icns .build/SideTodo.iconset -o "$APP/Contents/Resources/SideTodo.icns"
plutil -lint "$APP/Contents/Info.plist"
lipo "$APP/Contents/MacOS/SideTodo" -verify_arch arm64 x86_64
# Ad-hoc signing is necessary for the ARM binary. This is NOT Developer ID signing/notarization.
codesign --force --sign - --timestamp=none "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
"$APP/Contents/MacOS/SideTodo" --self-test
if [[ "${1:-}" == "--ui-smoke" ]]; then
    "$APP/Contents/MacOS/SideTodo" --ui-smoke
fi
cp ../docs/MAC-TESTING.ko.md "$OUTPUT/START-HERE.ko.md"
cp README.md "$OUTPUT/BUILD-INFO.md"
mkdir -p .build/friend-package
ditto "$APP" .build/friend-package/SideTodo.app
cp "$OUTPUT/START-HERE.ko.md" "$OUTPUT/BUILD-INFO.md" .build/friend-package/
ditto -c -k --sequesterRsrc .build/friend-package "$OUTPUT/SideTodo-$VERSION-universal.zip"
(cd "$OUTPUT" && shasum -a 256 "SideTodo-$VERSION-universal.zip" > "SideTodo-$VERSION-universal.sha256")
echo "Ready: $OUTPUT"
