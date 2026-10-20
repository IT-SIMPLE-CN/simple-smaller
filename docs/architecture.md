# 架构与约定

简单小一点由一个 Rust 引擎和两个原生界面组成：macOS 用 SwiftUI + AppKit，Windows 用 WinUI 3。图片、视频、PDF 的处理全部在引擎里完成，界面只负责选文件、调设置、看结果。编解码库随包内置，用户不用另装组件。

## 两端分工

| 位置 | 作用 |
| --- | --- |
| `core` | Rust 引擎 `smaller-core`：图片 / 视频 / PDF 管线、配方、队列、命名；用 UniFFI 导出 Swift / C# 接口 |
| `apps/mac` | macOS 应用：SwiftUI + AppKit，最低 macOS 14；macOS 26 起用 Liquid Glass，14 / 15 退回系统材质 |
| `apps/windows` | Windows 应用：WinUI 3（Windows App SDK，.NET 10，非打包 + Velopack），界面全部用 C# 代码搭，字体 MiSans |
| `scripts` | 编解码库、引擎、打包、发布、图标渲染脚本（见 [build.md](build.md)、[release.md](release.md)） |
| `tools` | 实测工具（图片、视频、PDF 的压缩效果对比），结果在 `docs/bench/` |
| `docs` | 构建、发版、编码库说明、实测记录 |

- **引擎**：Rust。mac 端以静态库链接进应用；Windows 端以独立进程 `smaller-core.exe` 运行，界面通过进程间通信调用。
- **视频**：ffmpeg / ffprobe 作为独立程序随应用分发，引擎按需启动。
- 两端的 `Model/`、`Services/` 同名同职责，Windows 版逐个从 Swift 移植；改一端的行为时，另一端一起对齐。

## 引擎接口

引擎的对外接口在 `core/src/api/`，按类型与设置、任务、引擎入口、视频、PDF 分文件。各条管线的做法见 [codecs.md](codecs.md)。

接口改动后要重新生成绑定：

- mac：`scripts/build-core-mac.sh`（生成 Swift 绑定与静态库）。
- Windows：在 Windows 里跑 `scripts\build-core-windows.ps1`。

生成的绑定不进仓库（见 `.gitignore`）。

## mac 应用的目录（`apps/mac/Sources/SimpleSmaller`）

| 目录 | 放什么 |
| --- | --- |
| `App/` | 入口、菜单命令、AppDelegate、自动更新 |
| `Model/` | 配方、文件、工作区（按分段拆成 `Workspace+裁切 / 视频 / 开始`）、用词 |
| `Services/` | `AppModel`（按职责拆成 `+Files`、`+Batch`、`+Activity`）、圆环与服务用的 `QuickRun`、通知、引擎回调桥 |
| `Auto/` | 剪贴板自动压缩、监视文件夹、HUD |
| `Ring/` | 访达圆环、结果卡、右键服务 |
| `Native/` | 图片头、ImageIO 解码、PDF 缩略图、视频播放 |
| `Views/Bento/` | 色板与卡片、控件（胶囊、滑块分段、按钮、滑杆）、动效；新界面一律用这里的组件和色板，不用系统灰 |
| `Views/Main/` | 主窗口、首页、工作区网格、完成页、开始块、配方条、工具栏 |
| `Views/Cards/` | 网格里的各张卡（文件、图片、裁切、视频、PDF、输出） |
| `Views/Preview/` | 左大卡里的预览（裁切、剪辑、序列帧、PDF 页面、胶片条、前后对比） |
| `Views/Common/` | 文件表、缩略图、文件交互（拖出、快速查看）、密码、窗口外观、品牌标 |
| `Views/Windows/` | 设置、新建配方、菜单栏小窗、关于 |

## Windows 应用的目录（`apps/windows/SimpleSmaller`）

