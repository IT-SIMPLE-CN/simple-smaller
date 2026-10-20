//! 线上的数据形状：与 `smaller_core::api` 的类型一一对应，字段名一律 snake_case，
//! 带数据的枚举用 `"type"` 标出是哪一种（如 `{"type":"folder","path":"D:\\out"}`）。
//! 只在这里做 JSON ↔ 引擎类型的转换，引擎本身不依赖序列化。

use serde::{Deserialize, Serialize};
use smaller_core::api as core;

// ---------- 请求里的设置 ----------

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum Kind {
    Image,
    Video,
    Pdf,
}

impl From<Kind> for core::Kind {
    fn from(k: Kind) -> Self {
        match k {
            Kind::Image => core::Kind::Image,
            Kind::Video => core::Kind::Video,
            Kind::Pdf => core::Kind::Pdf,
        }
    }
}

#[derive(Deserialize, Clone)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum Place {
    BesideSource { folder_suffix: String },
    Folder { path: String },
}

impl From<Place> for core::Place {
    fn from(p: Place) -> Self {
        match p {
            Place::BesideSource { folder_suffix } => core::Place::BesideSource { folder_suffix },
            Place::Folder { path } => core::Place::Folder { path },
        }
    }
}

#[derive(Deserialize, Clone)]
pub struct Naming {
    pub template: String,
    pub lowercase: bool,
    pub pinyin: bool,
    pub hyphenate: bool,
    pub date: String,
}

impl From<Naming> for core::Naming {
    fn from(n: Naming) -> Self {
        core::Naming { template: n.template, lowercase: n.lowercase, pinyin: n.pinyin, hyphenate: n.hyphenate, date: n.date }
    }
}

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum ImageFormat {
    Jpeg,
    Webp,
    Avif,
    Png,
    Original,
}

impl From<ImageFormat> for core::ImageFormat {
    fn from(f: ImageFormat) -> Self {
        match f {
            ImageFormat::Jpeg => core::ImageFormat::Jpeg,
            ImageFormat::Webp => core::ImageFormat::Webp,
            ImageFormat::Avif => core::ImageFormat::Avif,
            ImageFormat::Png => core::ImageFormat::Png,
            ImageFormat::Original => core::ImageFormat::Original,
        }
    }
}

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum SizeBy {
    Width,
    Height,
    LongEdge,
}

#[derive(Deserialize, Clone)]
pub struct CropSetting {
    pub ratio_width: u32,
    pub ratio_height: u32,
    pub width: u32,
    pub label: Option<String>,
}

#[derive(Deserialize, Clone)]
pub struct CropBox {
    pub path: String,
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

#[derive(Deserialize, Clone)]
pub struct ImageSettings {
    pub format: ImageFormat,
    pub quality: u8,
    pub lossless: bool,
    pub target_bytes: Option<u64>,
    pub sizes: Vec<u32>,
    pub size_by: SizeBy,
    pub crop: Option<CropSetting>,
    pub crop_boxes: Vec<CropBox>,
}

impl From<ImageSettings> for core::ImageSettings {
    fn from(s: ImageSettings) -> Self {
        core::ImageSettings {
            format: s.format.into(),
            quality: s.quality,
            lossless: s.lossless,
            target_bytes: s.target_bytes,
            sizes: s.sizes,
            size_by: match s.size_by {
                SizeBy::Width => core::SizeBy::Width,
                SizeBy::Height => core::SizeBy::Height,
                SizeBy::LongEdge => core::SizeBy::LongEdge,
            },
            crop: s.crop.map(|c| core::CropSetting { ratio_width: c.ratio_width, ratio_height: c.ratio_height, width: c.width, label: c.label }),
            crop_boxes: s.crop_boxes.into_iter().map(|b| core::CropBox { path: b.path, x: b.x, y: b.y, width: b.width, height: b.height }).collect(),
        }
    }
}

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum VideoCodecChoice {
    H264,
    Hevc,
}

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum VideoEncoderChoice {
    Quality,
    Fast,
}

#[derive(Deserialize, Clone, Copy)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum VideoRate {
    High,
    Medium,
    Low,
    TargetBytes { bytes: u64 },
}

#[derive(Deserialize, Clone)]
pub struct VideoCropBox {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

#[derive(Deserialize, Clone, Copy)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum FramePick {
    PerSecond { count: f64 },
    EverySeconds { seconds: f64 },
    Total { count: u32 },
    All,
}

