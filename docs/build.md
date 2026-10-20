# 构建说明

macOS 见下文；Windows 见文末「Windows」一节。目录结构与两端分工见 [architecture.md](architecture.md)。

所有编解码库都从源码按固定版本编译，中间产物放在仓库下的 `.local/`（不进 git）。

## macOS

### 需要什么

- **系统与 Xcode**：Xcode 26 或更高（含 Swift 6.2 与 macOS 26 SDK，Liquid Glass 要用），对应的 macOS 版本见 Xcode 的要求。编出的应用最低支持 macOS 14。Apple 芯片与 Intel 机器都能构建。
- **rustup 官方工具链**：版本由 `rust-toolchain.toml` 固定（含 arm64 与 x86_64 目标），第一次构建时 rustup 会自动装上。安装 rustup：

  ```bash
  curl https://sh.rustup.rs | sh -s -- -y --no-modify-path
  ```

  脚本会自己把 `~/.cargo/bin` 放到 PATH 最前面。**不要用 Homebrew 的 Rust**（见下文「踩过的坑」）。
- **Homebrew 工具**：

  ```bash
  brew install cmake nasm ninja autoconf automake libtool pkg-config meson
  ```

  `meson` 只有交叉编译 Windows 版 ffmpeg 时用到。
- 系统自带的 `git`、`python3`、`bash` 3.2（脚本不用 bash 4 的写法）。
- 各库的源码由脚本自动拉取，版本写死在脚本里；改了版本号再跑会删掉旧源码重拉。

**首次构建**要从源码编 FFmpeg、aom、MuPDF 和 Rust 依赖，视机器要几十分钟。磁盘至少留 8 GB：本机架构约 5 GB，再编 Intel 版做通用包还要多几 GB。之后改 Swift 或 Rust 代码只重编改动的部分。

### 顺序

1. `scripts/build-codecs-mac.sh`：jpegli、libwebp、aom、libavif → `.local/codecs/<架构>`。
2. `scripts/build-mupdf-mac.sh`：MuPDF → `.local/mupdf/<架构>`。
3. `scripts/build-ffmpeg-mac.sh`：x264、zimg、FFmpeg → `.local/ffmpeg/<架构>/bin`（随应用分发的独立程序）。
4. `scripts/build-core-mac.sh`：Rust 引擎静态库 + UniFFI 生成的 Swift 绑定。
5. `scripts/bundle-mac.sh`：`swift build` 后装成 `.local/app/SimpleSmaller.app`（带 ffmpeg、图标、强调色、临时签名）。

前三步只在第一次或升级库版本时跑。之后改了引擎跑第 4、5 步，只改了界面跑第 5 步。

### 运行开发包

```bash
scripts/bundle-mac.sh            # 默认 debug、本机架构
open .local/app/SimpleSmaller.app
```

开发包是临时签名，不检查更新。访达圆环要在「系统设置 › 隐私与安全性 › 辅助功能」里给它授权；重新打包后签名会变，可能要重新授权。

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

### 测试

引擎测试：

```bash
PATH="$HOME/.cargo/bin:$PATH" CFLAGS=-mmacosx-version-min=14.0 CXXFLAGS=-mmacosx-version-min=14.0 cargo test -p smaller-core --release
```

找不到 ffmpeg 时视频测试会跳过；加 `SMALLER_REQUIRE_FFMPEG=1` 则必须找到（发版前用这个跑）。

改了应用图标：编辑 `apps/mac/AppIcon.svg`，再跑 `scripts/render-icon.sh` 生成各尺寸。

### 踩过的坑

- **不要用 Homebrew 的 Rust**：它的标准库按 macOS 26 编，链进应用后在 macOS 14 / 15 上打不开。
- **不要设 `MACOSX_DEPLOYMENT_TARGET`**：官方 rustc 会把它算进目标身份，设成默认 11.0 以外的任何值，编出的 proc-macro 库 dyld 加载不了（报 `can't find crate` 或 `mis-aligned LINKEDIT string pool`）。Rust 代码默认按 11.0 编，本来就低于应用要求的 14。cc 编的 C / C++ 改用 `CFLAGS` / `CXXFLAGS` 传 `-mmacosx-version-min=14.0`，rustc 不读这两个变量。换过设置后要 `cargo clean`，旧的坏库会留在 `target/`。
- **带空格的路径**：x264、FFmpeg、MuPDF 的构建系统处理不了带空格的路径，这三个在 `~/Library/Caches/simple-smaller` 里编译后再拷回 `.local`，所以仓库放在带空格的目录里也能编。
- **链接 C++**：jpegli 是 C++ 写的，Swift 链接引擎静态库时要带 `-lc++`（已写在 `apps/mac/Package.swift`）。
- **交叉编译要显式指定目标 CPU**：aom（`-DAOM_TARGET_CPU`）和 zimg（`--host`）默认按本机 CPU 选优化代码，在 Apple 芯片上编 Intel 版会选到 NEON 而报错。
- **链接记录里的 SDK 版本**：SwiftPM 会把它写成最低系统版本 14.0，系统据此把应用当旧应用、不给 Liquid Glass。`Package.swift` 用 `-platform_version` 写明实际 SDK 版本（打包脚本传 `SMALLER_SDK_VERSION`）。
- **验证最低版本**：`vtool -show-build apps/mac/.build/debug/SimpleSmaller` 应显示 `minos 14.0`，`swift build` 的输出里不应有 `was built for newer 'macOS' version`。

