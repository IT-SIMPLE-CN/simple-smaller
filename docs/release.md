# macOS 发布与自动更新

做法与 dotnfilm-bots（`docs/implementation/macos-release.md`）一致：Developer ID 签名、Apple 公证、装订票据，Sparkle 自动更新；安装包放阿里云 OSS，更新源放自己的域名。

## 版本号

- 对外版本号写在 `apps/mac/version.txt`（`CFBundleShortVersionString`）。
- 构建号是 main 的提交数（`CFBundleVersion`），只增不减。Sparkle 按构建号判断新旧。
- 发版前写好 `apps/mac/release-notes/<版本>.html`，它会出现在更新弹窗里。只写用户能感觉到的变化。

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
