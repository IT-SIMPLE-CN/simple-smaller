# 构建说明

macOS 见下文；Windows 见文末「Windows」一节。

## 工具

- Xcode（含 Swift 6.2 以上）。
- **rustup 官方工具链**，版本由 `rust-toolchain.toml` 固定（含 arm64 与 x86_64 目标）。安装：`curl https://sh.rustup.rs | sh -s -- -y --no-modify-path`，脚本会自己把 `~/.cargo/bin` 放到 PATH 最前面。
- Homebrew：`cmake nasm ninja autoconf automake libtool pkg-config meson`（`meson` 只有交叉编译 Windows 版 ffmpeg 时用到）。
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

## Windows

Windows 版的引擎、编码库、MuPDF 在 Windows 里用 MSVC 编（开发时用 Parallels 的 ARM64 Windows 11，x64 靠交叉编译）；随包的 ffmpeg 在 Mac 上用 llvm-mingw 交叉编译。

### 工具（Windows）

- Visual Studio 2022 生成工具，含 C++ 桌面开发、ARM64 与 x64 的 MSVC（`windows-env.ps1` 用 `vcvarsall.bat` 进环境，CMake / Ninja 用 VS 自带的）。
- rustup 官方工具链（`rust-toolchain.toml` 固定版本），Git for Windows（带 Perl，aom 配置要用），x64 编码库要 NASM。
- .NET SDK 与 Windows App SDK（WinUI 3 应用）。

### 顺序

在 Mac 上（任意时候先跑一次即可，产物在 `.local/ffmpeg/win-<arm64|x64>/bin`）：

```bash
scripts/build-ffmpeg-windows.sh arm64
scripts/build-ffmpeg-windows.sh x64      # 或 all 两套都编
```

在 Windows 里（仓库副本，如 `C:\dev\simple-smaller`），每种架构：

1. `scripts\build-codecs-windows.ps1 -Arch arm64`：jpegli、libwebp、aom、libavif → `.local\codecs\<架构>`。
2. `scripts\build-mupdf-windows.ps1 -Arch arm64`：MuPDF → `.local\mupdf\win-<架构>`。
3. `scripts\fetch-misans.ps1`：下载 MiSans 字体到 `Assets\Fonts`（不进仓库，见脚本说明）。
4. `scripts\build-core-windows.ps1 -Arch arm64`：`smaller_core.dll` + C# 绑定，并把 ffmpeg.exe / ffprobe.exe 放到 `apps\windows\SimpleSmaller\Native\<架构>`。

第 4 步找 ffmpeg 的方式：先看共享文件夹里 Mac 仓库的 `Z:\Development\SIMPLE SMALLER\.local\ffmpeg\win-<架构>\bin`（Parallels 把 Mac 主目录映射为 `Z:`；别的位置用环境变量 `SMALLER_MAC_REPO` 指定），比本地的新就拷到本地 `.local\ffmpeg\win-<架构>\bin`，再从本地拷进 `Native`。共享文件夹不可用时直接用本地已有的那份。

### 踩过的坑（Windows）

- **ffmpeg 不用现成构建**：BtbN 等现成 GPL 构建带 x265（违反「HEVC 只走系统 / 显卡硬件」），单个 exe 90–158 MB。自己交叉编译，组件与 mac 一致，单个 exe ARM64 约 23 MB、x64 约 31 MB（x86 汇编优化多）。
- **zimg 3.0.6 与新版 libc++**：`zimg.cpp` 漏写 `#include <exception>`，新版 libc++ 不再顺带引入，脚本用 `-include exception` 补上。
- **n9.0 的 Media Foundation 编码器要 d3d11va**：`mfenc.c` 无条件用 D3D11 类型，关了 d3d11va 编不过，所以一起开（运行时才加载 `d3d11.dll`）。
- **只依赖系统 DLL**：链接带 `-static`，libc++、libunwind 静态链进去；脚本最后用 `objdump -p` 列出依赖，只应出现 `KERNEL32`、`api-ms-win-crt-*`（UCRT）等系统 DLL。