## Windows

Windows 版的引擎、编码库、MuPDF 在 Windows 里用 MSVC 编，x64 与 ARM64 两种机器都可以，也可以在一种机器上交叉编另一种架构。随包的 ffmpeg 目前只能在 macOS 上交叉编译（见下文「ffmpeg」）。

### 需要什么（Windows）

- **Windows 10 / 11**，x64 或 ARM64。
- **仓库路径不要带空格**（如 `C:\src\simple-smaller`）：MSVC 侧部分库的构建系统处理不了带空格的路径。
- **Visual Studio 2022**（Community 或 Build Tools 均可），工作负载选「使用 C++ 的桌面开发」，单个组件里勾上：
  - MSVC v143 生成工具：**x64/x86** 与 **ARM64/ARM64EC** 两个都要。`scripts/windows-env.ps1` 用带 ARM64 组件的那份 VS 进环境，所以只编 x64 也得装 ARM64 组件。
  - Windows 11 SDK（10.0.22621 或更新）。
  - CMake 与 Ninja 用 VS 自带的（「用于 Windows 的 C++ CMake 工具」）。
  - 想在 VS 里打开、调试应用，再加「WinUI 应用程序开发」工作负载。
- **.NET 10 SDK**。Windows App SDK、Velopack 等由 NuGet 按 `SimpleSmaller.csproj` 里写的版本自动还原，不用单独装。
- **rustup 官方工具链**（`rustup-init.exe`），版本由 `rust-toolchain.toml` 固定，第一次构建时自动装上 ARM64 与 x64 目标。
- **Git for Windows**（带 Perl，aom 配置要用）。
- **NASM**：编 x64 的编码库要用，装好后放进 PATH。

### ffmpeg

随包的 `ffmpeg.exe` / `ffprobe.exe` 是自编的最小 GPL 构建（不含 x265）。构建脚本 `scripts/build-ffmpeg-windows.sh` 用 macOS 版的 llvm-mingw 交叉编译，目前只能在 macOS 上跑（需要上文的 Homebrew 工具）：

```bash
scripts/build-ffmpeg-windows.sh arm64
scripts/build-ffmpeg-windows.sh x64      # 或 all 两套都编
```

产物在 `.local/ffmpeg/win-<arm64|x64>/bin`，把它拷到 Windows 那份仓库的同一位置。没有 Mac 的话，用 Releases 里附的 ffmpeg 预编译包，解压到同一位置即可（见 Releases）。

如果在虚拟机里构建、ffmpeg 编在宿主机的仓库里，可以把环境变量 `SMALLER_MAC_REPO` 设成共享文件夹里那份仓库的路径，第 4 步会在宿主机那份更新时自动拷过来。

### 顺序（Windows）

在 PowerShell 里，每种架构（`-Arch arm64` 或 `-Arch x64`，默认本机架构）：

1. `scripts\build-codecs-windows.ps1 -Arch arm64`：jpegli、libwebp、aom、libavif → `.local\codecs\win-<架构>`。
2. `scripts\build-mupdf-windows.ps1 -Arch arm64`：MuPDF → `.local\mupdf\win-<架构>`。
3. `scripts\fetch-misans.ps1`：下载 MiSans 字体到 `apps\windows\SimpleSmaller\Assets\Fonts`（按其许可协议不进仓库，见脚本说明）。
4. `scripts\build-core-windows.ps1 -Arch arm64`：编 Rust 引擎、生成 C# 绑定，并和 ffmpeg.exe / ffprobe.exe 一起放到 `apps\windows\SimpleSmaller\Native\<架构>`。

前三步只在第一次或升级库版本时跑；改了引擎重跑第 4 步。

### 构建与运行应用

命令行（ARM64；x64 把 `ARM64` / `win-arm64` 换成 `x64` / `win-x64`）：

```powershell
dotnet build apps\windows\SimpleSmaller\SimpleSmaller.csproj -c Debug -p:Platform=ARM64 -r win-arm64
```

产物在 `apps\windows\SimpleSmaller\bin\ARM64\Debug\` 下（按目标框架与运行时分子目录），运行其中的 `SimpleSmaller.exe`。

也可以用 Visual Studio 打开 `apps\windows\SimpleSmaller\SimpleSmaller.csproj`，平台选 ARM64 或 x64，直接运行调试。开发构建不检查更新。

### 踩过的坑（Windows）

- **ffmpeg 不用现成构建**：常见的现成 GPL 构建带 x265（违反「HEVC 只走系统 / 显卡硬件」），体积也大得多。自己交叉编译，组件与 mac 一致，单个 exe ARM64 约 23 MB、x64 约 31 MB（x86 汇编优化多）。
- **zimg 3.0.6 与新版 libc++**：`zimg.cpp` 漏写 `#include <exception>`，新版 libc++ 不再顺带引入，脚本用 `-include exception` 补上。
- **n9.0 的 Media Foundation 编码器要 d3d11va**：`mfenc.c` 无条件用 D3D11 类型，关了 d3d11va 编不过，所以一起开（运行时才加载 `d3d11.dll`）。
- **只依赖系统 DLL**：链接带 `-static`，libc++、libunwind 静态链进去；脚本最后用 `objdump -p` 列出依赖，只应出现 `KERNEL32`、`api-ms-win-crt-*`（UCRT）等系统 DLL。
- 界面层的坑见 [architecture.md](architecture.md) 的「Windows 的坑」。
