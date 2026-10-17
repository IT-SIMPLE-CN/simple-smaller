#!/bin/bash
# 在 Mac 上交叉编译 Windows 版随包的 ffmpeg.exe / ffprobe.exe（GPL 构建）。
# 组件与 mac 版一致：x264 + zimg + zlib，另加 Media Foundation（h264_mf / hevc_mf，系统与显卡硬件编码）
# 和 dav1d（AV1 软件解码；ffmpeg 自带的 av1 解码器只走硬件）。不带 x265 与其他编码库：HEVC 只走系统 / 显卡硬件。
# 工具链用 llvm-mingw（固定版本与 SHA-256），全部静态链接：产物只依赖 Windows 自带的 UCRT 与系统 DLL。
# 构建系统处理不了带空格的路径（仓库目录名有空格），所以在 ~/Library/Caches/simple-smaller 里编译，
# 最后把 ffmpeg.exe / ffprobe.exe 拷到 .local/ffmpeg/win-<arch>/bin。
# 需要 Homebrew 的 nasm（x64 的 x264 / dav1d 汇编）、meson、ninja、pkg-config。
# 用法：scripts/build-ffmpeg-windows.sh [arm64|x64]，默认 arm64；all 两套都编。
set -euo pipefail

FFMPEG_TAG="n9.0.2"
X264_COMMIT="b35605ace3ddf7c1a5d67a2eb553f034aef41d55" # stable 分支，与 mac 相同
ZIMG_TAG="release-3.0.6"
DAV1D_TAG="1.5.4"
ZLIB_TAG="v1.3.2"
LLVM_MINGW="llvm-mingw-20260922-ucrt-macos-universal"
LLVM_MINGW_URL="https://github.com/mstorsjo/llvm-mingw/releases/download/20260922/$LLVM_MINGW.tar.xz"
LLVM_MINGW_SHA256="52e5f5a7b131021d0c39a37a38fa380a1da7885cd04bd61afd0cd4ecfb8bc1f3"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-arm64}"
if [ "$ARCH" = "all" ]; then
  "$0" arm64 && "$0" x64
  exit
fi
case "$ARCH" in
  arm64) TRIPLE="aarch64-w64-mingw32"; FF_ARCH="aarch64"; CPU_FAMILY="aarch64" ;;
  x64) TRIPLE="x86_64-w64-mingw32"; FF_ARCH="x86_64"; CPU_FAMILY="x86_64" ;;
  *) echo "架构只能是 arm64 / x64 / all" >&2; exit 1 ;;
esac

WORK="$HOME/Library/Caches/simple-smaller"
SRC="$WORK/src"
BUILD="$WORK/build/win-$ARCH"
PREFIX="$WORK/prefix/win-$ARCH"
OUT="$ROOT/.local/ffmpeg/win-$ARCH/bin"
mkdir -p "$SRC" "$BUILD" "$PREFIX" "$OUT" "$WORK/downloads" "$WORK/toolchains"
JOBS="$(sysctl -n hw.ncpu)"

# 工具链：校验和不对就重新下载。
TC="$WORK/toolchains/$LLVM_MINGW"
if [ ! -x "$TC/bin/$TRIPLE-clang" ]; then
  tarball="$WORK/downloads/$LLVM_MINGW.tar.xz"
  if [ ! -f "$tarball" ] || [ "$(shasum -a 256 "$tarball" | cut -d' ' -f1)" != "$LLVM_MINGW_SHA256" ]; then
    curl -fsSL -o "$tarball" "$LLVM_MINGW_URL"
    got="$(shasum -a 256 "$tarball" | cut -d' ' -f1)"
    [ "$got" = "$LLVM_MINGW_SHA256" ] || { rm -f "$tarball"; echo "llvm-mingw 校验和不对：$got" >&2; exit 1; }
  fi
  rm -rf "$TC" && tar -xJf "$tarball" -C "$WORK/toolchains"
fi
export PATH="$TC/bin:$PATH"
CC="$TRIPLE-clang"
CXX="$TRIPLE-clang++"
# 只认本前缀里的 .pc，不会误用本机 Homebrew 的库。
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"
unset PKG_CONFIG_PATH

# 源码目录不在、或不是要的那个版本（改了版本号），就删掉重新拉，不悄悄沿用旧源码。
stale() { [ ! -d "$1" ] || [ "$(git -C "$1" describe --tags --exact-match 2> /dev/null)" != "$2" ]; }
fetch() { # 目录名 标签 地址
  if stale "$SRC/$1" "$2"; then
    rm -rf "$SRC/$1"
    git clone -q --depth 1 --branch "$2" "$3" "$SRC/$1"
  fi
}

