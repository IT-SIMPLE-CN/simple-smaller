//! 对外的类型：平台注入、共用设置、图片 / 视频 / PDF 设置、文件信息。

use std::path::PathBuf;

use crate::image::{CropRect, ImageRecipe, OutputFormat, SizeMode};
use crate::jobs::{ImageCrop, ImageJob, ItemResult, ItemStatus, MediaKind, OutputPlace};
use crate::naming::NamingRule;
use crate::video;

// ---------- 平台注入 ----------

#[derive(Debug, Clone, uniffi::Record)]
pub struct RawImage {
    pub width: u32,
    pub height: u32,
    /// RGBA8，已转成 sRGB、按方向转正。
    pub rgba: Vec<u8>,
    pub has_alpha: bool,
}

/// 平台解码（mac：ImageIO）。不认识时返回 None。
#[uniffi::export(with_foreign)]
pub trait PlatformDecoder: Send + Sync {
    fn decode(&self, path: String) -> Option<RawImage>;
}

/// 任务回调，可能来自后台线程。
#[uniffi::export(with_foreign)]
pub trait JobListener: Send + Sync {
    /// 单个文件的进度（0–1），视频与 PDF 用。
    fn on_progress(&self, index: u32, fraction: f64);
    fn on_item(&self, result: JobItemResult);
    fn on_finished(&self);
}

// ---------- 共用设置 ----------

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum Kind {
    Image,
    Video,
    Pdf,
}

