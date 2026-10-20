#!/bin/bash
# 编译 Rust 引擎并生成 Swift 绑定，供 apps/mac 链接。
# 用法：scripts/build-core-mac.sh [debug|release] [universal]，默认 release、本机架构。
# 带 universal 时同时编 arm64 与 x86_64，合成一个通用静态库（发布用；需先编好两种架构的编码库与 MuPDF）。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROFILE="${1:-release}"
UNIVERSAL="${2:-}"
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

# 编译产物里不留本机路径（panic 信息、调试信息里的源码位置）：仓库、cargo 注册表、主目录都映射成短名。
# 仓库路径带空格，RUSTFLAGS 按空格拆分会拆坏，用 CARGO_ENCODED_RUSTFLAGS（参数之间用 0x1f 分隔）。
# 同一路径匹配多条时后写的优先，所以主目录写在最前面。
CARGO_HOME_DIR="${CARGO_HOME:-$HOME/.cargo}"
SEP=$'\x1f'
export CARGO_ENCODED_RUSTFLAGS="--remap-path-prefix=$HOME=~${SEP}--remap-path-prefix=$CARGO_HOME_DIR/registry/src=/cargo${SEP}--remap-path-prefix=$CARGO_HOME_DIR/git/checkouts=/cargo-git${SEP}--remap-path-prefix=$ROOT=/smaller"
unset RUSTFLAGS

FLAG=""
[ "$PROFILE" = "release" ] && FLAG="--release"
if [ "$UNIVERSAL" = "universal" ]; then
  for T in aarch64-apple-darwin x86_64-apple-darwin; do
    cargo build $FLAG -p smaller-core --target "$T"
  done
  LIB_DIR="$ROOT/target/aarch64-apple-darwin/$PROFILE"
else
  cargo build $FLAG -p smaller-core
  LIB_DIR="$ROOT/target/$PROFILE"
fi

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
if [ "$UNIVERSAL" = "universal" ]; then
  lipo -create "$ROOT/target/aarch64-apple-darwin/$PROFILE/libsmaller_core.a" "$ROOT/target/x86_64-apple-darwin/$PROFILE/libsmaller_core.a" \
    -output "$ROOT/.local/lib/libsmaller_core.a"
else
  cp "$LIB_DIR/libsmaller_core.a" "$ROOT/.local/lib/libsmaller_core.a"
fi
echo "引擎已就绪：$PROFILE ${UNIVERSAL:-$(uname -m)}"
