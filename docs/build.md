# 构建说明（macOS）

## 工具

- Xcode（含 Swift 6.2 以上）。
- **rustup 官方工具链**，版本由 `rust-toolchain.toml` 固定（含 arm64 与 x86_64 目标）。安装：`curl https://sh.rustup.rs | sh -s -- -y --no-modify-path`，脚本会自己把 `~/.cargo/bin` 放到 PATH 最前面。
- Homebrew：`cmake nasm ninja autoconf automake libtool pkg-config`。
- 系统自带的 `git`、`python3`（发布脚本用来解析 JSON、给 OSS 请求签名）、`bash` 3.2（脚本不用 bash 4 的写法）。
- 各库的源码会自动拉取；脚本里写死了版本，改了版本号再跑会删掉旧源码重拉。

## 顺序

1. `scripts/build-codecs-mac.sh`：jpegli、libwebp、aom、libavif → `.local/codecs/<架构>`。
2. `scripts/build-mupdf-mac.sh`：MuPDF → `.local/mupdf/<架构>`。
3. `scripts/build-ffmpeg-mac.sh`：x264、zimg、FFmpeg → `.local/ffmpeg/<架构>/bin`（随应用分发的独立程序）。
4. `scripts/build-core-mac.sh`：Rust 引擎静态库 + UniFFI 生成的 Swift 绑定。
5. `scripts/bundle-mac.sh`：`swift build` 后装成 `.local/app/SimpleSmaller.app`（带 ffmpeg、图标、强调色、临时签名）。

### 通用包（Apple 芯片 + Intel）

前三步各再跑一次 `x86_64`，然后：

```bash
scripts/build-codecs-mac.sh x86_64
scripts/build-mupdf-mac.sh x86_64
scripts/build-ffmpeg-mac.sh x86_64
scripts/build-core-mac.sh release universal   # 两种架构的引擎合成一个静态库
scripts/bundle-mac.sh release universal       # swift build --arch arm64 --arch x86_64，ffmpeg 用 lipo 合并
```

`lipo -archs .local/app/SimpleSmaller.app/Contents/MacOS/SimpleSmaller` 应显示 `x86_64 arm64`。

引擎测试：`PATH="$HOME/.cargo/bin:$PATH" CFLAGS=-mmacosx-version-min=14.0 CXXFLAGS=-mmacosx-version-min=14.0 cargo test -p smaller-core --release`。找不到 ffmpeg 时视频测试会跳过；加 `SMALLER_REQUIRE_FFMPEG=1` 则必须找到（发版前用这个跑）。

改了应用图标：编辑 `apps/mac/AppIcon.svg`，再跑 `scripts/render-icon.sh` 生成各尺寸。

## 踩过的坑

- **不要用 Homebrew 的 Rust**：它的标准库按 macOS 26 编，链进应用后在 macOS 14 / 15 上打不开。
- **不要设 `MACOSX_DEPLOYMENT_TARGET`**：官方 rustc 会把它算进目标身份，设成默认 11.0 以外的任何值，编出的 proc-macro 库 dyld 加载不了（报 `can't find crate` 或 `mis-aligned LINKEDIT string pool`）。Rust 代码默认按 11.0 编，本来就低于应用要求的 14。cc 编的 C / C++ 改用 `CFLAGS` / `CXXFLAGS` 传 `-mmacosx-version-min=14.0`，rustc 不读这两个变量。换过设置后要 `cargo clean`，旧的坏库会留在 `target/`。
- **带空格的路径**：仓库目录名有空格，x264、FFmpeg、MuPDF 的构建系统处理不了，这三个在 `~/Library/Caches/simple-smaller` 里编译后再拷回 `.local`。
- **链接 C++**：jpegli 是 C++ 写的，Swift 链接引擎静态库时要带 `-lc++`（已写在 `apps/mac/Package.swift`）。
- **交叉编译要显式指定目标 CPU**：aom（`-DAOM_TARGET_CPU`）和 zimg（`--host`）默认按本机 CPU 选优化代码，在 Apple 芯片上编 Intel 版会选到 NEON 而报错。
- **链接记录里的 SDK 版本**：SwiftPM 会把它写成最低系统版本 14.0，系统据此把应用当旧应用、不给 Liquid Glass。`Package.swift` 用 `-platform_version` 写明实际 SDK 版本（打包脚本传 `SMALLER_SDK_VERSION`）。
- **验证最低版本**：`vtool -show-build apps/mac/.build/debug/SimpleSmaller` 应显示 `minos 14.0`，`swift build` 的输出里不应有 `was built for newer 'macOS' version`。
