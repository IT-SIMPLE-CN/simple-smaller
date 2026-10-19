# macOS 发布与自动更新

做法与 dotnfilm-bots（`docs/implementation/macos-release.md`）一致：Developer ID 签名、Apple 公证、装订票据，Sparkle 自动更新；安装包放阿里云 OSS，更新源放自己的域名。

## 版本号

- 对外版本号写在 `apps/mac/version.txt`（`CFBundleShortVersionString`）。
- 构建号是 main 的提交数（`CFBundleVersion`），只增不减。Sparkle 按构建号判断新旧。
- 发版前写好 `apps/mac/release-notes/<版本>.html`，它会出现在更新弹窗里。只写用户能感觉到的变化。

## 发布机一次性准备

- **证书**：钥匙串里有且只有一个「Developer ID Application」证书（IT-SIMPLE 账号）。
- **公证凭据**：用 `xcrun notarytool store-credentials dotnfilm-notary --apple-id <账号> --team-id 9XR55S32UG` 存进钥匙串，过程中会让你输 App 专用密码。和 dotnfilm-bots 共用同一个 profile。
- **更新签名私钥**：在登录钥匙串的 `simple-smaller` 账户下，见下文「签名密钥」。
- **OSS 凭据**：`.local/oss.env`（600），里面要有 `OBJECT_STORE_ACCESS_KEY_ID` 和 `OBJECT_STORE_SECRET_ACCESS_KEY`。
- **服务器**：`ssh itsimple-sg` 能免密登录，并且能免密 `sudo`（替换 appcast 要用）。换机器时用 `DEPLOY_HOST` 指定。
- **通用二进制**：编解码库、MuPDF、ffmpeg 的 `x86_64` 版要先编好（见 `docs/build.md` 的「通用包」）。
- 固定的地址和名字在 `scripts/release-env.sh`，发布和上传两个脚本共用。

## 发版

```bash
scripts/release-mac.sh          # 通用二进制 → 签名 → 公证 → 装订 → 更新包与 appcast → DMG → 公证 DMG
scripts/publish-mac.sh --dry-run
scripts/publish-mac.sh          # 先传安装包，再原子替换 appcast
```

- 工作区要干净，发布脚本读取的提交数就是这次的构建号。
- 产物在 `.local/mac-releases/<版本>-<构建号>/`。

### release-mac.sh 做了什么

- **签名**：钥匙串里唯一的 Developer ID Application，可用 `APPLE_SIGNING_IDENTITY` 指定。
  - 由内到外逐个签：`ffmpeg`、`ffprobe`、Sparkle 的 `Autoupdate` 和 `Updater.app`、`Sparkle.framework`，最后签整个应用。
  - 签名前会检查包里是否还有没签过的可执行文件。
  - 不用 `--deep` 代替逐层签名。
- **公证**：用 notarytool 的 keychain profile，默认 `dotnfilm-notary`（与 bots 同一个 Apple 开发者账号），可用 `NOTARY_KEYCHAIN_PROFILE` 指定。
  - 被拒时，日志在产物目录的 `notary-*.json`。
  - 不能靠移除隔离属性或关 Gatekeeper 绕过。
- **更新包**：在装订票据之后再打 zip，再用 Sparkle 的 `sign_update --account simple-smaller` 签名。

## 地址

| 内容 | 地址 |
| --- | --- |
| 更新源 | `https://bots.it-simple.cn/downloads/smaller/appcast.xml`（服务器 `itsimple-sg`，目录 `/var/www/project/static/downloads/smaller`） |
| 更新包、DMG、校验和 | `https://itsimple-bot.oss-cn-shenzhen.aliyuncs.com/smaller/` |

- **服务器路由**：`bots.it-simple.cn` 的 nginx 只放行清单上写明的路径。`/downloads/smaller/` 这一条加在服务器的 `/var/www/nginx/vhosts/bots.it-simple.cn.conf` 里，紧跟 `/downloads/mac/`，写法照抄那一条（2026-10-17 加），bots 仓库的 `deploy/production/nginx-docker.conf` 同步写了这一条。改这份配置前先备份；`docker exec nginx_proxy nginx -t` 通过后才热加载。这份网关是多个站点共用的。
- **OSS**：分发桶与 bots 共用，按前缀分开。每个对象单独设为公共读，桶里其他对象的权限不变。
- **凭据**：放在 `.local/oss.env`（600，不进仓库）。
- **appcast 历史**：最多保留 12 条历史条目。Sparkle 永远挑当前系统装得了的最新一条，所以不会出现逐版本爬升。

## 签名密钥

Sparkle 用 EdDSA 验签。

- **公钥**：写在 `apps/mac/Info.plist` 的 `SUPublicEDKey`，是公开信息。
- **私钥**：在发布机登录钥匙串的 `simple-smaller` 账户下（`generate_keys --account simple-smaller`）。
- **备份**：私钥丢了，就再也发不出能自动安装的更新，所有人只能手动重装。所以要用 `generate_keys --account simple-smaller -x <文件>` 导出一份存进密码管理器，导出后删掉这个文件。

