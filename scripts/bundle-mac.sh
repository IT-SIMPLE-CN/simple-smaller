#!/bin/bash
# 把 swift build 的产物装成 SimpleSmaller.app（Info.plist、随包 ffmpeg、临时签名）。
# 用法：scripts/bundle-mac.sh [debug|release]，默认 debug。产物在 .local/app/SimpleSmaller.app。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIG="${1:-debug}"
ARCH="$(uname -m)"
cd "$ROOT/apps/mac"
# 按本机 SDK 版本记链接记录，系统才会给 Liquid Glass（见 Package.swift）。
export SMALLER_SDK_VERSION="$(xcrun --show-sdk-version)"
swift build -c "$CONFIG" > "$ROOT/.local/swift-build.log" 2>&1 || { grep -E "error:" "$ROOT/.local/swift-build.log" | head -20; exit 1; }
BIN="$(swift build -c "$CONFIG" --show-bin-path)/SimpleSmaller"

APP="$ROOT/.local/app/SimpleSmaller.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin"
cp "$BIN" "$APP/Contents/MacOS/SimpleSmaller"
cp "$ROOT/.local/ffmpeg/$ARCH/bin/ffmpeg" "$ROOT/.local/ffmpeg/$ARCH/bin/ffprobe" "$APP/Contents/Resources/bin/"
cp "$ROOT/apps/mac/Resources/"* "$APP/Contents/Resources/" 2>/dev/null || true
# 强调色（品牌红）与应用图标编成 Assets.car：系统按 macOS 规则在开关、滑杆、进度、选中处用强调色。
xcrun actool "$ROOT/apps/mac/Assets.xcassets" --compile "$APP/Contents/Resources" --platform macosx \
  --minimum-deployment-target 14.0 --app-icon AppIcon --accent-color AccentColor \
  --output-partial-info-plist "$ROOT/.local/assets-partial.plist" > "$ROOT/.local/actool.log" 2>&1
VERSION="$(grep '^version' "$ROOT/Cargo.toml" | head -1 | cut -d'"' -f2)"
BUILD="$(git -C "$ROOT" rev-list --count HEAD)"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD/" "$ROOT/apps/mac/Info.plist" > "$APP/Contents/Info.plist"
codesign --force --deep --sign - "$APP" > /dev/null 2>&1
echo "$APP"