# zlib（mac 用系统自带的，Windows 没有，编成静态库）
fetch zlib "$ZLIB_TAG" https://github.com/madler/zlib.git
rm -rf "$BUILD/zlib" && cp -R "$SRC/zlib" "$BUILD/zlib"
(
  cd "$BUILD/zlib"
  make -f win32/Makefile.gcc -j"$JOBS" CC="$CC" AR="$TRIPLE-ar" RC="$TRIPLE-windres" STRIP="$TRIPLE-strip" \
    CFLAGS="-O2" libz.a > build.log
  mkdir -p "$PREFIX/include" "$PREFIX/lib"
  cp zlib.h zconf.h "$PREFIX/include/" && cp libz.a "$PREFIX/lib/"
)
echo "✓ zlib"

# x264
if [ ! -d "$SRC/x264" ]; then
  git clone -q https://code.videolan.org/videolan/x264.git "$SRC/x264"
fi
git -C "$SRC/x264" checkout -q "$X264_COMMIT"
rm -rf "$BUILD/x264" && cp -R "$SRC/x264" "$BUILD/x264"
(
  cd "$BUILD/x264"
  CC="$CC" ./configure --prefix="$PREFIX" --host="$TRIPLE" --cross-prefix="$TRIPLE-" \
    --enable-static --disable-cli > configure.log
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
  # 新版 libc++ 不再顺带引入 <exception>，zimg 3.0.6 漏写了这个头文件，强制带上。
  ./configure --prefix="$PREFIX" --enable-static --disable-shared --host="$TRIPLE" \
    CC="$CC" CXX="$CXX" CXXFLAGS="-O2 -include exception" > configure.log
  make -j"$JOBS" > build.log && make install > install.log
)
echo "✓ zimg"

# dav1d（AV1 软件解码）
fetch dav1d "$DAV1D_TAG" https://code.videolan.org/videolan/dav1d.git
rm -rf "$BUILD/dav1d" && mkdir -p "$BUILD/dav1d"
cat > "$BUILD/dav1d/cross.ini" << EOF
[binaries]
c = '$CC'
cpp = '$CXX'
ar = '$TRIPLE-ar'
strip = '$TRIPLE-strip'
windres = '$TRIPLE-windres'
nasm = 'nasm'

[host_machine]
system = 'windows'
cpu_family = '$CPU_FAMILY'
cpu = '$CPU_FAMILY'
endian = 'little'
EOF
(
  cd "$BUILD/dav1d"
  meson setup build "$SRC/dav1d" --cross-file cross.ini --prefix="$PREFIX" --libdir=lib \
    --buildtype=release --default-library=static -Denable_tools=false -Denable_tests=false > configure.log
  ninja -C build > build.log && ninja -C build install > install.log
)
echo "✓ dav1d"

# ffmpeg：关掉自动探测，只开明确需要的组件。-static 让 libc++、libunwind、winpthread 都静态链进去。
# n9.0 的 mfenc.c 无条件用到 D3D11 的类型，不开 d3d11va 编不过；它只在运行时按需加载 d3d11.dll。
fetch ffmpeg "$FFMPEG_TAG" https://git.ffmpeg.org/ffmpeg.git
rm -rf "$BUILD/ffmpeg" && mkdir -p "$BUILD/ffmpeg"
(
  cd "$BUILD/ffmpeg"
  "$SRC/ffmpeg/configure" --prefix="$PREFIX" --enable-cross-compile --target-os=mingw32 --arch="$FF_ARCH" \
    --cross-prefix="$TRIPLE-" --cc="$CC" --cxx="$CXX" --pkg-config=pkg-config --pkg-config-flags=--static \
    --extra-cflags="-I$PREFIX/include" --extra-ldflags="-L$PREFIX/lib -static" --extra-libs="-lc++" \
    --enable-gpl --enable-version3 --enable-static --disable-shared \
    --disable-autodetect --disable-doc --disable-debug --disable-network --disable-ffplay \
    --enable-w32threads --enable-zlib --enable-libx264 --enable-libzimg --enable-libdav1d \
    --enable-mediafoundation --enable-d3d11va \
    > configure.log
  make -j"$JOBS" > build.log && make install > install.log
)
cp "$PREFIX/bin/ffmpeg.exe" "$PREFIX/bin/ffprobe.exe" "$OUT/"
"$TRIPLE-strip" "$OUT/ffmpeg.exe" "$OUT/ffprobe.exe"
echo "✓ ffmpeg"
# 只应依赖系统 DLL（KERNEL32、api-ms-win-crt-*、mfplat 等），不应出现 libc++ / libwinpthread。
"$TRIPLE-objdump" -p "$OUT/ffmpeg.exe" | grep "DLL Name" | sort -u
ls -lh "$OUT"
echo "已装到 $OUT"
