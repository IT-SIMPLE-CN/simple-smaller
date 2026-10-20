#!/bin/bash
# 把 Windows 构建机（虚拟机）里 scripts\release-windows.ps1 做好的产物发布出去：安装程序与更新包传阿里云 OSS（smaller/win/），
# Velopack 更新源（releases.<频道>.json）放到更新源服务器的 win/ 下。做法同 publish-mac.sh，见 docs/release.md。
# 虚拟机名、虚拟机里的仓库路径、虚拟机里看到的 Mac 主目录写在 .local/private/release.env（SMALLER_VM、SMALLER_VM_REPO、SMALLER_VM_HOME）；
# 在虚拟机里执行命令用本机的 .local/vm.sh（不进仓库）。
#
#   scripts/publish-windows.sh            发布 apps/windows/version.txt 指定的版本
#   scripts/publish-windows.sh --dry-run  只取回产物、核对校验和与分发桶，不上传
#
# 先传包再换更新源，顺序不能反：更新源一换，客户端立刻按它取包。更新源先写 .tmp 再 mv，原子替换。
# OSS 凭据来自 .local/oss.env（600，不进仓库、不进命令行）。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DRY=""
[ "${1:-}" = "--dry-run" ] && DRY=1
. "$ROOT/scripts/release-env.sh"
ENV_FILE="$ROOT/.local/oss.env"
WIN_PREFIX="$PREFIX/win"
WIN_ASSETS="$ASSETS/win"
WIN_REMOTE="$REMOTE/win"
WIN_FEED="${FEED%/appcast.xml}/win"
VM="${SMALLER_VM:?release.env 里没有 SMALLER_VM（Windows 构建虚拟机名）}"
VM_REPO="${SMALLER_VM_REPO:?release.env 里没有 SMALLER_VM_REPO（虚拟机里的仓库路径）}"
VM_HOME="${SMALLER_VM_HOME:?release.env 里没有 SMALLER_VM_HOME（虚拟机里看到的 Mac 主目录，如 Z:）}"
export SMALLER_VM
step() { printf '\n▸ %s\n' "$1"; }

VERSION="$(tr -d ' \n' < "$ROOT/apps/windows/version.txt")"
OUT="$ROOT/.local/win-releases/$VERSION"
[ -f "$ENV_FILE" ] || { echo "缺少 ${ENV_FILE}（OSS 凭据）"; exit 1; }

step "从虚拟机取回 $VERSION 的产物"
rm -rf "$OUT" && mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd -P)"
win_out="$VM_HOME${OUT#$HOME}"; win_out="${win_out//\//\\}"
"$ROOT/.local/vm.sh" "Copy-Item '$VM_REPO\\.local\\win-releases\\$VERSION\\*' '$win_out' -Force; 'copied'" > /dev/null
cd "$OUT"
[ -f SHA256SUMS ] || { echo "虚拟机里没有 $VERSION 的产物，先在虚拟机里跑 scripts\\release-windows.ps1"; exit 1; }
tr -d '\r' < SHA256SUMS > SHA256SUMS.unix && mv SHA256SUMS.unix SHA256SUMS
shasum -a 256 -c SHA256SUMS
FEEDS="$(ls releases.*.json 2> /dev/null || true)"
[ -n "$FEEDS" ] || { echo "产物里没有 releases.*.json"; exit 1; }
PACKAGES="$(ls | grep -v -E '^(releases\..*\.json|SHA256SUMS)$')"
for f in $FEEDS; do
  python3 -I - "$f" "$VERSION" <<'PY'
import json, sys
feed = json.load(open(sys.argv[1], encoding="utf-8-sig"))
versions = [a["Version"] for a in feed["Assets"]]
assert sys.argv[2] in versions, f"{sys.argv[1]} 里没有 {sys.argv[2]}：{versions}"
print(f"   {sys.argv[1]}：" + "、".join(f'{a["Version"]} {a["Type"]} {a["FileName"]}' for a in feed["Assets"]))
PY
done

