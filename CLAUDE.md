# SIMPLE SMALLER · AI 协作规范

简单小一点：macOS / Windows 原生的图片、视频、PDF 压缩与裁切工具，IT-SIMPLE 开源（AGPLv3）。全部在本机处理，编解码能力随包内置，用户不另装组件。

## 设计源

- 设计稿：Claude Design 画布 <https://claude.ai/artifact/6fA3baqMbLuLNAv3gYqS9F>。界面文案、区块结构、排列顺序逐字照搬；设计更新时按 diff 对齐，不自由发挥。
- 以画布「V2.1 · 调整」页为准（V2 便当盒视觉 + 合理性调整）：浅灰底白色圆角卡片、首页选功能、顶部配方条、右下红色开始块。工具栏、分段等系统控件仍用系统样式与 Liquid Glass。品牌红 `#EF3922` 只用于主按钮、选中和进度。
- 任何地方不写预估体积，点「开始」才处理。
- 不写宣传语，不做五周年专题（只在「关于」里一句），不做「已为你省下」统计。

## 结构

| 位置 | 作用 |
| --- | --- |
| `core` | Rust 引擎 `smaller-core`：图片 / 视频 / PDF 管线、配方、队列、命名、试压缓存；UniFFI 导出 Swift / C# 接口 |
| `apps/mac` | macOS 应用：SwiftUI + AppKit，最低 macOS 14；26 起用 Liquid Glass，14 / 15 退回系统材质 |
| `apps/windows` | Windows 应用（mac 之后做）：WinUI 3，Mica / Acrylic |
| `docs` | 引擎说明、配方规范、验证方法 |

## 产品规则

- 一个配方 = 一次输出。格式、尺寸、质量、裁切、命名、存放位置同属一个配方；批量文件共用同一套设置，只有裁切框按张记。
- 图片分段：压缩 · 裁切。视频：压缩 · 剪辑 · 序列帧。PDF：压缩 · 合并 · 拆分 · 转图片 · 图片转 PDF。
- 尺寸默认按「宽」，可切换高 / 长边；裁切填宽、高按比例算；比例写成「平台 比例」（小红书 3:4）。
- 裁切默认居中、按比例取最大，每张可手调，↩ 确认并到下一张。
- 原文件只读；输出先写隐藏临时文件再改名；压完变大保留原文件；可撤销（移到废纸篓）。
- 圆环：mac 访达 ⌥ 点击、Windows 资源管理器 Alt 点击。中心镂空，参考 dotnfilm-bots `apps/mac/Sources/AgentsConsole/Media` 移植后按本项目重写。

## 引擎选型（2026-10 调研结论）

- 图片：jpegli（JPEG）、libwebp 1.6、libavif + aom（tune=iq）、libimagequant + oxipng（PNG）、fast_image_resize、moxcms；HEIC：mac 用 ImageIO，Windows 优先系统扩展，缺失才用 libde265。
- 视频：FFmpeg GPL 构建，作为独立进程。H.264 默认 x264 两遍编码；HEVC 只走系统 / 显卡硬件编码（规避专利，不内置 x265）；4K 长片默认硬件；GIF 用 gifski。
- PDF：只用 MuPDF（图片降采样重压、字体子集化、去重保存、组装页面、渲染缩略图与转图片）。实测后不再需要 qpdf 与 PDFium，见 `docs/codecs.md`。
- 随包引擎的源码与许可证声明同步公开。

## 体验标准

- 优先系统控件、系统动画与弹簧曲线；自定义动效用 `cubic-bezier(.22,1,.36,1)`，0.2–0.4 秒。
- 编解码全部在后台线程；界面线程不做重活，120Hz 不掉帧。
- 尊重「减弱动态效果」「降低透明度」与旁白。

## 构建与验证

- 构建、启动应用、功能验证由 AI 自行完成：改完自己跑 `scripts/build-core-mac.sh` 与 `swift build`，启动应用，按受影响的用户路径实际操作到结果（截图核对界面与动效），再向用户汇报。不把可执行的验证交回用户。
- 启动或重启前先确认进程来源，只动本项目启动的 SimpleSmaller 进程，验证完关掉。
- 引擎改动跑 `cargo test`（用 rustup 工具链，见 `docs/build.md`）；压缩效果用真实素材实测，汇报前后体积与画质分数，不以编译通过代替验证。
- 如实汇报实际操作过的路径、看到的结果、失败项和没验证到的边界。

## 协作

- 一律中文：回复、文档、界面文案、commit message（保留 conventional 前缀）。
- 已获长期授权：每一步自己验证通过后直接 commit 并 push 到私有仓库 `IT-SIMPLE-CN/simple-smaller`，遇到问题自己定位、修复、找方案，阶段性汇报结果。仓库公开、对外发布、签名分发仍需用户授权。
- 过程资产（原型、临时脚本）用完即删，临时文件放 `.local/`（不提交）。