#[derive(Deserialize, Clone)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum VideoOutput {
    Mp4,
    Mute,
    Gif { fps: f64, width: u32, quality: u8 },
    Frames { pick: FramePick, width: Option<u32>, format: ImageFormat, quality: u8, zip: bool, contact_sheet: bool },
    Live { apple: bool, android: bool, cover_at: f64, max_long_edge: u32 },
}

#[derive(Deserialize, Clone)]
pub struct VideoSettings {
    pub codec: VideoCodecChoice,
    pub encoder: VideoEncoderChoice,
    pub rate: VideoRate,
    pub max_short_edge: Option<u32>,
    pub keep_audio: bool,
    pub trim_start: Option<f64>,
    pub trim_end: Option<f64>,
    pub crop: Option<VideoCropBox>,
    pub hdr_to_sdr: bool,
    pub max_fps: Option<f64>,
    pub output: VideoOutput,
    pub cover_at: Option<f64>,
}

impl From<VideoSettings> for core::VideoSettings {
    fn from(s: VideoSettings) -> Self {
        core::VideoSettings {
            codec: match s.codec {
                VideoCodecChoice::H264 => core::VideoCodecChoice::H264,
                VideoCodecChoice::Hevc => core::VideoCodecChoice::Hevc,
            },
            encoder: match s.encoder {
                VideoEncoderChoice::Quality => core::VideoEncoderChoice::Quality,
                VideoEncoderChoice::Fast => core::VideoEncoderChoice::Fast,
            },
            rate: match s.rate {
                VideoRate::High => core::VideoRate::High,
                VideoRate::Medium => core::VideoRate::Medium,
                VideoRate::Low => core::VideoRate::Low,
                VideoRate::TargetBytes { bytes } => core::VideoRate::TargetBytes { bytes },
            },
            max_short_edge: s.max_short_edge,
            keep_audio: s.keep_audio,
            trim_start: s.trim_start,
            trim_end: s.trim_end,
            crop: s.crop.map(|c| core::VideoCropBox { x: c.x, y: c.y, width: c.width, height: c.height }),
            hdr_to_sdr: s.hdr_to_sdr,
            max_fps: s.max_fps,
            output: match s.output {
                VideoOutput::Mp4 => core::VideoOutput::Mp4,
                VideoOutput::Mute => core::VideoOutput::Mute,
                VideoOutput::Gif { fps, width, quality } => core::VideoOutput::Gif { fps, width, quality },
                VideoOutput::Frames { pick, width, format, quality, zip, contact_sheet } => core::VideoOutput::Frames {
                    pick: match pick {
                        FramePick::PerSecond { count } => core::FramePick::PerSecond { count },
                        FramePick::EverySeconds { seconds } => core::FramePick::EverySeconds { seconds },
                        FramePick::Total { count } => core::FramePick::Total { count },
                        FramePick::All => core::FramePick::All,
                    },
                    width,
                    format: format.into(),
                    quality,
                    zip,
                    contact_sheet,
                },
                VideoOutput::Live { apple, android, cover_at, max_long_edge } => core::VideoOutput::Live { apple, android, cover_at, max_long_edge },
            },
            cover_at: s.cover_at,
        }
    }
}

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum PdfLevel {
    Screen,
    Email,
    Print,
}

#[derive(Deserialize, Clone, Copy)]
#[serde(rename_all = "snake_case")]
pub enum PdfPageMode {
    FitImage,
    A4,
}

#[derive(Deserialize, Clone, Copy)]
pub struct PdfPageRef {
    pub file: u32,
    pub page: u32,
}

#[derive(Deserialize, Clone)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum PdfOperation {
    Compress { level: PdfLevel, target_bytes: Option<u64>, subset_fonts: bool, grayscale: bool },
    Assemble { pages: Vec<PdfPageRef> },
    Merge,
    Split { every: u32 },
    ToImages { width: u32, format: ImageFormat, quality: u8, long_image: bool },
    FromImages { page_mode: PdfPageMode },
}

#[derive(Deserialize, Clone)]
pub struct PdfSettings {
    pub operation: PdfOperation,
    pub strip_metadata: bool,
    pub password: Option<String>,
}

impl PdfSettings {
    /// 图片转 PDF 的输入是图片，其余是 PDF（与引擎里的规划一致）。
    pub fn input_kind(&self) -> core::Kind {
        if matches!(self.operation, PdfOperation::FromImages { .. }) { core::Kind::Image } else { core::Kind::Pdf }
    }
}