| 目录 | 放什么 |
| --- | --- |
| `Program.cs`、`App.xaml.cs`、`App/` | 入口（第一行 Velopack，单实例）、启动参数、窗口打开、崩溃日志、资源管理器右键菜单、平台补充（缩略图、任务栏） |
| `Model/`、`Services/` | 与 mac 同名同职责；`Updates.cs` 是 Velopack 自动更新 |
| `Bento/` | 色板（`Themes/Bento.xaml` + `Palette`）、卡片与控件、动效、`LineIcon`（照搬设计稿 SVG 的线性图标） |
| `Views/Main`、`Views/Cards`、`Views/Preview`、`Views/Common`、`Views/Result`、`Views/Windows` | 对应 mac 的同名目录；网格每格由 `Views/Cards/*Slots.cs` 决定 |
| `Ring/`、`Auto/` | 资源管理器 Alt 点击圆环、托盘与剪贴板 / 监视文件夹 |

## 界面

- 浅灰底、白色圆角卡片：首页选功能，顶部配方条，右下红色开始块。工具栏、分段等系统控件仍用系统样式（macOS 26 起是 Liquid Glass）。
- 品牌红 `#EF3922` 只用于主按钮、选中和进度。
- 任何地方不写预估体积，点「开始」才处理。
- 不写宣传语，不做「已为你省下」之类的统计。

## 产品规则

- 一个配方 = 一次输出。格式、尺寸、质量、裁切、命名、存放位置同属一个配方；批量文件共用同一套设置，只有裁切框按张记。
- 图片只有一页，不分「压缩 / 裁切」：比例是配方里的参数，选「原比例」就是只压缩，选了比例就同时裁切，缩略图上直接画出每张的框，点一张进大图调。视频：压缩 · 剪辑 · 序列帧。PDF：压缩 · 合并 · 拆分 · 转图片 · 图片转 PDF。
- 尺寸默认按「宽」，可切换高 / 长边；裁切填宽、高按比例算；比例写成「平台 比例」（小红书 3:4）。
- 裁切默认居中、按比例取最大，每张可手调；框动了就按新的，没有「确认」这一步，↩ / → 只是到下一张。
- 原文件只读；输出先写隐藏临时文件再改名；压完变大保留原文件；可撤销（移到废纸篓 / 回收站）。
- 圆环：mac 访达 ⌥ 点击、Windows 资源管理器 Alt 点击，中心镂空。

## 体验标准

- 优先系统控件、系统动画与弹簧曲线；自定义动效用 `cubic-bezier(.22,1,.36,1)`，0.2–0.4 秒。
- 编解码全部在后台线程；界面线程不做重活，120Hz 不掉帧。
- 尊重「减弱动态效果」「降低透明度」与旁白 / 讲述人。
- 界面改动浅色、深色都要看。

## Windows 的坑（都踩过）

- WinUI 不会把过大的圆角收成半圆，会画成椭圆：胶囊用 `Capsule.Apply`，不写 `CornerRadius(100)`。
- Composition 位移动 `Translation`，不动 `Visual.Offset`（会被布局覆盖）。`Border`、`Viewbox` 是密封类。
- SVG（SvgImageSource）不支持原点非 0 的 viewBox、不画 `<text>`、上千个元素会丢：装饰图形用代码画。
- 外部进程（ffmpeg 等）一律 `CREATE_NO_WINDOW`，否则闪黑框，控制台分配失败时报 0xc0000142。
- PowerShell 5.1 按 GBK 读脚本、MSVC 按 GBK 读源码：`.ps1` 存 UTF-8 带 BOM，C/C++ 外壳编译加 `/utf-8`。
- 非打包 WinUI 要 `EnableMsixTooling`，否则 `dotnet publish` 不带 `.pri`，装好就崩。
- MiSans 按许可协议不进仓库，构建时由 `scripts/fetch-misans.ps1` 下载；Windows 的 ffmpeg 自编最小 GPL 版（不含 x265），见 `scripts/build-ffmpeg-windows.sh`。

## 引擎选型

- 图片：jpegli（JPEG）、libwebp、libavif + aom（tune=iq）、libimagequant + oxipng（PNG）、fast_image_resize、moxcms；HEIC：mac 用 ImageIO，Windows 用系统扩展。
- 视频：FFmpeg GPL 构建，作为独立进程。H.264 默认 x264 两遍编码；HEVC 只走系统 / 显卡硬件编码（不内置 x265）；GIF 用 gifski。
- PDF：只用 MuPDF（图片降采样重压、字体子集化、去重保存、组装页面、渲染缩略图与转图片）。

版本、许可证与实测见 [codecs.md](codecs.md)。
