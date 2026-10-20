#!/bin/bash
# 把 scripts/release-mac.sh 做好的产物发布出去：安装包传阿里云 OSS，appcast 换到更新源服务器上。
# 地址见 scripts/release-env.sh，服务器与目录在 .local/private/release.env，见 docs/release.md。
#
#   scripts/publish-mac.sh            发布 apps/mac/version.txt 指定版本的最新一次产物
#   scripts/publish-mac.sh --dry-run  只核对产物、分发桶与线上 appcast，不上传
#
# 先传安装包再换 appcast，顺序不能反：appcast 一换，客户端立刻按它取包。appcast 先写 .tmp 再 mv，原子替换。
# OSS 凭据来自 .local/oss.env（600，不进仓库、不进命令行）。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DRY=""
[ "${1:-}" = "--dry-run" ] && DRY=1
. "$ROOT/scripts/release-env.sh"
ENV_FILE="$ROOT/.local/oss.env"
step() { printf '\n▸ %s\n' "$1"; }

VERSION="$(tr -d ' \n' < "$ROOT/apps/mac/version.txt")"
# 同一版本可能打过几次：按构建号（目录名最后一段）取最新的一次，按数值比，99 < 100。
OUT="$(ls -d "$ROOT/.local/mac-releases/$VERSION"-* 2> /dev/null | while IFS= read -r d; do echo "${d##*-} $d"; done | sort -n | tail -1 | cut -d' ' -f2-)"
[ -n "$OUT" ] || { echo "没有 $VERSION 的发布产物，先跑 scripts/release-mac.sh"; exit 1; }
[ -f "$ENV_FILE" ] || { echo "缺少 ${ENV_FILE}（OSS 凭据）"; exit 1; }
cd "$OUT"
FILES="$(ls *.zip *.dmg SHA256SUMS 2> /dev/null || true)"
[ -f appcast.xml ] || { echo "产物里没有 appcast.xml"; exit 1; }
grep -q "enclosure url=\"$ASSETS/" appcast.xml || { echo "appcast 的包地址不在 $ASSETS 下"; exit 1; }

step "发布 $(basename "$OUT")"
shasum -a 256 -c SHA256SUMS
for f in $FILES; do echo "   $f"; done

# 用 OSS 的 V1 签名直接 PUT / DELETE，不依赖额外的命令行工具。对象单独设为公共读：Sparkle 匿名下载。
oss() {
  python3 -I - "$ENV_FILE" "$BUCKET" "$REGION" "$@" <<'PY'
import base64, hmac, hashlib, mimetypes, sys, urllib.request
from email.utils import formatdate
env_file, bucket, region, method, key = sys.argv[1:6]
path = sys.argv[6] if len(sys.argv) > 6 else None
env = {}
for line in open(env_file, encoding="utf-8"):
    line = line.strip()
    if line and not line.startswith("#") and "=" in line:
        k, v = line.split("=", 1)
        env[k.strip()] = v.strip().strip('"')
ak, sk = env["OBJECT_STORE_ACCESS_KEY_ID"], env["OBJECT_STORE_SECRET_ACCESS_KEY"]
types = {".zip": "application/zip", ".dmg": "application/x-apple-diskimage", ".xml": "application/xml"}
ctype = "" if method == "DELETE" else types.get(key[key.rfind("."):], "text/plain; charset=utf-8")
date = formatdate(usegmt=True)
oss_headers = {} if method == "DELETE" else {"x-oss-object-acl": "public-read"}
canon = "".join(f"{k}:{v}\n" for k, v in sorted(oss_headers.items()))
sig = base64.b64encode(hmac.new(sk.encode(), f"{method}\n\n{ctype}\n{date}\n{canon}/{bucket}/{key}".encode(), hashlib.sha1).digest()).decode()
data = open(path, "rb").read() if path else (None if method == "DELETE" else b"")
req = urllib.request.Request(f"https://{bucket}.{region}.aliyuncs.com/{key}", data=data, method=method)
req.add_header("Date", date)
req.add_header("Authorization", f"OSS {ak}:{sig}")
if ctype: req.add_header("Content-Type", ctype)
for k, v in oss_headers.items(): req.add_header(k, v)
try:
    with urllib.request.urlopen(req, timeout=600) as r:
        print(f"   {method} {key}：{r.status}")
except urllib.error.HTTPError as e:
    body = e.read().decode(errors="replace")
    code = body.split("<Code>")[1].split("</Code>")[0] if "<Code>" in body else ""
    sys.exit(f"   {method} {key} 失败：HTTP {e.code} {code}")
PY
}

step "分发桶预检"
printf 'ok' > "$ROOT/.local/publish-check"
oss PUT "$PREFIX/.publish-check" "$ROOT/.local/publish-check"
[ "$(curl -fsS "$ASSETS/.publish-check")" = "ok" ] || { echo "能写进 ${BUCKET}，但对象匿名取不到"; exit 1; }
oss DELETE "$PREFIX/.publish-check"
rm "$ROOT/.local/publish-check"
echo "   $BUCKET 可写，写进去的对象匿名取得到"

step "合并线上 appcast"
curl -fsS "$FEED" > "$ROOT/.local/appcast.live.xml" 2> /dev/null || : > "$ROOT/.local/appcast.live.xml"
python3 -I - appcast.xml "$ROOT/.local/appcast.live.xml" appcast.published.xml <<'PY'
import re, sys
fresh, live, out = (open(sys.argv[1]).read(), open(sys.argv[2]).read(), sys.argv[3])
ver = lambda item: (re.search(r"<sparkle:version>([^<]+)</sparkle:version>", item) or [None, ""])[1].strip()
new = re.search(r"<item>[\s\S]*?</item>", fresh).group(0)
history = [i for i in re.findall(r"<item>[\s\S]*?</item>", live) if ver(i) and ver(i) != ver(new) and "<enclosure " in i]
history.sort(key=lambda i: int(ver(i)) if ver(i).isdigit() else 0, reverse=True)
items = "\n".join("    " + i.strip() for i in [new] + history[:11])
head = fresh[:fresh.index("<item>")].rstrip() + "\n"
tail = "\n" + fresh[fresh.rindex("</item>") + len("</item>"):].lstrip()
open(out, "w").write(head + items + tail)
print("   版本（构建号）：" + "、".join(ver(i) for i in [new] + history[:11]))
PY

if [ -n "$DRY" ]; then
  echo; echo "（--dry-run：到此为止，没有上传）"
  exit 0
fi

step "上传安装包"
for f in $FILES; do oss PUT "$PREFIX/$f" "$OUT/$f"; done
for f in $FILES; do
  remote="$(curl -fsSI "$ASSETS/$f" | tr -d '\r' | awk -F': ' 'tolower($1)=="content-length"{print $2}')"
  [ "$remote" = "$(stat -f%z "$f")" ] || { echo "   $f 线上大小 $remote 与本地不符"; exit 1; }
  echo "   $ASSETS/$f 匿名可取"
done

step "替换 appcast"
ssh "$HOST" "cat > /tmp/smaller-appcast.xml.tmp" < appcast.published.xml
ssh "$HOST" "mkdir -p $REMOTE && mv /tmp/smaller-appcast.xml.tmp $REMOTE/appcast.xml && chmod 644 $REMOTE/appcast.xml"
BUILD_NOW="$(sed -n 's/.*<sparkle:version>\([^<]*\)<.*/\1/p' appcast.xml | head -1)"
curl -fsS "$FEED" | grep -q "<sparkle:version>$BUILD_NOW</sparkle:version>" || { echo "   线上 appcast 里没有 $BUILD_NOW"; exit 1; }
echo "   $FEED 已是 ${VERSION}（${BUILD_NOW}）"

DMG="$(ls *.dmg 2> /dev/null | head -1)"
printf '\n✓ 已发布。首次安装：%s\n' "$ASSETS/$DMG"
