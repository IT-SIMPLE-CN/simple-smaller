#!/bin/bash
# 编译 Rust 引擎并生成 Swift 绑定，供 apps/mac 链接。
# 用法：scripts/build-core-mac.sh [debug|release]，默认 release。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROFILE="${1:-release}"
cd "$ROOT"

# 用 rustup 的官方工具链（见 rust-toolchain.toml），不用 Homebrew 的 Rust：
# 后者的标准库按 macOS 26 编，应用在 macOS 14 / 15 上打不开。
export PATH="$HOME/.cargo/bin:$PATH"
if ! command -v rustup > /dev/null; then
  echo "需要 rustup：curl https://sh.rustup.rs | sh -s -- -y --no-modify-path" >&2
  exit 1
fi
# 不要设 MACOSX_DEPLOYMENT_TARGET：官方 rustc 会把它算进目标身份，设成默认值 11.0 以外的
# 任何版本，编译器都会拒绝加载 proc-macro（报「can't find crate」）。默认按 11.0 编，
# C 外壳由 cc 跟随同一默认值，都低于应用要求的 14，本来就兼容。
unset MACOSX_DEPLOYMENT_TARGET
# cc 编的 C / C++（外壳、libdeflate 等）不跟随上面的默认值，会按当前 SDK 编；
# 用 CFLAGS / CXXFLAGS 单独指定最低 14（rustc 不读这两个变量）。
export CFLAGS="-mmacosx-version-min=14.0"
export CXXFLAGS="-mmacosx-version-min=14.0"

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
