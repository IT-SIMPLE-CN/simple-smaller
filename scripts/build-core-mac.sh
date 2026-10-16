#!/bin/bash
# 编译 Rust 引擎并生成 Swift 绑定，供 apps/mac 链接。
# 用法：scripts/build-core-mac.sh [debug|release]，默认 release。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROFILE="${1:-release}"
cd "$ROOT"

if [ "$PROFILE" = "release" ]; then
  cargo build --release -p smaller-core
else
  cargo build -p smaller-core
fi
LIB_DIR="$ROOT/target/$PROFILE"

OUT="$ROOT/.local/bindings"
rm -rf "$OUT"
mkdir -p "$OUT"
cargo run -q -p uniffi-bindgen -- generate \
  --library "$LIB_DIR/libsmaller_core.dylib" \
  --language swift \
  --out-dir "$OUT"

MAC="$ROOT/apps/mac/Sources"
mkdir -p "$MAC/SmallerCore" "$MAC/smaller_coreFFI"
cp "$OUT/smaller_core.swift" "$MAC/SmallerCore/smaller_core.swift"
cp "$OUT/smaller_coreFFI.h" "$MAC/smaller_coreFFI/smaller_coreFFI.h"
cat > "$MAC/smaller_coreFFI/module.modulemap" <<'EOF'
module smaller_coreFFI {
    header "smaller_coreFFI.h"
    export *
}
EOF

# SwiftPM 只认一个固定位置的静态库，按 profile 拷过去，避免链接器优先挑到同名 dylib。
mkdir -p "$ROOT/.local/lib"
cp "$LIB_DIR/libsmaller_core.a" "$ROOT/.local/lib/libsmaller_core.a"
echo "引擎已就绪：$PROFILE"
