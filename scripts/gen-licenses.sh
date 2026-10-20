#!/bin/bash
# 按 Cargo.lock 重新生成两端随包的 Rust 依赖许可证汇总：
#   licenses/mac/rust-crates.txt、licenses/windows/rust-crates.txt
# 改了 Rust 依赖（Cargo.lock 变了）后跑一次，连同结果一起提交。需要 cargo-about：
#   cargo install --locked cargo-about --features cli
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$HOME/.cargo/bin:$PATH"
cd "$ROOT"
command -v cargo-about > /dev/null || { echo "需要 cargo-about：cargo install --locked cargo-about --features cli" >&2; exit 1; }
# mac 直接链接引擎库（core）；Windows 随包的是引擎宿主程序 smaller-core.exe（core-host，含 core 与 JSON 通信用的库）。
for p in mac windows; do
  manifest=core/Cargo.toml
  [ "$p" = windows ] && manifest=core-host/Cargo.toml
  cargo about generate --locked -c "licenses/about/$p.toml" -m "$manifest" \
    licenses/about/crates.hbs -o "licenses/$p/rust-crates.txt"
  echo "✓ licenses/$p/rust-crates.txt"
done
