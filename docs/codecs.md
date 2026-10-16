# 编码库与图片管线

## 随包编码库

由 `scripts/build-codecs-mac.sh` 按固定版本从源码编成静态库，装到 `.local/codecs/<架构>`。升级版本时改脚本顶部的版本号，并在这里记录原因与实测结果。

| 库 | 版本 | 用途 | 许可证 |
| --- | --- | --- | --- |
| jpegli | 提交 `031a0077`（上游未发版） | JPEG 编码 | BSD-3-Clause |
| libwebp | v1.6.0 | WebP 编码 | BSD-3-Clause |
| aom | v3.15.2 | AV1 编解码（AVIF 用） | BSD-2-Clause + AOM 专利许可 |
| libavif | v1.4.2 | AVIF 容器 | BSD-2-Clause |
| libimagequant（imagequant crate） | 4.4 | PNG 有损减色 | GPL-3.0-or-later |
| oxipng | 10.2 | PNG 无损优化 | MIT |
| fast_image_resize | 6.1 | 缩放（Lanczos3） | MIT / Apache-2.0 |
| moxcms | 0.9 | ICC 色彩转换 | BSD-3-Clause / Apache-2.0 |
| image | 0.25 | JPG / PNG / GIF / WebP / TIFF / BMP 解码 | MIT / Apache-2.0 |

## C 库外壳

`core/native/` 下每个 C 库包一层很薄的外壳：只收像素与参数，返回编码后的字节，出错返回负数。libjpeg 系的 `longjmp` 在外壳里接住，不穿过 FFI 边界。Rust 侧声明在 `core/src/ffi.rs`。

- jpegli：按 butteraugli 视觉距离设质量（`jpegli_quality_to_distance`），默认渐进式；质量 ≥ 90 时不做色度抽样。
- libwebp：高级配置接口，method 6、开 sharp_yuv；无损用压缩等级预设。
- libavif：固定选 aom，显式开 `tune=iq`，设置失败直接报错，不悄悄退回旧模式；速度档 6。

## 图片管线（`core/src/image.rs`）

1. 解码：AVIF 走 libavif，其余走 image crate；按 EXIF 方向转正。
2. 色彩：带 ICC 的图转成 sRGB 后输出不再附带 ICC。
3. 透明：声明有透明通道但每个像素都不透明时，按不透明处理。
4. 分类：抽样统计颜色数，颜色少的按「图形」（截图、图文）处理，供默认走无损时参考。
5. 缩放：按宽 / 高 / 长边，只缩小不放大。
6. 编码：质量统一 0–100，由各编码器换算。JPEG 遇到透明铺白底。PNG 有损先减色再交给 oxipng。
7. 限体积：先在 30–上限之间二分质量；质量到 30 仍超出，按面积比例缩小（留 5% 余量），最多三轮。

## 实测方法

`tools/bench-image`：每张图按质量 20–95 扫一遍，用 SSIMULACRA2 打分（70 分以上算好，80 分以上算高），插值出达到 70 / 80 分所需的体积，与普通 JPEG 编码、macOS ImageIO（等同「预览」导出）对比。结果写在 `docs/bench/`。

## 视频管线（`core/src/video.rs`）

随包的 ffmpeg / ffprobe 由 `scripts/build-ffmpeg-mac.sh` 编译：FFmpeg n9.0.2（GPL 构建）+ x264 stable（`b35605ac`）+ zimg 3.0.6，系统侧用 VideoToolbox 与 AudioToolbox。关掉自动探测，不链接本机 Homebrew 的库；产物只依赖系统库。x264 与 ffmpeg 的构建系统处理不了带空格的路径，所以在 `~/Library/Caches/simple-smaller` 里编译后拷回 `.local/ffmpeg/<架构>/bin`。

| 组件 | 版本 | 许可证 |
| --- | --- | --- |
| FFmpeg | n9.0.2 | GPL-3.0（含 x264 后整体为 GPL） |
| x264 | stable `b35605ac` | GPL-2.0-or-later |
| zimg | 3.0.6 | WTFPL |
| gifski | 1.34 | AGPL-3.0 |

1. 探测：ffprobe 读时长、显示尺寸（按旋转修正）、帧率、码率、音轨、HDR（PQ / HLG）。
2. 滤镜链：裁切 → HDR 转 SDR（zscale + hable 色调映射，输出 BT.709）→ 限帧率 → 缩放（Lanczos，宽高取偶数）→ yuv420p。
3. 编码：
   - H.264 画质优先：x264 `-preset slow`；码率档用 CRF 20 / 23 / 27；限体积用两遍编码，码率按「目标体积 × 0.97 ÷ 时长 − 音频 128k」倒推，不超过源码率，超出目标再按比例降码率重压一次。
   - 速度优先与 HEVC：VideoToolbox，码率档用恒定质量 65 / 55 / 45，限体积用目标码率并校验重试。
   - 音频：AudioToolbox AAC 128k；去掉元数据（含位置）；文件头前置。
4. 静态帧：ffmpeg 输出 RGBA 原始帧。序列帧交给图片管线编码；GIF 交给 gifski。
5. 进度读 `-progress`；取消时结束子进程。输出先写同目录隐藏临时文件，完成后改名，失败或取消时删除。

实测见 `docs/bench/2026-10-16-video.md`。

## PDF 管线（`core/src/pdf.rs`）

MuPDF 1.28.5（AGPL-3.0）由 `scripts/build-mupdf-mac.sh` 编成静态库（同样在无空格目录编译）。用 `-DTOFU -DTOFU_CJK` 去掉内置的 Noto 兜底字体，静态库从几十 MB 降到 16 MB；代价是 PDF 没有嵌入字体时，缩略图与转图片里这部分文字显示不出来，压缩与合并不受影响。外壳 `core/native/pdf_shim.c` 用 `fz_try` 接住错误，并关掉 MuPDF 往 stderr 打印日志。

1. 压缩：彩色、灰度图超过目标 dpi 的 1.5 倍时按目标 dpi 降采样，重压为 JPEG；黑白图降采样后用 CCITT G4。MuPDF 只在单张新图更小时替换。可选字体子集化、去掉 Info 与 XMP 元数据。保存时深比较去重（garbage 4）、压缩流与对象流。
2. 兜底：重压后仍剩原体积 90% 以上时再试「只重新保存」，取更小者；都不比原文件小就保留原文件、不写出。
3. 限体积：从当前档位起按（dpi, 质量）阶梯 150/75 → 130/70 → 110/68 → 96/62 → 80/55 → 72/50 逐档降。
4. 校验：写出的文件要能重新打开、页数不变，再改名落盘。
5. 组装：按（第几个文件, 第几页）的顺序挑页拼成新文件，合并、排序、删页、拆分、提取共用一个接口。
6. 渲染：按目标宽度渲染单页（白底），供缩略图与转图片。

档位：屏幕阅读 110 dpi / q70；邮件附件 150 dpi / q75；打印保真 300 dpi / q88。实测见 `docs/bench/2026-10-16-pdf.md`。
