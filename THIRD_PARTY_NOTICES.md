# 第三方组件与许可证

简单小一点（SIMPLE SMALLER）本身以 [AGPL-3.0-only](LICENSE) 授权。本仓库自己的源文件不逐个加 SPDX 文件头，除下文列出的第三方组件外，仓库内全部文件均按根目录 `LICENSE` 授权；仓库名与版本 tag 见下文「源代码」。

应用随包附带下列第三方组件。许可证原文都在 [`licenses/`](licenses/)，打包时拷进应用：

- mac：`简单小一点.app/Contents/Resources/ThirdParty/`（`licenses/common` + `licenses/mac`）
- Windows：安装目录下的 `Assets\ThirdParty\`（`licenses/common` + `licenses/windows`）

应用内「关于 › 开源组件与许可证」里是同一份清单的简表，并能直接打开上面的目录。

## 源代码

- 本应用：<https://github.com/IT-SIMPLE-CN/simple-smaller>。mac 每个对外版本对应 tag `mac-v<版本>`，Windows 对应 `windows-v<版本>`。
- C / C++ 组件全部从源码按构建脚本里固定的版本编译（见 [docs/build.md](docs/build.md)）。GPL / AGPL 组件（FFmpeg、x264、zimg、dav1d、MuPDF、gifski、libimagequant）对应版本的源码归档随 GitHub Releases 一起提供；Windows 版 ffmpeg 的预编译包也在 Releases 里。

## 两端共有

### 图片

| 组件 | 版本 | 许可证 | 来源 | 随包位置 | 许可证全文 |
| --- | --- | --- | --- | --- | --- |
| jpegli | 提交 `031a0077` | BSD-3-Clause（另有专利授权） | <https://github.com/google/jpegli> | 引擎（静态链接） | `jpegli-LICENSE.txt`、`jpegli-PATENTS.txt` |
| Highway（jpegli 子模块） | 提交 `271a9a0e` | Apache-2.0（可选 BSD-3-Clause） | <https://github.com/google/highway> | 引擎 | `jpegli-highway-LICENSE.txt` |
| Little CMS（jpegli 子模块） | 2.17 | MIT | <https://github.com/mm2/Little-CMS> | 引擎 | `jpegli-lcms2-LICENSE.txt` |
| libwebp | 1.6.0 | BSD-3-Clause（另有专利授权） | <https://chromium.googlesource.com/webm/libwebp> | 引擎 | `libwebp-COPYING.txt`、`libwebp-PATENTS.txt` |
| aom | 3.15.2 | BSD-2-Clause + AOM 专利许可 1.0 | <https://aomedia.googlesource.com/aom> | 引擎 | `aom-LICENSE.txt`、`aom-PATENTS.txt` |
| aom 自带：fastfeat | 随 aom | BSD-3-Clause | aom `third_party/fastfeat` | 引擎 | `aom-fastfeat-LICENSE.txt` |
| aom 自带：vector | 随 aom | MIT | aom `third_party/vector` | 引擎 | `aom-vector-LICENSE.txt` |
| aom 自带：x86inc | 随 aom | ISC | aom `third_party/x86inc` | 引擎（x86_64 / x64） | `aom-x86inc-LICENSE.txt` |
| libavif | 1.4.2 | BSD-2-Clause | <https://github.com/AOMediaCodec/libavif> | 引擎 | `libavif-LICENSE.txt` |
| libimagequant（crate `imagequant`） | 4.4.1 | GPL-3.0-or-later | <https://github.com/ImageOptim/libimagequant> | 引擎 | `libimagequant-COPYRIGHT.txt`、GPLv3 全文 `FFmpeg-COPYING.GPLv3.txt` |
| libdeflate（crate `libdeflate-sys`） | 1.26.1 | MIT | <https://github.com/ebiggers/libdeflate> | 引擎 | `libdeflate-COPYING.txt` |
| 其余 Rust crate（oxipng、zopfli、image、fast_image_resize、moxcms 等） | 见 `Cargo.lock` | MIT / Apache-2.0 / BSD-3-Clause / MPL-2.0 / Unicode-3.0 / CC0-1.0 | crates.io | 引擎 | `rust-crates.txt`（各平台一份） |
| Rust 标准库 | 由 `rust-toolchain.toml` 固定 | MIT OR Apache-2.0 | <https://github.com/rust-lang/rust> | 引擎 | `rust-crates.txt` 里的 MIT 原文 |

### 视频

| 组件 | 版本 | 许可证 | 来源 | 随包位置 | 许可证全文 |
| --- | --- | --- | --- | --- | --- |
| FFmpeg（GPL 构建，`--enable-gpl --enable-version3`） | n9.0.2 | GPL-3.0-or-later | <https://git.ffmpeg.org/ffmpeg.git> | 独立程序 ffmpeg / ffprobe | `FFmpeg-LICENSE.md`、`FFmpeg-COPYING.GPLv3.txt` |
| x264 | stable 分支，提交 `b35605ac` | GPL-2.0-or-later | <https://code.videolan.org/videolan/x264> | 编进 ffmpeg | `x264-COPYING.txt` |
| zimg | 3.0.6 | WTFPL | <https://github.com/sekrit-twc/zimg> | 编进 ffmpeg | `zimg-COPYING.txt` |
| gifski（启用 `gifsicle` 特性：内置有损 GIF 压缩，源自 giflossy） | 1.34.0 | AGPL-3.0-or-later | <https://github.com/ImageOptim/gifski> | 引擎 | `gifski-NOTICE.txt`、AGPL 全文 `SimpleSmaller-AGPL-3.0.txt` |

HEVC 只用系统 / 显卡的硬件编码，不随包附带 HEVC 编码器（不含 x265）。

### PDF

| 组件 | 版本 | 许可证 | 来源 | 随包位置 | 许可证全文 |
| --- | --- | --- | --- | --- | --- |
| MuPDF | 1.28.5 | AGPL-3.0-or-later | <https://github.com/ArtifexSoftware/mupdf> | 引擎 | `MuPDF-COPYING.txt` |
| FreeType（MuPDF 子模块） | 2.14.3 | FreeType License（FTL） | MuPDF `thirdparty/freetype` | 引擎 | `MuPDF-freetype-LICENSE.txt`、`MuPDF-freetype-FTL.txt` |
| HarfBuzz（同上） | 13.0.1 | MIT | MuPDF `thirdparty/harfbuzz` | 引擎 | `MuPDF-harfbuzz-COPYING.txt` |
| jbig2dec（同上） | 0.20 | AGPL-3.0-or-later | MuPDF `thirdparty/jbig2dec` | 引擎 | `MuPDF-jbig2dec-LICENSE.txt` |
| Little CMS（lcms2mt，同上） | 2.19 | MIT | MuPDF `thirdparty/lcms2` | 引擎 | `MuPDF-lcms2mt-LICENSE.txt` |
| libjpeg（IJG，同上） | 10 | IJG License | MuPDF `thirdparty/libjpeg` | 引擎 | `MuPDF-libjpeg-README.txt` |
| OpenJPEG（同上） | 2.5.4 | BSD-2-Clause | MuPDF `thirdparty/openjpeg` | 引擎 | `MuPDF-openjpeg-LICENSE.txt` |
| MuJS（同上） | 随 MuPDF | ISC | MuPDF `thirdparty/mujs` | 引擎 | `MuPDF-mujs-COPYING.txt` |
| zlib（同上） | 1.3.2 | Zlib | MuPDF `thirdparty/zlib` | 引擎 | `MuPDF-zlib-LICENSE.txt` |
| Brotli（同上） | 随 MuPDF | MIT | MuPDF `thirdparty/brotli` | 引擎 | `MuPDF-brotli-LICENSE.txt` |
| Gumbo（同上） | 随 MuPDF | Apache-2.0 | MuPDF `thirdparty/gumbo-parser` | 引擎 | `MuPDF-gumbo-COPYING.txt` |
| cmark-gfm（同上） | 0.29.0 | BSD-2-Clause | MuPDF `thirdparty/cmark-gfm` | 引擎 | `MuPDF-cmark-gfm-COPYING.txt` |
| Extract（同上） | 随 MuPDF | AGPL-3.0-or-later | MuPDF `thirdparty/extract` | 引擎 | `MuPDF-extract-COPYING.txt` |
| URW Base 35 字体（Nimbus Sans / Roman / Mono PS、Standard Symbols PS、Dingbats） | 随 MuPDF | OFL-1.1 | MuPDF `resources/fonts/urw` | 引擎（PDF 标准 14 字体） | `MuPDF-URW-fonts-OFL.txt` |

MuPDF 按 `TOFU`、`TOFU_CJK` 编译（不内置 Noto 兜底字体），并关掉断字表（`FZ_ENABLE_HYPHEN=0`）；不编条码（zxing-cpp、zint）与 OCR（Tesseract、Leptonica）。

署名：

- Portions of this software are copyright © 2026 The FreeType Project (https://freetype.org). All rights reserved.
- This software is based in part on the work of the Independent JPEG Group.

### 其他

| 组件 | 版本 | 许可证 | 来源 | 随包位置 | 许可证全文 |
| --- | --- | --- | --- | --- | --- |
| 线性图标（部分改自 Lucide / Feather） | — | ISC / MIT | <https://github.com/lucide-icons/lucide> | 界面 | `Lucide-LICENSE.txt` |

## 仅 mac

| 组件 | 版本 | 许可证 | 来源 | 随包位置 | 许可证全文 |
| --- | --- | --- | --- | --- | --- |
| Sparkle（含 bsdiff / bspatch 4.3、sais-lite、ed25519、SUSignatureVerifier 等） | 2.9.6 | MIT（内含组件为 BSD-2-Clause / MIT / Zlib） | <https://github.com/sparkle-project/Sparkle> | `Contents/Frameworks/Sparkle.framework` | `Sparkle-2.9.6-LICENSE.txt` |
| UniFFI（Rust crate 与生成的 Swift 绑定） | 0.31.2 | MPL-2.0 | <https://github.com/mozilla/uniffi-rs> | 引擎与应用 | `UniFFI-MPL-2.0.txt` |
| Rust 依赖汇总 | 见 `Cargo.lock` | — | — | — | `rust-crates.txt`（mac 依赖树） |

mac 版的引擎以静态库链接进应用，ffmpeg / ffprobe 是 `Contents/Resources/bin/` 下的独立程序。

## 仅 Windows

Windows 版分三个程序：界面 `SimpleSmaller.exe`（WinUI 3）、引擎 `smaller-core.exe`（上文图片、PDF 与 gifski 都编在这里，界面通过进程间通信调用）、`ffmpeg.exe` / `ffprobe.exe`。

| 组件 | 版本 | 许可证 | 来源 | 随包位置 | 许可证全文 |
| --- | --- | --- | --- | --- | --- |
| dav1d | 1.5.4 | BSD-2-Clause | <https://code.videolan.org/videolan/dav1d> | 编进 ffmpeg.exe | `dav1d-COPYING.txt` |
| zlib | 1.3.2 | Zlib | <https://github.com/madler/zlib> | 编进 ffmpeg.exe | `zlib-LICENSE.txt` |
| Windows App SDK（WinUI 3 等） | 2.5.1 | Microsoft Software License Terms – Microsoft Windows App SDK | NuGet `Microsoft.WindowsAppSDK` | 界面（自带运行库） | `WindowsAppSDK-2.5.1-license.txt`、`WindowsAppSDK-2.5.1-NOTICE.txt`、`WindowsAppSDK-WinUI-NOTICE.txt`、`WindowsAppSDK-ML-license.txt` |
| WebView2（随 Windows App SDK） | 1.0.3719.77 | BSD-3-Clause（Microsoft） | NuGet `Microsoft.Web.WebView2` | 界面 | `WebView2-LICENSE.txt`、`WebView2-NOTICE.txt` |
| .NET 运行库（自带） | 10.0.13 | MIT | <https://github.com/dotnet/runtime> | 界面 | `dotnet-LICENSE.txt`、`dotnet-THIRD-PARTY-NOTICES.txt` |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | NuGet `CommunityToolkit.Mvvm` | 界面 | `CommunityToolkit.Mvvm-License.md`、`CommunityToolkit.Mvvm-ThirdPartyNotices.txt` |
| Velopack | 1.2.161 | MIT | <https://github.com/velopack/velopack> | 安装程序与自动更新 | `Velopack-LICENSE.txt` |
| MiSans（小米） | 构建时下载 | MiSans 字体知识产权许可协议 | 小米官方下载地址（见 `scripts/fetch-misans.ps1`） | `Assets\Fonts\` | 构建时一同下载的 `Assets\Fonts\MiSans-License.pdf` |
| Rust 依赖汇总 | 见 `Cargo.lock` | — | — | — | `rust-crates.txt`（Windows 依赖树） |

MiSans 按其协议不进本仓库，由 `scripts/fetch-misans.ps1` 在构建时下载；fork 后再分发须自行遵守小米的协议。

## 名称与标志

「简单小一点」「SIMPLE SMALLER」「IT-SIMPLE」的名称、应用图标与品牌标不随 AGPL-3.0 授权。fork 后对外发布请换用自己的名称、图标与更新源。

## 维护

升级组件或改了 Rust 依赖时，按 [licenses/README.md](licenses/README.md) 更新原文、本文件与两份应用内清单，并跑 `scripts/gen-licenses.sh` 与 `cargo deny check licenses`（白名单见 [deny.toml](deny.toml)）。
