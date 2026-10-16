# 构建说明（macOS）

## 工具

- Xcode（含 Swift 6.2 以上）。
- **rustup 官方工具链**，版本由 `rust-toolchain.toml` 固定（含 arm64 与 x86_64 目标）。安装：`curl https://sh.rustup.rs | sh -s -- -y --no-modify-path`，脚本会自己把 `~/.cargo/bin` 放到 PATH 最前面。
- Homebrew：`cmake nasm ninja autoconf automake libtool pkg-config`。

## 顺序

1. `scripts/build-codecs-mac.sh`：jpegli、libwebp、aom、libavif → `.local/codecs/<架构>`。
2. `scripts/build-mupdf-mac.sh`：MuPDF → `.local/mupdf/<架构>`。
3. `scripts/build-ffmpeg-mac.sh`：x264、zimg、FFmpeg → `.local/ffmpeg/<架构>/bin`（随应用分发的独立程序）。
4. `scripts/build-core-mac.sh`：Rust 引擎静态库 + UniFFI 生成的 Swift 绑定。
5. `cd apps/mac && swift build`。

引擎测试：`PATH="$HOME/.cargo/bin:$PATH" CFLAGS=-mmacosx-version-min=14.0 CXXFLAGS=-mmacosx-version-min=14.0 cargo test -p smaller-core --release`。

## 踩过的坑

- **不要用 Homebrew 的 Rust**：它的标准库按 macOS 26 编，链进应用后在 macOS 14 / 15 上打不开。
- **不要设 `MACOSX_DEPLOYMENT_TARGET`**：官方 rustc 会把它算进目标身份，设成默认 11.0 以外的任何值，编出的 proc-macro 库 dyld 加载不了（报 `can't find crate` 或 `mis-aligned LINKEDIT string pool`）。Rust 代码默认按 11.0 编，本来就低于应用要求的 14。cc 编的 C / C++ 改用 `CFLAGS` / `CXXFLAGS` 传 `-mmacosx-version-min=14.0`，rustc 不读这两个变量。换过设置后要 `cargo clean`，旧的坏库会留在 `target/`。
- **带空格的路径**：仓库目录名有空格，x264、FFmpeg、MuPDF 的构建系统处理不了，这三个在 `~/Library/Caches/simple-smaller` 里编译后再拷回 `.local`。
- **链接 C++**：jpegli 是 C++ 写的，Swift 链接引擎静态库时要带 `-lc++`（已写在 `apps/mac/Package.swift`）。
- **验证最低版本**：`vtool -show-build apps/mac/.build/debug/SimpleSmaller` 应显示 `minos 14.0`，`swift build` 的输出里不应有 `was built for newer 'macOS' version`。
