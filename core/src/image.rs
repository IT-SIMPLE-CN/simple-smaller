//! 图片管线：解码 → 按 EXIF 转正 → 转到 sRGB → 按宽 / 高 / 长边缩小 → 编码 → 限体积。
//!
//! 质量统一用 0–100，由各编码器自己换算：JPEG 走 jpegli 的视觉距离，
//! WebP、AVIF 直接用各自的质量值，PNG 有损时作为减色的质量上限。

use std::io::Cursor;
use std::path::Path;

use fast_image_resize as fir;
use image::{DynamicImage, ImageDecoder, ImageReader};

use crate::ffi;

#[derive(Debug, thiserror::Error)]
pub enum ImageError {
    #[error("读取失败：{0}")]
    Io(#[from] std::io::Error),
    #[error("解码失败：{0}")]
    Decode(String),
    #[error("编码失败：{0}")]
    Encode(String),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SourceFormat {
    Jpeg,
    Png,
    Webp,
    Avif,
    Gif,
    Tiff,
    Bmp,
    /// 从视频里取出的帧。
    VideoFrame,
}

/// 内容大类，决定默认走有损还是无损。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ContentKind {
    /// 照片、渐变丰富的画面。
    Photo,
    /// 截图、图文、平涂插画：颜色少、边缘硬。
    Graphic,
}

/// 解码后的图片，像素统一是 sRGB 的 RGBA8。
#[derive(Clone)]
pub struct Picture {
    pub width: u32,
    pub height: u32,
    pub rgba: Vec<u8>,
    pub has_alpha: bool,
    pub source: SourceFormat,
    pub kind: ContentKind,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OutputFormat {
    Jpeg,
    Webp,
    Avif,
    Png,
    /// 保持原格式（GIF / TIFF / BMP 按有无透明转 PNG 或 JPEG）。
    Original,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SizeMode {
    Width,
    Height,
    LongEdge,
}

#[derive(Debug, Clone)]
pub struct ImageRecipe {
    pub format: OutputFormat,
    /// 0–100。
    pub quality: u8,
    pub lossless: bool,
    pub resize: Option<(SizeMode, u32)>,
    /// 限体积（字节）。设了以后 `quality` 只作为上限。
    pub target_bytes: Option<u64>,
}

#[derive(Debug, Clone)]
pub struct Encoded {
    pub bytes: Vec<u8>,
    pub width: u32,
    pub height: u32,
    pub format: OutputFormat,
    pub quality: u8,
}

// ---------- 解码 ----------

pub fn load(path: &Path) -> Result<Picture, ImageError> {
    let data = std::fs::read(path)?;
    decode(&data)
}

/// JPEG 是否完整：找到第一个扫描段（SOS）之后的结束标记（EOI）。不能只看文件尾——
/// 三星动态照片等会在 EOI 后面接一段视频或附加数据。
pub fn jpeg_has_end(data: &[u8]) -> bool {
    let mut i = 2;
    while i + 4 <= data.len() {
        if data[i] != 0xFF { return false; }
        let marker = data[i + 1];
        if marker == 0xFF { i += 1; continue; }
        if marker == 0xD8 || (0xD0..=0xD7).contains(&marker) { i += 2; continue; }
        let len = u16::from_be_bytes([data[i + 2], data[i + 3]]) as usize;
        if marker == 0xDA {
            return data[i + 2 + len.min(data.len() - i - 2)..].windows(2).any(|w| w == [0xFF, 0xD9]);
        }
        i += 2 + len;
    }
    false
}

/// 引擎自己认识的格式（解坏了就是文件有问题，不再交给系统兜底）。
pub fn is_known_format(data: &[u8]) -> bool {
    is_avif(data) || image::guess_format(data).is_ok_and(|f| matches!(f,
        image::ImageFormat::Jpeg | image::ImageFormat::Png | image::ImageFormat::WebP | image::ImageFormat::Gif | image::ImageFormat::Tiff | image::ImageFormat::Bmp))
}

pub fn decode(data: &[u8]) -> Result<Picture, ImageError> {
    if is_avif(data) {
        let (rgba, width, height, has_alpha) =
            ffi::avif_decode(data).map_err(|e| ImageError::Decode(format!("AVIF {}", e.0)))?;
        return Ok(finish_picture(width, height, rgba, has_alpha, SourceFormat::Avif));
    }

    let reader = ImageReader::new(Cursor::new(data))
        .with_guessed_format()
        .map_err(ImageError::Io)?;
    let source = match reader.format() {
        Some(image::ImageFormat::Jpeg) => SourceFormat::Jpeg,
        Some(image::ImageFormat::Png) => SourceFormat::Png,
        Some(image::ImageFormat::WebP) => SourceFormat::Webp,
        Some(image::ImageFormat::Gif) => SourceFormat::Gif,
        Some(image::ImageFormat::Tiff) => SourceFormat::Tiff,
        Some(image::ImageFormat::Bmp) => SourceFormat::Bmp,
        other => return Err(ImageError::Decode(format!("不支持的格式 {other:?}"))),
    };
    // JPEG 解码器遇到截断会把缺的部分补成灰色照样返回；没有结束标记（FFD9）就当文件不完整。
    if source == SourceFormat::Jpeg && !jpeg_has_end(data) {
        return Err(ImageError::Decode("文件不完整".into()));
    }
    let mut decoder = reader.into_decoder().map_err(|e| ImageError::Decode(e.to_string()))?;
    let orientation = decoder.orientation().map_err(|e| ImageError::Decode(e.to_string()))?;
    let icc = decoder.icc_profile().ok().flatten();
    let mut img = DynamicImage::from_decoder(decoder).map_err(|e| ImageError::Decode(e.to_string()))?;
    img.apply_orientation(orientation);

    let has_alpha = img.color().has_alpha();
    let rgba = img.to_rgba8();
    let (width, height) = rgba.dimensions();
    let mut pixels = rgba.into_raw();
    if let Some(icc) = icc {
        to_srgb(&mut pixels, &icc);
    }
    Ok(finish_picture(width, height, pixels, has_alpha, source))
}

/// 用已有的 RGBA8 像素建一张图（视频帧、剪贴板等来源）。
pub fn from_rgba(width: u32, height: u32, rgba: Vec<u8>, has_alpha: bool, source: SourceFormat) -> Picture {
    finish_picture(width, height, rgba, has_alpha, source)
}

fn finish_picture(width: u32, height: u32, rgba: Vec<u8>, declared_alpha: bool, source: SourceFormat) -> Picture {
    // 带透明通道但每个像素都不透明的图，按不透明处理，才能出 JPEG、省掉透明通道。
    let has_alpha = declared_alpha && rgba.chunks_exact(4).any(|p| p[3] != 255);
    let kind = classify(&rgba, width, height);
    Picture { width, height, rgba, has_alpha, source, kind }
}

fn is_avif(data: &[u8]) -> bool {
    data.len() > 12 && &data[4..8] == b"ftyp" && (&data[8..12] == b"avif" || &data[8..12] == b"avis")
}

/// 按 ICC 把像素转到 sRGB。配置文件本身就是 sRGB 或读不懂时原样保留。
fn to_srgb(pixels: &mut [u8], icc: &[u8]) {
    let Ok(src) = moxcms::ColorProfile::new_from_slice(icc) else { return };
    let dst = moxcms::ColorProfile::new_srgb();
    let Ok(transform) = src.create_transform_8bit(
        moxcms::Layout::Rgba,
        &dst,
        moxcms::Layout::Rgba,
        moxcms::TransformOptions::default(),
    ) else {
        return;
    };
    let input = pixels.to_vec();
    let _ = transform.transform(&input, pixels);
}

/// 抽样统计颜色数：颜色很少的按图形（截图、图文）处理。
fn classify(rgba: &[u8], width: u32, height: u32) -> ContentKind {
    let step = ((width.max(height) / 256).max(1)) as usize;
    let mut colors = std::collections::HashSet::new();
    for y in (0..height as usize).step_by(step) {
        for x in (0..width as usize).step_by(step) {
            let i = (y * width as usize + x) * 4;
            colors.insert([rgba[i] >> 2, rgba[i + 1] >> 2, rgba[i + 2] >> 2]);
            if colors.len() > 2048 {
                return ContentKind::Photo;
            }
        }
    }
    ContentKind::Graphic
}

// ---------- 缩放 ----------

/// 只缩小不放大；目标边比原图大时原样返回。
pub fn resize(picture: &Picture, mode: SizeMode, size: u32) -> Result<Picture, ImageError> {
    let (w, h) = (picture.width as f64, picture.height as f64);
    let scale = match mode {
        SizeMode::Width => size as f64 / w,
        SizeMode::Height => size as f64 / h,
        SizeMode::LongEdge => size as f64 / w.max(h),
    };
    if scale >= 1.0 {
        return Ok(picture.clone());
    }
    let nw = ((w * scale).round() as u32).max(1);
    let nh = ((h * scale).round() as u32).max(1);
    let src = fir::images::Image::from_vec_u8(picture.width, picture.height, picture.rgba.clone(), fir::PixelType::U8x4)
        .map_err(|e| ImageError::Encode(e.to_string()))?;
    let mut dst = fir::images::Image::new(nw, nh, fir::PixelType::U8x4);
    let options = fir::ResizeOptions::new().resize_alg(fir::ResizeAlg::Convolution(fir::FilterType::Lanczos3));
    fir::Resizer::new()
        .resize(&src, &mut dst, &options)
        .map_err(|e| ImageError::Encode(e.to_string()))?;
    Ok(Picture { width: nw, height: nh, rgba: dst.into_vec(), ..picture.clone() })
}

// ---------- 裁切 ----------

/// 裁切框，原图像素坐标。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CropRect {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

/// 按宽高比在图里取居中、最大的框（裁切的默认框）。
pub fn center_max_rect(width: u32, height: u32, aspect: f64) -> CropRect {
    let (w, h) = (width as f64, height as f64);
    let (cw, ch) = if w / h > aspect { (h * aspect, h) } else { (w, w / aspect) };
    let (cw, ch) = (cw.round().max(1.0) as u32, ch.round().max(1.0) as u32);
    CropRect { x: (width - cw.min(width)) / 2, y: (height - ch.min(height)) / 2, width: cw.min(width), height: ch.min(height) }
}

/// 裁出一块；框超出画面时收进画面里。
pub fn crop(picture: &Picture, rect: CropRect) -> Picture {
    let x = rect.x.min(picture.width.saturating_sub(1));
    let y = rect.y.min(picture.height.saturating_sub(1));
    let w = rect.width.min(picture.width - x).max(1);
    let h = rect.height.min(picture.height - y).max(1);
    let mut rgba = Vec::with_capacity(w as usize * h as usize * 4);
    for row in y..y + h {
        let start = (row as usize * picture.width as usize + x as usize) * 4;
        rgba.extend_from_slice(&picture.rgba[start..start + w as usize * 4]);
    }
    Picture { width: w, height: h, rgba, ..picture.clone() }
}

// ---------- 编码 ----------

pub fn resolve_format(format: OutputFormat, picture: &Picture) -> OutputFormat {
    match format {
        OutputFormat::Original => match picture.source {
            SourceFormat::Jpeg => OutputFormat::Jpeg,
            SourceFormat::Png => OutputFormat::Png,
            SourceFormat::Webp => OutputFormat::Webp,
            SourceFormat::Avif => OutputFormat::Avif,
            _ if picture.has_alpha => OutputFormat::Png,
            _ => OutputFormat::Jpeg,
        },
        other => other,
    }
}

pub fn encode(picture: &Picture, format: OutputFormat, quality: u8, lossless: bool) -> Result<Vec<u8>, ImageError> {
    let q = quality.min(100);
    let err = |e: ffi::CodecError| ImageError::Encode(format!("{format:?} 错误码 {}", e.0));
    match resolve_format(format, picture) {
        OutputFormat::Jpeg => {
            let rgb = flatten_rgb(picture);
            let params = ffi::JpegParams {
                distance: ffi::jpegli_quality_to_distance(q),
                progressive: true,
                // 高质量时不做色度抽样，红色文字与细边更干净。
                chroma_444: q >= 90,
            };
            ffi::jpegli_encode(&rgb, picture.width, picture.height, &params, None).map_err(err)
        }
        OutputFormat::Webp => {
            let params = ffi::WebpParams {
                quality: q as f32,
                lossless,
                method: if lossless { 5 } else { 6 },
                sharp_yuv: true,
                target_size: None,
            };
            ffi::webp_encode(&picture.rgba, picture.width, picture.height, picture.has_alpha, &params).map_err(err)
        }
        OutputFormat::Avif => {
            let params = ffi::AvifParams { quality: if lossless { 100 } else { q }, speed: 6, yuv444: lossless || q >= 90 };
            ffi::avif_encode(&picture.rgba, picture.width, picture.height, picture.has_alpha, &params, None).map_err(err)
        }
        OutputFormat::Png => encode_png(picture, q, lossless),
        OutputFormat::Original => unreachable!(),
    }
}

/// JPEG 没有透明：透明部分铺白底。
fn flatten_rgb(picture: &Picture) -> Vec<u8> {
    picture
        .rgba
        .chunks_exact(4)
        .flat_map(|p| {
            let a = p[3] as u32;
            let mix = |c: u8| ((c as u32 * a + 255 * (255 - a) + 127) / 255) as u8;
            [mix(p[0]), mix(p[1]), mix(p[2])]
        })
        .collect()
}

fn png_options() -> oxipng::Options {
    let mut options = oxipng::Options::from_preset(3);
    options.strip = oxipng::StripChunks::Safe;
    options
}

fn encode_png(picture: &Picture, quality: u8, lossless: bool) -> Result<Vec<u8>, ImageError> {
    let png_err = |e: oxipng::PngError| ImageError::Encode(e.to_string());
    if lossless {
        let raw = oxipng::RawImage::new(picture.width, picture.height, oxipng::ColorType::RGBA, oxipng::BitDepth::Eight, picture.rgba.clone())
            .map_err(png_err)?;
        return raw.create_optimized_png(&png_options()).map_err(png_err);
    }

    let liq_err = |e: imagequant::Error| ImageError::Encode(format!("减色失败：{e}"));
    let mut attr = imagequant::new();
    attr.set_speed(4).map_err(liq_err)?;
    attr.set_quality(0, quality).map_err(liq_err)?;
    let pixels: Vec<imagequant::RGBA> = picture
        .rgba
        .chunks_exact(4)
        .map(|p| imagequant::RGBA::new(p[0], p[1], p[2], p[3]))
        .collect();
    let mut img = attr
        .new_image(pixels, picture.width as usize, picture.height as usize, 0.0)
        .map_err(liq_err)?;
    let mut result = attr.quantize(&mut img).map_err(liq_err)?;
    result.set_dithering_level(1.0).map_err(liq_err)?;
    let (palette, indexes) = result.remapped(&mut img).map_err(liq_err)?;
    let palette = palette.into_iter().map(|c| rgb::RGBA8::new(c.r, c.g, c.b, c.a)).collect();
    let raw = oxipng::RawImage::new(
        picture.width,
        picture.height,
        oxipng::ColorType::Indexed { palette },
        oxipng::BitDepth::Eight,
        indexes,
    )
    .map_err(png_err)?;
    raw.create_optimized_png(&png_options()).map_err(png_err)
}

// ---------- 配方 ----------

/// 按配方处理一张图。限体积时先二分质量，质量到底线还塞不下就按比例缩尺寸再试，最多三轮。
pub fn render(picture: &Picture, recipe: &ImageRecipe) -> Result<Encoded, ImageError> {
    let mut current = match recipe.resize {
        Some((mode, size)) => resize(picture, mode, size)?,
        None => picture.clone(),
    };
    let format = resolve_format(recipe.format, &current);

    let Some(target) = recipe.target_bytes else {
        let bytes = encode(&current, format, recipe.quality, recipe.lossless)?;
        // WebP：截图、图文这类图形内容试一次无损，文字边缘不糊；
        // 无损不比有损大 25% 以上就用无损（实测截图无损往往更小），照片仍用有损。
        if format == OutputFormat::Webp && !recipe.lossless && current.kind == ContentKind::Graphic {
            let lossless = encode(&current, format, recipe.quality, true)?;
            if lossless.len() as f64 <= bytes.len() as f64 * 1.25 {
                return Ok(Encoded { bytes: lossless, width: current.width, height: current.height, format, quality: 100 });
            }
        }
        return Ok(Encoded { bytes, width: current.width, height: current.height, format, quality: recipe.quality });
    };

    const FLOOR: u8 = 30;
    for _round in 0..3 {
        if let Some((bytes, q)) = search_quality(&current, format, FLOOR, recipe.quality, target)? {
            return Ok(Encoded { bytes, width: current.width, height: current.height, format, quality: q });
        }
        // 质量到底线仍超出：按面积比例缩小，留 5% 余量。
        let floor_size = encode(&current, format, FLOOR, false)?.len() as f64;
        let ratio = ((target as f64 / floor_size).sqrt() * 0.95).min(0.9);
        let new_width = ((current.width as f64) * ratio).round() as u32;
        current = resize(&current, SizeMode::Width, new_width.max(16))?;
    }
    let bytes = encode(&current, format, FLOOR, false)?;
    Ok(Encoded { bytes, width: current.width, height: current.height, format, quality: FLOOR })
}

/// 在 [lo, hi] 里找体积不超过 target 的最高质量；最低质量也超出时返回 None。
fn search_quality(picture: &Picture, format: OutputFormat, lo: u8, hi: u8, target: u64) -> Result<Option<(Vec<u8>, u8)>, ImageError> {
    let top = encode(picture, format, hi, false)?;
    if top.len() as u64 <= target {
        return Ok(Some((top, hi)));
    }
    let (mut lo, mut hi) = (lo, hi);
    let mut best: Option<(Vec<u8>, u8)> = None;
    while lo <= hi {
        let mid = lo + (hi - lo) / 2;
        let bytes = encode(picture, format, mid, false)?;
        if bytes.len() as u64 <= target {
            best = Some((bytes, mid));
            lo = mid + 1;
        } else if mid == 0 {
            break;
        } else {
            hi = mid - 1;
        }
    }
    Ok(best)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn photo(w: u32, h: u32) -> Picture {
        let mut rgba = Vec::with_capacity((w * h * 4) as usize);
        for y in 0..h {
            for x in 0..w {
                let n = ((x * 7919 + y * 104729) % 251) as u8;
                rgba.extend_from_slice(&[(x * 255 / w) as u8 ^ n / 8, (y * 255 / h) as u8, n, 255]);
            }
        }
        finish_picture(w, h, rgba, false, SourceFormat::Png)
    }

    /// 像截图：大块纯色加几条细线。
    fn screenshot(w: u32, h: u32) -> Picture {
        let mut rgba = Vec::with_capacity((w * h * 4) as usize);
        for y in 0..h {
            for x in 0..w {
                let c: [u8; 4] = if y % 40 == 0 || x % 120 == 0 { [40, 40, 48, 255] } else if y < 60 { [239, 57, 34, 255] } else { [250, 250, 252, 255] };
                rgba.extend_from_slice(&c);
            }
        }
        finish_picture(w, h, rgba, false, SourceFormat::Png)
    }

    #[test]
    fn 截断的图片报错() {
        let p = photo(320, 240);
        let png = encode(&p, OutputFormat::Png, 100, true).unwrap();
        assert!(decode(&png).is_ok());
        assert!(decode(&png[..png.len() / 3]).is_err(), "截断的 PNG 不能当成功");
        let jpg = encode(&p, OutputFormat::Jpeg, 90, false).unwrap();
        assert!(decode(&jpg[..jpg.len() / 3]).is_err(), "截断的 JPEG 不能当成功");
        // EOI 后面接一大段附加数据（动态照片）仍算完整。
        let mut motion = jpg.clone();
        motion.extend(std::iter::repeat_n(0x42u8, 20_000));
        assert!(decode(&motion).is_ok(), "带附加数据的 JPEG 应能正常读");
    }

    #[test]
    fn webp_截图无损照片有损() {
        let recipe = ImageRecipe { format: OutputFormat::Webp, quality: 78, lossless: false, resize: None, target_bytes: None };
        let shot = screenshot(600, 400);
        assert_eq!(shot.kind, ContentKind::Graphic);
        assert_eq!(render(&shot, &recipe).unwrap().quality, 100, "截图应走无损");
        let p = photo(600, 400);
        assert_eq!(render(&p, &recipe).unwrap().quality, 78, "照片仍走有损");
    }

    #[test]
    fn 按宽缩小且不放大() {
        let p = photo(400, 200);
        let r = resize(&p, SizeMode::Width, 100).unwrap();
        assert_eq!((r.width, r.height), (100, 50));
        let same = resize(&p, SizeMode::Width, 800).unwrap();
        assert_eq!((same.width, same.height), (400, 200));
    }

    #[test]
    fn 限体积不超过目标() {
        let p = photo(800, 600);
        for format in [OutputFormat::Jpeg, OutputFormat::Webp, OutputFormat::Avif] {
            let recipe = ImageRecipe { format, quality: 90, lossless: false, resize: None, target_bytes: Some(30_000) };
            let out = render(&p, &recipe).unwrap();
            assert!(out.bytes.len() <= 30_000, "{format:?} 输出 {} 字节", out.bytes.len());
        }
    }

    #[test]
    fn png_有损与无损都能解回来() {
        let p = photo(120, 80);
        for lossless in [false, true] {
            let bytes = encode(&p, OutputFormat::Png, 80, lossless).unwrap();
            let back = decode(&bytes).unwrap();
            assert_eq!((back.width, back.height), (120, 80));
        }
    }

    #[test]
    fn 透明图能编_avif_与_webp() {
        let mut p = photo(64, 64);
        for px in p.rgba.chunks_exact_mut(4).take(500) {
            px[3] = 0;
        }
        p.has_alpha = true;
        for format in [OutputFormat::Avif, OutputFormat::Webp, OutputFormat::Png] {
            let bytes = encode(&p, format, 70, false).unwrap();
            assert!(decode(&bytes).unwrap().has_alpha, "{format:?} 丢了透明");
        }
    }

    #[test]
    fn 居中最大裁切框() {
        assert_eq!(center_max_rect(6000, 4000, 0.75), CropRect { x: 1500, y: 0, width: 3000, height: 4000 });
        assert_eq!(center_max_rect(1000, 3000, 1.0), CropRect { x: 0, y: 1000, width: 1000, height: 1000 });
        let p = photo(400, 200);
        let c = crop(&p, CropRect { x: 350, y: 150, width: 200, height: 200 });
        assert_eq!((c.width, c.height), (50, 50));
        assert_eq!(&c.rgba[..4], &p.rgba[(150 * 400 + 350) * 4..(150 * 400 + 350) * 4 + 4]);
    }

    #[test]
    fn 透明图转_jpeg_铺白底() {
        let rgba = vec![0, 0, 0, 0, 255, 0, 0, 255];
        let p = finish_picture(2, 1, rgba, true, SourceFormat::Png);
        assert!(p.has_alpha);
        assert_eq!(flatten_rgb(&p), vec![255, 255, 255, 255, 0, 0]);
    }
}
