#!/bin/bash
# 按固定版本从源码编译图片编码库（静态库），装到 .local/codecs/<arch>。
# 用法：scripts/build-codecs-mac.sh [arm64|x86_64]，默认本机架构。
# 版本更新时只改下面几行，并在 docs/codecs.md 记录原因。
set -euo pipefail

LIBWEBP_TAG="v1.6.0"
AOM_TAG="v3.15.2"
LIBAVIF_TAG="v1.4.2"
JPEGLI_COMMIT="031a0077f5799a6041004267fc12b956c1f52a20" # 上游没有发版，钉提交

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-$(uname -m)}"
SRC="$ROOT/.local/src"
BUILD="$ROOT/.local/build/$ARCH"
PREFIX="$ROOT/.local/codecs/$ARCH"
mkdir -p "$SRC" "$BUILD" "$PREFIX"

COMMON=(
  -G Ninja
  -DCMAKE_BUILD_TYPE=Release
  -DBUILD_SHARED_LIBS=OFF
  -DCMAKE_OSX_ARCHITECTURES="$ARCH"
  -DCMAKE_OSX_DEPLOYMENT_TARGET=14.0
  -DCMAKE_INSTALL_PREFIX="$PREFIX"
  -DCMAKE_PREFIX_PATH="$PREFIX"
  -DCMAKE_POSITION_INDEPENDENT_CODE=ON
)

fetch_tag() { # 名字 仓库 标签
  if [ ! -d "$SRC/$1" ]; then
    git clone -q --depth 1 --branch "$3" "$2" "$SRC/$1"
  fi
}

build() { # 名字 [cmake 参数...]
  local name="$1"; shift
  cmake -S "$SRC/$name" -B "$BUILD/$name" "${COMMON[@]}" "$@" > "$BUILD/$name.configure.log"
  cmake --build "$BUILD/$name" > "$BUILD/$name.build.log"
  cmake --install "$BUILD/$name" > "$BUILD/$name.install.log"
  echo "✓ $name"
}

# libwebp
fetch_tag libwebp https://chromium.googlesource.com/webm/libwebp "$LIBWEBP_TAG"
build libwebp \
  -DWEBP_BUILD_ANIM_UTILS=OFF -DWEBP_BUILD_CWEBP=OFF -DWEBP_BUILD_DWEBP=OFF \
  -DWEBP_BUILD_GIF2WEBP=OFF -DWEBP_BUILD_IMG2WEBP=OFF -DWEBP_BUILD_VWEBP=OFF \
  -DWEBP_BUILD_WEBPINFO=OFF -DWEBP_BUILD_WEBPMUX=OFF -DWEBP_BUILD_EXTRAS=OFF

# aom（AV1 编解码，供 libavif 用）
fetch_tag aom https://aomedia.googlesource.com/aom "$AOM_TAG"
build aom \
  -DENABLE_DOCS=0 -DENABLE_EXAMPLES=0 -DENABLE_TESTDATA=0 -DENABLE_TESTS=0 -DENABLE_TOOLS=0

# libavif
fetch_tag libavif https://github.com/AOMediaCodec/libavif "$LIBAVIF_TAG"
build libavif \
  -DAVIF_CODEC_AOM=SYSTEM -DAVIF_BUILD_APPS=OFF -DAVIF_BUILD_TESTS=OFF \
  -DAVIF_LIBYUV=OFF -DAVIF_LIBSHARPYUV=OFF -DAVIF_JPEG=OFF -DAVIF_ZLIBPNG=OFF -DAVIF_LIBXML2=OFF

# jpegli（libjpeg62 接口）
if [ ! -d "$SRC/jpegli" ]; then
  git clone -q --recurse-submodules --shallow-submodules https://github.com/google/jpegli "$SRC/jpegli"
fi
git -C "$SRC/jpegli" checkout -q "$JPEGLI_COMMIT"
build jpegli \
  -DBUILD_TESTING=OFF -DJPEGLI_ENABLE_TOOLS=OFF -DJPEGLI_ENABLE_DEVTOOLS=OFF \
  -DJPEGLI_ENABLE_BENCHMARK=OFF -DJPEGLI_ENABLE_DOXYGEN=OFF -DJPEGLI_ENABLE_MANPAGES=OFF \
  -DJPEGLI_ENABLE_JNI=OFF -DJPEGLI_ENABLE_SJPEG=OFF -DJPEGLI_ENABLE_OPENEXR=OFF \
  -DJPEGLI_ENABLE_FUZZERS=OFF -DJPEGLI_INSTALL_JPEGLI_LIBJPEG=ON -DJPEGLI_STATIC=ON
# macOS 上 jpegli 不生成 libjpeg 兼容库，直接用它自己的 jpegli_* 接口：
# jpegli-static 默认不在 all 里，单独编，连同头文件与依赖一起装。
cmake --build "$BUILD/jpegli" --target jpegli-static > "$BUILD/jpegli.static.log"
cp "$BUILD/jpegli/lib/libjpegli-static.a" "$PREFIX/lib/libjpegli.a"
cp "$BUILD/jpegli/third_party/liblcms2.a" "$PREFIX/lib/liblcms2.a" 2>/dev/null || true
for h in common.h encode.h decode.h types.h; do
  cp "$SRC/jpegli/lib/jpegli/$h" "$PREFIX/include/jpegli/$h"
done
echo "✓ jpegli-static"

echo "编码库已装到 $PREFIX"
