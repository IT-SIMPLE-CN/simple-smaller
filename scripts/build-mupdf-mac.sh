#!/bin/bash
# 按固定版本编译 MuPDF 静态库（AGPL），装到 .local/mupdf/<arch>。
# MuPDF 的 Makefile 同样处理不了带空格的路径，在 ~/Library/Caches/simple-smaller 里编译后拷回。
# 用法：scripts/build-mupdf-mac.sh [arm64|x86_64]，默认本机架构。
set -euo pipefail

MUPDF_TAG="1.28.5"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-$(uname -m)}"
WORK="$HOME/Library/Caches/simple-smaller"
SRC="$WORK/src/mupdf"
OUT="$ROOT/.local/mupdf/$ARCH"
JOBS="$(sysctl -n hw.ncpu)"

# 源码目录不在、或不是要的那个版本（改了版本号），就删掉重新拉，不悄悄沿用旧源码。
stale() { [ ! -d "$1" ] || [ "$(git -C "$1" describe --tags --exact-match 2> /dev/null)" != "$2" ]; }

if stale "$SRC" "$MUPDF_TAG"; then
  rm -rf "$SRC"
  git clone -q --depth 1 --branch "$MUPDF_TAG" --recurse-submodules --shallow-submodules \
    https://github.com/ArtifexSoftware/mupdf "$SRC"
fi

# TOFU：不内置 Noto 兜底字体（PDF 绝大多数自带嵌入字体），否则静态库要大几十 MB。
# FZ_ENABLE_HYPHEN=0：不带断字表（hyph-all.zip，只给 HTML 排版用，各语言词表许可证不一），PDF 处理用不上。
# 编译选项不进 make 的依赖判断，每次从干净目录编，免得沿用旧选项编出的目标文件。
rm -rf "$SRC/build/release-$ARCH"
make -C "$SRC" -j"$JOBS" build=release OUT="build/release-$ARCH" \
  HAVE_X11=no HAVE_GLUT=no HAVE_CURL=no HAVE_LEPTONICA=no HAVE_TESSERACT=no \
  XCFLAGS="-arch $ARCH -mmacosx-version-min=14.0 -DTOFU -DTOFU_CJK -DFZ_ENABLE_HYPHEN=0" \
  XLDFLAGS="-arch $ARCH -mmacosx-version-min=14.0" \
  libs > "$WORK/mupdf-$ARCH.build.log"

mkdir -p "$OUT/lib"
rm -rf "$OUT/include"
cp "$SRC/build/release-$ARCH/libmupdf.a" "$SRC/build/release-$ARCH/libmupdf-third.a" "$OUT/lib/"
cp -R "$SRC/include" "$OUT/include"
echo "✓ mupdf $MUPDF_TAG"
ls -lh "$OUT/lib"
