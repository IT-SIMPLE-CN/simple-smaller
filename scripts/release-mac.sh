#!/bin/bash
# macOS 正式包：通用二进制 → Developer ID 签名 → Apple 公证 → 装订票据 → Sparkle 更新包与 appcast → DMG。
# 每步校验后才继续；做法与 dotnfilm-bots 的 scripts/release-mac.mjs 一致，见 docs/release.md。
#
#   scripts/release-mac.sh            完整流程
#   scripts/release-mac.sh --app-only 只出签名并公证过的 .app 与更新包，不做 DMG
#
# 签名证书取钥匙串里唯一的 Developer ID Application（可用 APPLE_SIGNING_IDENTITY 指定）；
# 公证用 notarytool 的 keychain profile（默认 dotnfilm-notary，可用 NOTARY_KEYCHAIN_PROFILE 指定）；
# 更新包的 EdDSA 私钥在钥匙串的 simple-smaller 账户下。私钥与凭据都不进命令行参数、不写日志。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP_ONLY=""
[ "${1:-}" = "--app-only" ] && APP_ONLY=1
PROFILE="${NOTARY_KEYCHAIN_PROFILE:-dotnfilm-notary}"
SPARKLE_BIN="$ROOT/.local/sparkle-tools/2.9.6/bin"
SPARKLE_SHA="52bf9e88cdd972fc0c81501377a880e90d47031bd8ca5462488f843e2609e192"
FEED="https://bots.it-simple.cn/downloads/smaller/appcast.xml"
ASSETS="https://itsimple-bot.oss-cn-shenzhen.aliyuncs.com/smaller"
NAME="简单小一点"
MIN_OS="14.0"
step() { printf '\n▸ %s\n' "$1"; }

VERSION="$(tr -d ' \n' < "$ROOT/apps/mac/version.txt")"
BUILD="$(git -C "$ROOT" rev-list --count HEAD)"
[ -z "$(git -C "$ROOT" status --porcelain)" ] || { echo "工作区有未提交的改动：先提交，构建号要对得上提交。"; exit 1; }

step "发布环境预检"
if [ -n "${APPLE_SIGNING_IDENTITY:-}" ]; then
  IDENTITY="$APPLE_SIGNING_IDENTITY"
else
  IDS="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Developer ID Application: [^"]*\)".*/\1/p')"
  [ "$(printf '%s\n' "$IDS" | grep -c .)" = "1" ] || { echo "需要唯一的 Developer ID Application 证书，当前：$IDS"; exit 1; }
  IDENTITY="$IDS"
fi
echo "   证书：$IDENTITY"
xcrun notarytool history --keychain-profile "$PROFILE" --output-format json > /dev/null \
  || { echo "公证 profile $PROFILE 预检失败（见上面 notarytool 的原始错误），还没开始构建。"; exit 1; }
if [ ! -x "$SPARKLE_BIN/sign_update" ]; then
  mkdir -p "$(dirname "$SPARKLE_BIN")"
  curl -fsSL -o "$ROOT/.local/sparkle.tar.xz" https://github.com/sparkle-project/Sparkle/releases/download/2.9.6/Sparkle-2.9.6.tar.xz
  echo "$SPARKLE_SHA  $ROOT/.local/sparkle.tar.xz" | shasum -a 256 -c > /dev/null
  tar -xf "$ROOT/.local/sparkle.tar.xz" -C "$(dirname "$SPARKLE_BIN")"
  rm "$ROOT/.local/sparkle.tar.xz"
fi

OUT="$ROOT/.local/mac-releases/$VERSION-$BUILD"
rm -rf "$OUT"; mkdir -p "$OUT"
APP="$OUT/$NAME.app"

step "构建 $NAME ${VERSION}（${BUILD}），Apple 芯片与 Intel 通用"
"$ROOT/scripts/build-core-mac.sh" release universal > "$ROOT/.local/build-core.log" 2>&1 || { tail -20 "$ROOT/.local/build-core.log"; exit 1; }
"$ROOT/scripts/bundle-mac.sh" release universal > /dev/null
ditto "$ROOT/.local/app/SimpleSmaller.app" "$APP"
lipo -archs "$APP/Contents/MacOS/SimpleSmaller"

# 内层先签外层后签；Sparkle 的 Autoupdate 与 Updater.app 是嵌套可执行体，逐个签，不用 --deep。
step "Developer ID 签名"
SIGN=(codesign --force --options runtime --timestamp --sign "$IDENTITY")
for t in "$APP/Contents/Resources/bin/ffmpeg" "$APP/Contents/Resources/bin/ffprobe" \
         "$APP/Contents/Frameworks/Sparkle.framework/Versions/B/Autoupdate" \
         "$APP/Contents/Frameworks/Sparkle.framework/Versions/B/Updater.app" \
         "$APP/Contents/Frameworks/Sparkle.framework"; do
  "${SIGN[@]}" "$t"