## 回滚

Sparkle 不降级。新版有问题时：

1. 发一个构建号更高的修复版。
2. 把坏的那条从线上 appcast 里删掉。

## 应用里

- 启动后每 6 小时在后台检查一次（`SUScheduledCheckInterval`）。
- 发现新版本先问用户，同意了才下载安装（`SUAutomaticallyUpdate` 为 false）。
- 「设置 › 通用 › 隐私与更新」可以关掉自动检查。应用菜单里有「检查更新…」。

# Windows 发布与自动更新

安装与更新用 Velopack：每个架构一个安装程序（`SimpleSmaller-win-x64-Setup.exe`、`SimpleSmaller-win-arm64-Setup.exe`），装到用户目录（`%LOCALAPPDATA%\SimpleSmaller`），不要管理员权限。安装包与更新包放 OSS 的 `smaller/win/`，更新源放 `bots.it-simple.cn/downloads/smaller/win/`，和 mac 一样分开放。代码签名暂时没做，首次运行 SmartScreen 会提示，对外大范围发布前再买证书或接 Azure Trusted Signing（vpk 的 `--signParams` / `--azureTrustedSignFile`）。

## 版本号

- 对外版本号写在 `apps/windows/version.txt`，csproj 与 vpk 都读它。更新说明写在 `apps/windows/release-notes/<版本>.md`，出现在应用的「发现新版本」询问里。
- 频道按架构分：`win-x64`、`win-arm64`，打包时写进安装包，装好的应用只看自己的频道。

## 发版

1. VM 里（构建环境见 `docs/build.md` 的 Windows 一节）：`scripts\release-windows.ps1`。两种架构各 `dotnet publish`（自带 .NET 与 Windows App SDK），再 `vpk pack`。产物在 `.local\win-releases\<版本>\`。
2. Mac 上：`scripts/publish-windows.sh --dry-run` 核对，再 `scripts/publish-windows.sh`：从 VM 取回产物、核对校验和、先传 OSS、再原子替换服务器上的 `releases.<频道>.json`。
3. 首次安装链接：`https://itsimple-bot.oss-cn-shenzhen.aliyuncs.com/smaller/win/SimpleSmaller-win-x64-Setup.exe`（ARM64 把 x64 换成 arm64）。

- 只建开始菜单快捷方式，不建桌面快捷方式；不出便携版。
- 卸载（「设置 › 应用」或 `Update.exe --uninstall`）会顺带注销资源管理器右键菜单（`Program.Main` 里 Velopack 的卸载钩子）。

## 应用里

- 启动 1 分钟后检查一次，之后每 6 小时一次；发现新版先问，同意后下载、等这一批处理完再重启安装。设置里可以关自动检查，菜单里有「检查更新…」。
- 开发构建（没装过）不检查更新。验证更新流程：在 VM 里打两个版本（`release-windows.ps1 -Arch arm64` 与 `-Version 0.9.x`），静默装旧版（`Setup.exe --silent`），设环境变量 `SMALLER_UPDATE_URL=<新版产物目录>` 后启动，约 1 分钟弹出询问。2026-10-18 实测 0.9.5 → 0.9.6：下载、安装、重启成功。
- 崩溃日志：`%LOCALAPPDATA%\SimpleSmaller\crash.log`（只在本机，不上传）。

## 回滚

Velopack 不降级。新版有问题时发一个版本号更高的修复版。

# GitHub Releases

上线（OSS + 更新源）之后，同一版本再发到仓库的 GitHub Releases，方便从仓库页面下载和留档。自动更新不读这里，仍走 OSS 与 bots.it-simple.cn。

- 两端版本号各自走，分开发：tag 用 `mac-v<版本>`、`windows-v<版本>`，打在构建那一版的提交上（mac 的构建号 = main 的提交数，`git rev-list --count <提交>` 对得上的那个）。
- mac 传 `SimpleSmaller-<版本>.dmg` 与它的 `SHA256SUMS`；Windows 传两个安装程序，文件名加上版本号（`SimpleSmaller-<版本>-win-x64-Setup.exe`、`…-win-arm64-Setup.exe`），按新名字重算 `SHA256SUMS`。更新包（zip / nupkg）不传。
- 说明照 `release-notes/` 写「这一版」，再加「安装」：系统要求、Windows 未签名时 SmartScreen 怎么放行、`SHA256SUMS` 用法。
- 传完用 `gh release view <tag> --json assets` 核对每个文件的 sha256 和本地一致。

```bash
git tag -a mac-v0.9.7 <提交> -m "简单小一点 mac 0.9.7（构建 168）" && git push origin mac-v0.9.7
gh release create mac-v0.9.7 --title "简单小一点 mac 0.9.7" --notes-file notes.md SimpleSmaller-0.9.7.dmg SHA256SUMS
```

仓库目前是私有的，Releases 只有组织成员能看到；仓库公开后自动对外。