impl From<Kind> for MediaKind {
    fn from(k: Kind) -> Self {
        match k {
            Kind::Image => MediaKind::Image,
            Kind::Video => MediaKind::Video,
            Kind::Pdf => MediaKind::Pdf,
        }
    }
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct Naming {
    pub template: String,
    pub lowercase: bool,
    pub pinyin: bool,
    pub hyphenate: bool,
    /// 由界面按本地时区给出，如 20261016。
    pub date: String,
}

impl Naming {
    pub(super) fn rule(&self) -> NamingRule {
        NamingRule { template: self.template.clone(), lowercase: self.lowercase, pinyin: self.pinyin, hyphenate: self.hyphenate }
    }
}

#[derive(Debug, Clone, uniffi::Enum)]
pub enum Place {
    BesideSource { folder_suffix: String },
    Folder { path: String },
}

impl From<&Place> for OutputPlace {
    fn from(p: &Place) -> Self {
        match p {
            Place::BesideSource { folder_suffix } => OutputPlace::BesideSource { folder_suffix: folder_suffix.clone() },
            Place::Folder { path } => OutputPlace::Folder(path.into()),
        }
    }
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct Output {
    pub path: String,
    pub bytes: u64,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum ItemState {
    Done,
    KeptOriginal,
    Failed,
    Cancelled,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct JobItemResult {
    pub index: u32,
    pub source: String,
    pub before_bytes: u64,
    pub outputs: Vec<Output>,
    pub state: ItemState,
    pub message: Option<String>,
}

impl From<&ItemResult> for JobItemResult {
    fn from(r: &ItemResult) -> Self {
        let (state, message) = match &r.status {
            ItemStatus::Done => (ItemState::Done, None),
            ItemStatus::KeptOriginal => (ItemState::KeptOriginal, None),
            ItemStatus::Failed(m) => (ItemState::Failed, Some(m.clone())),
            ItemStatus::Cancelled => (ItemState::Cancelled, None),
        };
        JobItemResult {
            index: r.index as u32,
            source: r.source.to_string_lossy().into_owned(),
            before_bytes: r.before_bytes,
            outputs: r.outputs.iter().map(|o| Output { path: o.path.to_string_lossy().into_owned(), bytes: o.bytes, width: o.width, height: o.height }).collect(),
            state,
            message,
        }
    }
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct PlanSummary {
    pub files: u32,
    pub skipped: Vec<String>,
}

// ---------- 图片设置 ----------

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, uniffi::Enum)]
pub enum ImageFormat {
    Jpeg,
    Webp,
    Avif,
    Png,
    Original,
}

impl From<ImageFormat> for OutputFormat {
    fn from(f: ImageFormat) -> Self {
        match f {
            ImageFormat::Jpeg => OutputFormat::Jpeg,
            ImageFormat::Webp => OutputFormat::Webp,
            ImageFormat::Avif => OutputFormat::Avif,
            ImageFormat::Png => OutputFormat::Png,
            ImageFormat::Original => OutputFormat::Original,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, uniffi::Enum)]
pub enum SizeBy {
    Width,
    Height,
    LongEdge,
}

impl From<SizeBy> for SizeMode {
    fn from(s: SizeBy) -> Self {
        match s {
            SizeBy::Width => SizeMode::Width,
            SizeBy::Height => SizeMode::Height,
            SizeBy::LongEdge => SizeMode::LongEdge,
        }
    }
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct CropSetting {
    /// 比例的宽、高份数，如 3、4。
    pub ratio_width: u32,
    pub ratio_height: u32,
    /// 输出宽度，高按比例。
    pub width: u32,
    pub label: Option<String>,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct ClipboardImage {
    /// true 为 PNG，否则 JPEG。
    pub png: bool,
    pub width: u32,
    pub height: u32,
    pub before_bytes: u64,
    pub bytes: Vec<u8>,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct PlanTarget {
    pub source: String,
    pub out_dir: String,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct PlanTargets {
    pub items: Vec<PlanTarget>,
    pub created_roots: Vec<String>,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct CropBox {
    pub path: String,
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct ImageSettings {
    pub format: ImageFormat,
    pub quality: u8,
    pub lossless: bool,
    pub target_bytes: Option<u64>,
    /// 尺寸档位；空表示不缩放，多档时一张出多个文件。
    pub sizes: Vec<u32>,
    pub size_by: SizeBy,
    pub crop: Option<CropSetting>,
    pub crop_boxes: Vec<CropBox>,
}

impl ImageSettings {
    pub(super) fn job(&self, naming: &Naming) -> ImageJob {
        ImageJob {
            recipe: ImageRecipe { format: self.format.into(), quality: self.quality, lossless: self.lossless, resize: None, target_bytes: self.target_bytes },
            sizes: self.sizes.clone(),
            size_mode: self.size_by.into(),
            // 比例份数为 0 表示自由比例。
            crop: self.crop.as_ref().map(|c| {
                let free = c.ratio_width == 0 || c.ratio_height == 0;
                ImageCrop {
                    aspect: (!free).then(|| c.ratio_width as f64 / c.ratio_height as f64),
                    width: c.width,
                    label: c.label.clone().or_else(|| (!free).then(|| format!("{}x{}", c.ratio_width, c.ratio_height))),
                }
            }),
            crop_overrides: self
                .crop_boxes
                .iter()
                .map(|b| (PathBuf::from(&b.path), CropRect { x: b.x, y: b.y, width: b.width, height: b.height }))
                .collect(),
            naming: naming.rule(),
            date: naming.date.clone(),
        }
    }

}

// ---------- 视频设置 ----------

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum VideoCodecChoice {
    H264,
    Hevc,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum VideoEncoderChoice {
    Quality,
    Fast,
}

#[derive(Debug, Clone, Copy, PartialEq, uniffi::Enum)]
pub enum VideoRate {
    High,
    Medium,
    Low,
    TargetBytes { bytes: u64 },
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct VideoCropBox {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, Copy, PartialEq, uniffi::Enum)]
pub enum FramePick {
    PerSecond { count: f64 },
    EverySeconds { seconds: f64 },
    Total { count: u32 },
    All,
}

#[derive(Debug, Clone, uniffi::Enum)]
pub enum VideoOutput {
    Mp4,
    /// 只去声音，视频流原样复制。
    Mute,
    Gif { fps: f64, width: u32, quality: u8 },
    Frames { pick: FramePick, width: Option<u32>, format: ImageFormat, quality: u8, zip: bool, contact_sheet: bool },
}

#[derive(Debug, Clone, uniffi::Record)]
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
    /// 同时导出封面（JPG）：取这一秒的画面；None 不导出。
    pub cover_at: Option<f64>,
}

impl VideoSettings {
    pub(super) fn trim(&self) -> Option<(f64, f64)> {
        match (self.trim_start, self.trim_end) {
            (Some(s), Some(e)) if e > s => Some((s, e)),
            _ => None,
        }
    }

    pub(super) fn crop(&self) -> Option<video::Crop> {
        self.crop.as_ref().map(|c| video::Crop { x: c.x, y: c.y, width: c.width, height: c.height })
    }

    pub(super) fn recipe(&self) -> video::VideoRecipe {
        video::VideoRecipe {
            codec: match self.codec {
                VideoCodecChoice::H264 => video::VideoCodec::H264,
                VideoCodecChoice::Hevc => video::VideoCodec::Hevc,
            },
            encoder: match self.encoder {
                VideoEncoderChoice::Quality => video::EncoderChoice::Quality,
                VideoEncoderChoice::Fast => video::EncoderChoice::Fast,
            },
            rate: match self.rate {
                VideoRate::High => video::Rate::High,
                VideoRate::Medium => video::Rate::Medium,
                VideoRate::Low => video::Rate::Low,
                VideoRate::TargetBytes { bytes } => video::Rate::TargetBytes(bytes),
            },
            max_short_edge: self.max_short_edge,
            keep_audio: self.keep_audio,
            trim: self.trim(),
            crop: self.crop(),
            hdr_to_sdr: self.hdr_to_sdr,
            max_fps: self.max_fps,
        }
    }
}

#[derive(Debug, Clone, uniffi::Record)]
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

// ---------- PDF 设置 ----------

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum PdfLevel {
    Screen,
    Email,
    Print,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum PdfPageMode {
    FitImage,
    A4,
}

#[derive(Debug, Clone, uniffi::Enum)]
pub enum PdfOperation {
    Compress { level: PdfLevel, target_bytes: Option<u64>, subset_fonts: bool, grayscale: bool },
    /// 从所有输入里按给定顺序挑页拼成一个文件（合并时排序、删页）。
    Assemble { pages: Vec<PdfPageRef> },
    /// 所有输入按顺序合成一个文件。
    Merge,
    /// 每 `every` 页拆一个文件。
    Split { every: u32 },
    ToImages { width: u32, format: ImageFormat, quality: u8, long_image: bool },
    /// 输入是图片，合成一个 PDF。
    FromImages { page_mode: PdfPageMode },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Record)]
pub struct PdfPageRef {
    /// 第几个输入文件（从 0 起）。
    pub file: u32,
    pub page: u32,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct PdfSettings {
    pub operation: PdfOperation,
    pub strip_metadata: bool,
    /// 输出的 PDF 加打开密码（AES-256）；None 不加。
    pub password: Option<String>,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct PdfMeta {
    pub pages: u32,
    pub needs_password: bool,
    pub bytes: u64,
}