done
# 包里不能再有没签过的 Mach-O。
UNSIGNED=""
while IFS= read -r f; do
  if file "$f" | grep -q "Mach-O" && ! codesign --verify --strict "$f" 2> /dev/null; then UNSIGNED="$UNSIGNED$f"$'\n'; fi
done < <(find "$APP/Contents/Resources" "$APP/Contents/Frameworks" -type f -perm -u+x)
[ -z "$UNSIGNED" ] || { echo "这些可执行文件没签：$UNSIGNED"; exit 1; }
"${SIGN[@]}" "$APP"
codesign --verify --strict --deep "$APP"

notarize() {
  local json id status
  json="$(xcrun notarytool submit "$1" --keychain-profile "$PROFILE" --wait --output-format json)"
  id="$(printf '%s' "$json" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')"
  status="$(printf '%s' "$json" | python3 -c 'import json,sys; print(json.load(sys.stdin)["status"])')"
  xcrun notarytool log "$id" --keychain-profile "$PROFILE" > "$OUT/notary-$(basename "$1").json"
  [ "$status" = "Accepted" ] || { echo "公证未通过（${status}），提交编号 ${id}，日志 $OUT/notary-$(basename "$1").json"; exit 1; }
  echo "   提交编号 ${id}：$status"
}

step "公证 App"
ditto -c -k --keepParent "$APP" "$OUT/notarize.zip"
notarize "$OUT/notarize.zip"
rm "$OUT/notarize.zip"
xcrun stapler staple "$APP" > /dev/null
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose=2 "$APP"

# 更新包在装订票据之后再打：Sparkle 解出来的就是这个 .app，票据跟着走，用户机器不用再联网向 Apple 查。
step "更新包与 appcast"
ZIP="$OUT/SimpleSmaller-$VERSION.zip"
ditto -c -k --keepParent "$APP" "$ZIP"
ENCLOSURE="$("$SPARKLE_BIN/sign_update" --account simple-smaller "$ZIP")"
NOTES_FILE="$ROOT/apps/mac/release-notes/$VERSION.html"
[ -f "$NOTES_FILE" ] || { echo "缺少版本说明 $NOTES_FILE"; exit 1; }
NOTES="$(cat "$NOTES_FILE")"
cat > "$OUT/appcast.xml" <<XML
<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
  <channel>
    <title>$NAME</title>
    <link>$FEED</link>
    <description>$NAME 的 macOS 更新源</description>
    <language>zh-CN</language>
    <item>
      <title>$VERSION</title>
      <pubDate>$(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S +0000')</pubDate>
      <sparkle:version>$BUILD</sparkle:version>
      <sparkle:shortVersionString>$VERSION</sparkle:shortVersionString>
      <sparkle:minimumSystemVersion>$MIN_OS</sparkle:minimumSystemVersion>
      <description><![CDATA[$NOTES]]></description>
      <enclosure url="$ASSETS/$(basename "$ZIP")" $ENCLOSURE type="application/octet-stream" />
    </item>
  </channel>
</rss>
XML
echo "   $(basename "$ZIP") 已签名，appcast 指向 $ASSETS/"

if [ -z "$APP_ONLY" ]; then
  step "制作 DMG"
  DMG="$OUT/SimpleSmaller-$VERSION.dmg"
  STAGE="$OUT/dmg"
  mkdir -p "$STAGE"
  ditto "$APP" "$STAGE/$NAME.app"
  ln -s /Applications "$STAGE/Applications"
  hdiutil create -volname "$NAME" -srcfolder "$STAGE" -ov -format UDZO "$DMG" > /dev/null
  rm -rf "$STAGE"
  codesign --force --timestamp --sign "$IDENTITY" "$DMG"
  step "公证 DMG"
  notarize "$DMG"
  xcrun stapler staple "$DMG" > /dev/null
  xcrun stapler validate "$DMG"
  hdiutil verify "$DMG" > /dev/null
  spctl --assess --type open --context context:primary-signature --verbose=2 "$DMG"
fi

step "校验和"
# --app-only 时没有 DMG：只列实际存在的文件，否则 shasum 报错会让脚本在这里退出。
( cd "$OUT" && ls *.zip *.dmg 2> /dev/null | while IFS= read -r f; do shasum -a 256 "$f"; done > SHA256SUMS; cat SHA256SUMS )
printf '\n✓ %s %s（%s）已签名、公证并装订票据：%s\n' "$NAME" "$VERSION" "$BUILD" "$OUT"