impl From<PdfSettings> for core::PdfSettings {
    fn from(s: PdfSettings) -> Self {
        core::PdfSettings {
            operation: match s.operation {
                PdfOperation::Compress { level, target_bytes, subset_fonts, grayscale } => core::PdfOperation::Compress {
                    level: match level {
                        PdfLevel::Screen => core::PdfLevel::Screen,
                        PdfLevel::Email => core::PdfLevel::Email,
                        PdfLevel::Print => core::PdfLevel::Print,
                    },
                    target_bytes,
                    subset_fonts,
                    grayscale,
                },
                PdfOperation::Assemble { pages } => core::PdfOperation::Assemble { pages: pages.into_iter().map(|p| core::PdfPageRef { file: p.file, page: p.page }).collect() },
                PdfOperation::Merge => core::PdfOperation::Merge,
                PdfOperation::Split { every } => core::PdfOperation::Split { every },
                PdfOperation::ToImages { width, format, quality, long_image } => core::PdfOperation::ToImages { width, format: format.into(), quality, long_image },
                PdfOperation::FromImages { page_mode } => core::PdfOperation::FromImages {
                    page_mode: match page_mode {
                        PdfPageMode::FitImage => core::PdfPageMode::FitImage,
                        PdfPageMode::A4 => core::PdfPageMode::A4,
                    },
                },
            },
            strip_metadata: s.strip_metadata,
            password: s.password,
        }
    }
}

// ---------- 回给应用的结果 ----------

#[derive(Serialize)]
pub struct Output {
    pub path: String,
    pub bytes: u64,
    pub width: u32,
    pub height: u32,
}

#[derive(Serialize)]
pub struct JobItemResult {
    pub index: u32,
    pub source: String,
    pub before_bytes: u64,
    pub outputs: Vec<Output>,
    /// done / kept_original / failed / cancelled
    pub state: &'static str,
    pub message: Option<String>,
}

impl From<core::JobItemResult> for JobItemResult {
    fn from(r: core::JobItemResult) -> Self {
        JobItemResult {
            index: r.index,
            source: r.source,
            before_bytes: r.before_bytes,
            outputs: r.outputs.into_iter().map(|o| Output { path: o.path, bytes: o.bytes, width: o.width, height: o.height }).collect(),
            state: match r.state {
                core::ItemState::Done => "done",
                core::ItemState::KeptOriginal => "kept_original",
                core::ItemState::Failed => "failed",
                core::ItemState::Cancelled => "cancelled",
            },
            message: r.message,
        }
    }
}

#[derive(Serialize)]
pub struct PlanSummary {
    pub files: u32,
    pub skipped: Vec<String>,
}

#[derive(Serialize)]
pub struct PlanTarget {
    pub source: String,
    pub out_dir: String,
}

#[derive(Serialize)]
pub struct PlanTargets {
    pub items: Vec<PlanTarget>,
    pub created_roots: Vec<String>,
}

#[derive(Serialize)]
pub struct VideoMeta {
    pub duration: f64,
    pub width: u32,
    pub height: u32,
    pub fps: f64,
    pub codec: String,
    pub bitrate: u64,
    pub has_audio: bool,
    pub hdr: bool,
    pub bytes: u64,
}

#[derive(Serialize)]
pub struct PdfMeta {
    pub pages: u32,
    pub needs_password: bool,
    pub bytes: u64,
}

#[derive(Serialize)]
pub struct ClipboardImage {
    pub png: bool,
    pub width: u32,
    pub height: u32,
    pub before_bytes: u64,
    /// 压好的图片字节，base64（标准字母表、带补位）。
    pub bytes: String,
}

/// 标准 base64（RFC 4648，带 `=` 补位）。只有剪贴板图片用到，不为它多引一个依赖。
pub fn base64(data: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(data.len().div_ceil(3) * 4);
    for chunk in data.chunks(3) {
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        out.push(ALPHABET[(n >> 18) as usize & 63] as char);
        out.push(ALPHABET[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 { ALPHABET[(n >> 6) as usize & 63] as char } else { '=' });
        out.push(if chunk.len() > 2 { ALPHABET[n as usize & 63] as char } else { '=' });
    }
    out
}

#[cfg(test)]
mod tests {
    #[test]
    fn base64_matches_rfc4648() {
        for (raw, enc) in [("", ""), ("f", "Zg=="), ("fo", "Zm8="), ("foo", "Zm9v"), ("foob", "Zm9vYg=="), ("fooba", "Zm9vYmE="), ("foobar", "Zm9vYmFy")] {
            assert_eq!(super::base64(raw.as_bytes()), enc);
        }
    }
}
