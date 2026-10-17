#!/bin/bash
# 从 apps/mac/AppIcon.svg（设计稿 15 · Logo：品牌红圆角方块 + 白色 IT-SIMPLE 标）渲染图标集 16…1024 px。
# 改图标只改 SVG，再跑这个脚本。
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/.local/icon"
swiftc -O "$ROOT/scripts/render-icon.swift" -o "$ROOT/.local/icon/render"
"$ROOT/.local/icon/render" "$ROOT/apps/mac/AppIcon.svg" "$ROOT/apps/mac/Assets.xcassets/AppIcon.appiconset"
