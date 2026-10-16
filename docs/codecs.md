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
