# 第三方许可证全文

随应用分发的第三方组件的许可证原文，打包时拷进应用：

| 目录 | 内容 | 装到 |
| --- | --- | --- |
| `common/` | 两端都有的：本应用的 AGPL-3.0、FFmpeg、x264、zimg、MuPDF 及其第三方库与字体、编码库（jpegli、libwebp、libavif、aom）、libimagequant、gifski、libdeflate、Lucide 图标 | 两端 |
| `mac/` | Sparkle、UniFFI（MPL-2.0）、mac 版的 Rust 依赖汇总 `rust-crates.txt` | mac：`Contents/Resources/ThirdParty/`（`scripts/bundle-mac.sh`） |
| `windows/` | dav1d、zlib、Windows App SDK、WebView2、.NET 运行库、CommunityToolkit.Mvvm、Velopack、Windows 版的 Rust 依赖汇总 `rust-crates.txt` | Windows：`Assets\ThirdParty\`（`SimpleSmaller.csproj` 的 Content 项） |
| `about/` | `cargo-about` 的配置与模板，不进应用 | — |

组件、版本、许可证与来源的总表见仓库根目录的 [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)；应用内「开源组件与许可证」的清单是 `apps/mac/Resources/Licenses.txt` 与 `apps/windows/SimpleSmaller/Assets/Licenses.txt`。

## 怎么更新

- 文本一律从上游源码或官方发布的原文拷贝，不手写：C / C++ 库取构建脚本固定版本的源码里的 `LICENSE` / `COPYING`，NuGet 包取包里的 `license.txt` / `NOTICE.txt`。
- 升级了某个库：换掉对应文件，同步改 `THIRD_PARTY_NOTICES.md` 与两份 `Licenses.txt` 里的版本号。
- Rust 依赖变了（`Cargo.lock` 有变化）：跑 `scripts/gen-licenses.sh` 重新生成两份 `rust-crates.txt`，并跑 `cargo deny check licenses`（白名单在根目录 `deny.toml`）。
- 新加的文件名在 `common/` 与对应平台目录之间不能重名（打包时拍平到同一个目录）。
