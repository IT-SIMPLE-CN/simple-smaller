# 发版

正式包由维护者构建、签名并发布。签名、公证与分发用到的证书、账号和服务器地址都在维护者本机配置，不在仓库里；这里只写对外可见的规则。

- mac：`scripts/release-mac.sh` 出通用二进制（Apple 芯片 + Intel），Developer ID 签名、Apple 公证、装订票据，再做 Sparkle 更新包与 DMG；`scripts/publish-mac.sh` 发布。
- Windows：`scripts\release-windows.ps1` 两种架构各打一个 Velopack 安装程序与更新包；`scripts/publish-windows.sh` 发布。
- 两个发布脚本都先传安装包、再换更新源，顺序不能反；都支持 `--dry-run` 只做核对。

**fork 发布修改版**：请换用自己的应用名称、图标、签名身份、更新源与分发地址（`scripts/release-env.sh`、mac 的 `Info.plist`、Windows 的 `Services/Updates.cs`），并生成自己的更新签名密钥，不要指向本项目的更新源。

## 版本号

两端版本号各自走。

- **mac**：对外版本号写在 `apps/mac/version.txt`（`CFBundleShortVersionString`）。构建号是 main 的提交数（`CFBundleVersion`），只增不减，Sparkle 按构建号判断新旧。发版时工作区要干净，构建号才对得上提交。
- **Windows**：对外版本号写在 `apps/windows/version.txt`，csproj 与 Velopack 都读它。更新频道按架构分：`win-x64`、`win-arm64`，装好的应用只看自己的频道。

## 更新说明

- mac 写在 `apps/mac/release-notes/<版本>.html`，Windows 写在 `apps/windows/release-notes/<版本>.md`，会出现在应用的「发现新版本」询问里。
- 只写用户能感觉到的变化，不写实现细节，不写宣传语。

## 应用里的更新

- **mac**（Sparkle）：启动后每 6 小时在后台检查一次。发现新版本先问用户，同意了才下载安装。更新包用 EdDSA 签名，公钥写在 `apps/mac/Info.plist` 的 `SUPublicEDKey`。
- **Windows**（Velopack）：启动 1 分钟后检查一次，之后每 6 小时一次；发现新版先问，同意后下载，等这一批处理完再重启安装。安装到 `%LOCALAPPDATA%\SimpleSmaller`，不需要管理员权限；只建开始菜单快捷方式，不建桌面快捷方式。卸载时顺带注销资源管理器右键菜单。
- 两端都可以在「设置」里关掉自动检查，菜单里有「检查更新…」。
- 开发构建（没装过的）不检查更新。验证 Windows 更新流程要用测试构建（见 `release-windows.ps1` 的测试开关）：打两个版本，静默装旧版（`Setup.exe --silent`），让测试构建指向新版产物目录后启动，约 1 分钟弹出询问。
- 崩溃日志只写在本机（Windows：`%LOCALAPPDATA%\SimpleSmaller\crash.log`），不上传。

## 回滚

Sparkle 与 Velopack 都不降级。新版有问题时：

1. 发一个版本号（mac 是构建号）更高的修复版。
2. mac 把坏的那条从更新源里删掉。更新源最多保留 12 条历史，Sparkle 总是挑当前系统装得了的最新一条，不会逐版本爬升。

## GitHub Releases

上线之后，同一版本再发到仓库的 GitHub Releases，方便下载和留档。应用内自动更新不读这里。

- 两端分开发：tag 用 `mac-v<版本>`、`windows-v<版本>`，打在构建那一版的提交上（mac 的构建号 = `git rev-list --count <提交>`）。
- mac 传 `SimpleSmaller-<版本>.dmg` 与它的 `SHA256SUMS`；Windows 传两个安装程序，文件名带上版本号（`SimpleSmaller-<版本>-win-x64-Setup.exe`、`…-win-arm64-Setup.exe`），按新名字重算 `SHA256SUMS`。更新包（zip / nupkg）不传。
- 说明照 `release-notes/` 写「这一版」，再加「安装」：系统要求、Windows 未签名时 SmartScreen 怎么放行、`SHA256SUMS` 用法。
- 传完用 `gh release view <tag> --json assets` 核对每个文件的 sha256 和本地一致。
- GPL / AGPL 组件的源码随发布提供：FFmpeg、x264、zimg、dav1d、zlib、MuPDF（含子模块）的源码归档（从构建脚本固定版本的 checkout 用 `git archive` 打），gifski、imagequant 的 `.crate` 原包（sha256 与 `Cargo.lock` 的 checksum 一致）；Windows 版再附两种架构的 ffmpeg 预编译包（`ffmpeg.exe`、`ffprobe.exe` 与许可证）。这些库的版本没变时，新版本的说明里链接到上一次附带它们的 Release 即可。
- 发版前确认许可证清单是新的：Rust 依赖变了跑 `scripts/gen-licenses.sh` 与 `cargo deny check licenses`，库升级了同步 `licenses/`、`THIRD_PARTY_NOTICES.md` 与两份 `Licenses.txt`（见 [licenses/README.md](../licenses/README.md)）。

```bash
git tag -a mac-v0.9.7 <提交> -m "简单小一点 mac 0.9.7（构建 168）" && git push origin mac-v0.9.7
gh release create mac-v0.9.7 --title "简单小一点 mac 0.9.7" --notes-file notes.md SimpleSmaller-0.9.7.dmg SHA256SUMS
```
