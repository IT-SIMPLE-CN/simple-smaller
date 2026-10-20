# 参与贡献

谢谢你愿意帮简单小一点变得更好。提交之前请读完这一页。

## 先开 Issue，再提 PR

- 修 bug、加功能、改界面，都先开一个 Issue 说清楚要解决什么，讨论定了再动手，免得白做。
- 错别字、文档链接这类很小的改动可以直接提 PR。
- 一个 PR 只做一件事。

## 构建与运行

- 环境准备与构建步骤见 [docs/build.md](docs/build.md)。
- 目录结构、两端分工、引擎接口与约定见 [docs/architecture.md](docs/architecture.md)。

## 提交说明

- 用 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/) 前缀：`feat`、`fix`、`docs`、`refactor`、`chore` 等，说明用中文，例如：

  ```text
  fix(windows): 窄窗口下开始块不再被截断
  ```

- 每个提交都要带 `Signed-off-by`（DCO，见下文「授权」），用 `git commit -s` 自动加上。

## 改动要验证

- **引擎**（`core/`）：跑 `cargo test`（用法见 [docs/build.md](docs/build.md)）。动了压缩效果的，用真实素材实测，在 PR 里写清楚前后体积与画质分数（`tools/bench-*`），不以编译通过代替验证。
- **界面**：PR 里附改动前后的截图，浅色、深色都要有；动了两端共有的行为，说明另一端是否已对齐。
- **对外接口**：改了 `core/src/api/` 要重新生成绑定，两端都能编过。

## 产品原则

以下几条不接受改动：

- 原文件只读，输出另写一份；压完变大保留原文件。
- 全部在本机处理，不上传文件，不加统计或遥测。
- 不写预估体积，点「开始」才处理。
- 不写宣传语。

## 授权

本项目按 [AGPL-3.0-only](LICENSE) 开源。你提交的贡献同样按 AGPL-3.0-only 授权。

我们使用 [Developer Certificate of Origin](https://developercertificate.org/)（DCO）：在提交说明末尾加上

```text
Signed-off-by: 你的名字 <你的邮箱>
```

即表示你确认有权按本项目的许可证提交这份代码。`git commit -s` 会自动加上这一行。没有 `Signed-off-by` 的 PR 无法合并。

## 安全问题

不要公开提 Issue，按 [SECURITY.md](SECURITY.md) 私下报告。
