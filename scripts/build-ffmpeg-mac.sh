#!/bin/bash
# 按固定版本编译随包的 ffmpeg / ffprobe（GPL 构建：x264 + zimg + VideoToolbox / AudioToolbox）。
# x264 与 ffmpeg 的构建系统处理不了带空格的路径（仓库目录名有空格），所以在
# ~/Library/Caches/simple-smaller 里编译，最后把 ffmpeg / ffprobe 拷到 .local/ffmpeg/<arch>/bin。
# 用法：scripts/build-ffmpeg-mac.sh [arm64|x86_64]，默认本机架构。
set -euo pipefail

FFMPEG_TAG="n9.0.2"
X264_COMMIT="b35605ace3ddf7c1a5d67a2eb553f034aef41d55" # stable 分支
ZIMG_TAG="release-3.0.6"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-$(uname -m)}"
WORK="$HOME/Library/Caches/simple-smaller"
SRC="$WORK/src"
BUILD="$WORK/build/$ARCH"
PREFIX="$WORK/prefix/$ARCH"
OUT="$ROOT/.local/ffmpeg/$ARCH/bin"
mkdir -p "$SRC" "$BUILD" "$PREFIX" "$OUT"
JOBS="$(sysctl -n hw.ncpu)"
export MACOSX_DEPLOYMENT_TARGET=14.0
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
CFLAGS_ARCH="-arch $ARCH -mmacosx-version-min=14.0"

# 源码目录不在、或不是要的那个版本（改了版本号），就删掉重新拉，不悄悄沿用旧源码。
stale() { [ ! -d "$1" ] || [ "$(git -C "$1" describe --tags --exact-match 2> /dev/null)" != "$2" ]; }

# x264
if [ ! -d "$SRC/x264" ]; then
  git clone -q https://code.videolan.org/videolan/x264.git "$SRC/x264"
fi
git -C "$SRC/x264" checkout -q "$X264_COMMIT"
rm -rf "$BUILD/x264" && cp -R "$SRC/x264" "$BUILD/x264"
(
  cd "$BUILD/x264"
  ./configure --prefix="$PREFIX" --enable-static --enable-pic --disable-cli \
    --host="$ARCH-apple-darwin" --extra-cflags="$CFLAGS_ARCH" --extra-ldflags="$CFLAGS_ARCH" > configure.log
  make -j"$JOBS" > build.log && make install > install.log
)
echo "✓ x264"

# zimg（zscale 滤镜，HDR 转 SDR 用）
if stale "$SRC/zimg" "$ZIMG_TAG"; then
  rm -rf "$SRC/zimg"
  git clone -q --depth 1 --branch "$ZIMG_TAG" --recurse-submodules https://github.com/sekrit-twc/zimg "$SRC/zimg"
fi
rm -rf "$BUILD/zimg" && cp -R "$SRC/zimg" "$BUILD/zimg"
(
  cd "$BUILD/zimg"
  ./autogen.sh > autogen.log 2>&1
  # --host 让 configure 按目标架构选优化代码（不写的话按本机，Apple 芯片上会选 NEON）。
  ./configure --prefix="$PREFIX" --enable-static --disable-shared --host="$ARCH-apple-darwin" \
    CFLAGS="$CFLAGS_ARCH" CXXFLAGS="$CFLAGS_ARCH" LDFLAGS="$CFLAGS_ARCH" > configure.log
  make -j"$JOBS" > build.log && make install > install.log
)
echo "✓ zimg"

# ffmpeg：关掉自动探测，只开明确需要的组件，不会误链本机 Homebrew 的库。
# 不要 hls、concat 解复用：这两种格式会按文件里写的路径去读别的文件，打开别人给的文件时不该这样。
if stale "$SRC/ffmpeg" "$FFMPEG_TAG"; then
  rm -rf "$SRC/ffmpeg"
  git clone -q --depth 1 --branch "$FFMPEG_TAG" https://git.ffmpeg.org/ffmpeg.git "$SRC/ffmpeg"
fi
mkdir -p "$BUILD/ffmpeg"
(
  cd "$BUILD/ffmpeg"
  "$SRC/ffmpeg/configure" --prefix="$PREFIX" --arch="$ARCH" \
    --pkg-config-flags=--static \
    --extra-cflags="$CFLAGS_ARCH -I$PREFIX/include" --extra-ldflags="$CFLAGS_ARCH -L$PREFIX/lib" \
    --enable-gpl --enable-version3 --enable-static --disable-shared \
    --disable-autodetect --disable-doc --disable-debug --disable-network --disable-ffplay \
    --disable-demuxer=hls,concat \
    --enable-libx264 --enable-libzimg --enable-zlib \
    --enable-videotoolbox --enable-audiotoolbox \
    > configure.log
  # 编译配置会原样写进程序（-buildconf、数据目录），把本机主目录换成 ~，产物里不留本机路径。
  sed -i '' "s#$HOME#~#g" config.h
  make -j"$JOBS" > build.log && make install > install.log
)
cp "$PREFIX/bin/ffmpeg" "$PREFIX/bin/ffprobe" "$OUT/"
echo "✓ ffmpeg"
"$OUT/ffmpeg" -hide_banner -version | head -1
echo "已装到 $OUT"