# 用 OSS 的 V1 签名直接 PUT / DELETE，与 publish-mac.sh 相同。对象单独设为公共读：Velopack 匿名下载。
oss() {
  python3 -I - "$ENV_FILE" "$BUCKET" "$REGION" "$@" <<'PY'
import base64, hmac, hashlib, sys, urllib.request
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
types = {".exe": "application/vnd.microsoft.portable-executable", ".nupkg": "application/octet-stream",
         ".zip": "application/zip", ".json": "application/json"}
ctype = "" if method == "DELETE" else types.get(key[key.rfind("."):], "text/plain; charset=utf-8")
date = formatdate(usegmt=True)
oss_headers = {} if method == "DELETE" else {"x-oss-object-acl": "public-read"}
canon = "".join(f"{k}:{v}\n" for k, v in sorted(oss_headers.items()))
sig = base64.b64encode(hmac.new(sk.encode(), f"{method}\n\n{ctype}\n{date}\n{canon}/{bucket}/{key}".encode(), hashlib.sha1).digest()).decode()
data = open(path, "rb").read() if path else (None if method == "DELETE" else b"")
req = urllib.request.Request(f"https://{bucket}.{region}.aliyuncs.com/{urllib.request.quote(key)}", data=data, method=method)
req.add_header("Date", date)
req.add_header("Authorization", f"OSS {ak}:{sig}")
if ctype: req.add_header("Content-Type", ctype)
for k, v in oss_headers.items(): req.add_header(k, v)
try:
    with urllib.request.urlopen(req, timeout=1800) as r:
        print(f"   {method} {key}：{r.status}")
except urllib.error.HTTPError as e:
    body = e.read().decode(errors="replace")
    code = body.split("<Code>")[1].split("</Code>")[0] if "<Code>" in body else ""
    sys.exit(f"   {method} {key} 失败：HTTP {e.code} {code}")
PY
}

step "分发桶预检"
printf 'ok' > "$ROOT/.local/publish-check"
oss PUT "$WIN_PREFIX/.publish-check" "$ROOT/.local/publish-check"
[ "$(curl -fsS "$WIN_ASSETS/.publish-check")" = "ok" ] || { echo "能写进 ${BUCKET}，但对象匿名取不到"; exit 1; }
oss DELETE "$WIN_PREFIX/.publish-check"
rm "$ROOT/.local/publish-check"
echo "   $BUCKET 可写，写进去的对象匿名取得到"

if [ -n "$DRY" ]; then
  echo; echo "（--dry-run：到此为止，没有上传）"
  for f in $PACKAGES; do echo "   会上传 $f"; done
  exit 0
fi

step "上传安装程序与更新包"
for f in $PACKAGES SHA256SUMS; do oss PUT "$WIN_PREFIX/$f" "$OUT/$f"; done
for f in $PACKAGES; do
  remote="$(curl -fsSI "$WIN_ASSETS/$f" | tr -d '\r' | awk -F': ' 'tolower($1)=="content-length"{print $2}')"
  [ "$remote" = "$(stat -f%z "$f")" ] || { echo "   $f 线上大小 $remote 与本地不符"; exit 1; }
  echo "   $WIN_ASSETS/$f 匿名可取"
done

step "替换更新源"
ssh "$HOST" "sudo mkdir -p $WIN_REMOTE"
for f in $FEEDS; do
  ssh "$HOST" "cat > /tmp/smaller-$f.tmp" < "$f"
  ssh "$HOST" "sudo mv /tmp/smaller-$f.tmp $WIN_REMOTE/$f && sudo chmod 644 $WIN_REMOTE/$f"
  curl -fsS "$WIN_FEED/$f" | grep -q "\"$VERSION\"" || { echo "   线上 $f 里没有 $VERSION"; exit 1; }
  echo "   $WIN_FEED/$f 已是 $VERSION"
done

printf '\n✓ 已发布。首次安装：\n'
for f in $(ls *-Setup.exe); do echo "   $WIN_ASSETS/$f"; done
