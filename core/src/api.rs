//! 对外接口（UniFFI）：Swift / C# 端只通过这里调用引擎。
//!
//! - 配方：图片、视频、PDF 各一套设置，对应界面上的选项。
//! - 任务：后台线程执行，逐个文件回调进度与结果，可取消、可整批撤销。
//! - 试压：按当前配方压一次返回体积，按「文件 + 修改时间 + 参数」缓存。
//! - 平台解码：mac 端注入 ImageIO，用来读 HEIC 等引擎不认识的格式。

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use crate::image::{self as img, CropRect, ImageRecipe, OutputFormat, Picture, SizeMode, SourceFormat};
use crate::jobs::{self, ImageCrop, ImageJob, ItemResult, ItemStatus, MediaKind, OutputFile, OutputPlace, Plan};
use crate::naming::{self, NameVars, NamingRule};
use crate::{EngineError, pdf, video};

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
#[uniffi::export(foreign)]
pub trait PlatformDecoder: Send + Sync {
    fn decode(&self, path: String) -> Option<RawImage>;
}

/// 任务回调，可能来自后台线程。
#[uniffi::export(foreign)]
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
    fn rule(&self) -> NamingRule {
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

fn format_back(f: OutputFormat) -> ImageFormat {
    match f {
        OutputFormat::Jpeg => ImageFormat::Jpeg,
        OutputFormat::Webp => ImageFormat::Webp,
        OutputFormat::Avif => ImageFormat::Avif,
        OutputFormat::Png => ImageFormat::Png,
        OutputFormat::Original => ImageFormat::Original,
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
    fn job(&self, naming: &Naming) -> ImageJob {
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

    fn cache_key(&self, path: &str, modified: u64) -> String {
        format!("{path}|{modified}|{:?}|{}|{}|{:?}|{:?}|{:?}|{:?}", self.format, self.quality, self.lossless, self.target_bytes, self.sizes, self.size_by, self.crop.as_ref().map(|c| (c.ratio_width, c.ratio_height, c.width)))
    }
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct ImagePreview {
    pub bytes: u64,
    pub width: u32,
    pub height: u32,
    pub format: ImageFormat,
    pub before_bytes: u64,
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct ImageMeta {
    pub width: u32,
    pub height: u32,
    pub has_alpha: bool,
    /// 截图、图文这类颜色少的画面。
    pub graphic: bool,
    pub bytes: u64,
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
    fn trim(&self) -> Option<(f64, f64)> {
        match (self.trim_start, self.trim_end) {
            (Some(s), Some(e)) if e > s => Some((s, e)),
            _ => None,
        }
    }

    fn crop(&self) -> Option<video::Crop> {
        self.crop.as_ref().map(|c| video::Crop { x: c.x, y: c.y, width: c.width, height: c.height })
    }

    fn recipe(&self) -> video::VideoRecipe {
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
}

#[derive(Debug, Clone, uniffi::Record)]
pub struct PdfMeta {
    pub pages: u32,
    pub needs_password: bool,
    pub bytes: u64,
}

// ---------- 任务 ----------

#[derive(uniffi::Object)]
pub struct Job {
    cancel: AtomicBool,
    finished: AtomicBool,
    results: Mutex<Vec<ItemResult>>,
    created_roots: Mutex<Vec<PathBuf>>,
}

#[uniffi::export]
impl Job {
    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::Relaxed);
    }

    pub fn is_finished(&self) -> bool {
        self.finished.load(Ordering::Relaxed)
    }

    /// 把这一批的产出移到废纸篓。
    pub fn undo(&self) -> Result<(), EngineError> {
        let results = self.results.lock().unwrap().clone();
        let roots = self.created_roots.lock().unwrap().clone();
        jobs::undo(&results, &roots).map_err(|message| EngineError::Failed { message })
    }
}

impl Job {
    fn new() -> Arc<Self> {
        Arc::new(Job { cancel: AtomicBool::new(false), finished: AtomicBool::new(false), results: Mutex::new(Vec::new()), created_roots: Mutex::new(Vec::new()) })
    }

    fn record(&self, listener: &Arc<dyn JobListener>, r: ItemResult) {
        listener.on_item(JobItemResult::from(&r));
        self.results.lock().unwrap().push(r);
    }

    fn finish(&self, listener: &Arc<dyn JobListener>) {
        self.finished.store(true, Ordering::Relaxed);
        listener.on_finished();
    }
}

// ---------- 引擎 ----------

const PREVIEW_CACHE_LIMIT: usize = 512;

#[derive(uniffi::Object)]
pub struct Engine {
    decoder: Option<Arc<dyn PlatformDecoder>>,
    previews: Mutex<HashMap<String, ImagePreview>>,
}

fn modified_secs(path: &Path) -> u64 {
    std::fs::metadata(path)
        .and_then(|m| m.modified())
        .ok()
        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn file_bytes(path: &Path) -> u64 {
    std::fs::metadata(path).map(|m| m.len()).unwrap_or(0)
}

fn failed(index: usize, source: &Path, message: String) -> ItemResult {
    ItemResult { index, source: source.to_path_buf(), before_bytes: file_bytes(source), outputs: Vec::new(), status: ItemStatus::Failed(message) }
}

fn stem(path: &Path) -> String {
    path.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default()
}

fn ext_for(format: OutputFormat) -> &'static str {
    match format {
        OutputFormat::Jpeg => "jpg",
        OutputFormat::Webp => "webp",
        OutputFormat::Avif => "avif",
        OutputFormat::Png | OutputFormat::Original => "png",
    }
}

#[uniffi::export]
impl Engine {
    #[uniffi::constructor]
    pub fn new(decoder: Option<Arc<dyn PlatformDecoder>>) -> Arc<Self> {
        Arc::new(Engine { decoder, previews: Mutex::new(HashMap::new()) })
    }

    pub fn version(&self) -> String {
        env!("CARGO_PKG_VERSION").to_string()
    }

    /// 展开输入（含文件夹）后会处理多少个文件、跳过哪些。
    pub fn plan(&self, inputs: Vec<String>, kind: Kind, place: Place) -> PlanSummary {
        let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
        let p = jobs::plan(&inputs, kind.into(), &(&place).into());
        PlanSummary { files: p.items.len() as u32, skipped: p.skipped.iter().map(|(path, why)| format!("{}：{why}", path.display())).collect() }
    }

    /// 展开输入后实际会处理的文件（与任务用同一套规则）。
    pub fn list_files(&self, inputs: Vec<String>, kind: Kind) -> Vec<String> {
        let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
        let p = jobs::plan(&inputs, kind.into(), &OutputPlace::BesideSource { folder_suffix: String::new() });
        p.items.iter().map(|i| i.source.to_string_lossy().into_owned()).collect()
    }

    pub fn image_meta(&self, path: String) -> Result<ImageMeta, EngineError> {
        let p = self.decode(Path::new(&path)).map_err(|message| EngineError::Unsupported { message })?;
        Ok(ImageMeta { width: p.width, height: p.height, has_alpha: p.has_alpha, graphic: p.kind == img::ContentKind::Graphic, bytes: file_bytes(Path::new(&path)) })
    }

    /// 试压：按配方压一次第一档（或裁切后的尺寸），只返回体积，不写文件。
    pub fn preview_image(&self, path: String, settings: ImageSettings) -> Result<ImagePreview, EngineError> {
        let source = Path::new(&path);
        let key = settings.cache_key(&path, modified_secs(source));
        if let Some(hit) = self.previews.lock().unwrap().get(&key) {
            return Ok(hit.clone());
        }
        let mut picture = self.decode(source).map_err(|message| EngineError::Unsupported { message })?;
        let job = settings.job(&Naming { template: String::new(), lowercase: false, pinyin: false, hyphenate: false, date: String::new() });
        let mut size = job.sizes.first().copied();
        let mut mode = job.size_mode;
        if let Some(c) = &job.crop {
            let rect = job.crop_overrides.get(source).copied().unwrap_or_else(|| c.default_rect(picture.width, picture.height));
            picture = img::crop(&picture, rect);
            size = Some(c.width);
            mode = SizeMode::Width;
        }
        let mut recipe = job.recipe.clone();
        recipe.resize = size.map(|s| (mode, s));
        let encoded = img::render(&picture, &recipe).map_err(|e| EngineError::Failed { message: e.to_string() })?;
        let preview = ImagePreview { bytes: encoded.bytes.len() as u64, width: encoded.width, height: encoded.height, format: format_back(encoded.format), before_bytes: file_bytes(source) };
        let mut cache = self.previews.lock().unwrap();
        if cache.len() >= PREVIEW_CACHE_LIMIT {
            cache.clear();
        }
        cache.insert(key, preview.clone());
        Ok(preview)
    }

    pub fn video_meta(&self, path: String) -> Result<VideoMeta, EngineError> {
        let tools = video::Tools::locate().map_err(|e| EngineError::Failed { message: e.to_string() })?;
        let i = video::probe(&tools, Path::new(&path)).map_err(|e| EngineError::Unsupported { message: e.to_string() })?;
        Ok(VideoMeta { duration: i.duration, width: i.width, height: i.height, fps: i.fps, codec: i.codec, bitrate: i.bitrate, has_audio: i.has_audio, hdr: i.hdr, bytes: file_bytes(Path::new(&path)) })
    }

    pub fn pdf_meta(&self, path: String) -> Result<PdfMeta, EngineError> {
        let i = pdf::info(Path::new(&path)).map_err(|e| EngineError::Unsupported { message: e.to_string() })?;
        Ok(PdfMeta { pages: i.pages, needs_password: i.needs_password, bytes: file_bytes(Path::new(&path)) })
    }

    /// 预估 PDF 压缩后的体积：压到临时文件再删掉。返回 None 表示会保留原文件（已经很小）。
    pub fn estimate_pdf(&self, path: String, level: PdfLevel, target_bytes: Option<u64>, subset_fonts: bool, grayscale: bool) -> Result<Option<u64>, EngineError> {
        let level = match level {
            PdfLevel::Screen => pdf::CompressLevel::SCREEN,
            PdfLevel::Email => pdf::CompressLevel::EMAIL,
            PdfLevel::Print => pdf::CompressLevel::PRINT,
        };
        let tmp = std::env::temp_dir().join(format!("smaller-estimate-{}-{}.pdf", std::process::id(), modified_secs(Path::new(&path))));
        let recipe = pdf::PdfRecipe { level, target_bytes, subset_fonts, strip_metadata: true, grayscale };
        let r = pdf::compress(Path::new(&path), &tmp, &recipe).map_err(|e| EngineError::Failed { message: e.to_string() })?;
        let _ = std::fs::remove_file(&tmp);
        Ok(match r {
            pdf::CompressOutcome::Written { bytes, .. } => Some(bytes),
            pdf::CompressOutcome::KeptOriginal => None,
        })
    }

    /// 渲染 PDF 某一页（缩略图用）。
    pub fn pdf_page_image(&self, path: String, page: u32, width: u32) -> Result<RawImage, EngineError> {
        let p = pdf::render_page(Path::new(&path), page, width).map_err(|e| EngineError::Failed { message: e.to_string() })?;
        Ok(RawImage { width: p.width, height: p.height, rgba: p.rgba, has_alpha: false })
    }

    pub fn run_images(self: Arc<Self>, inputs: Vec<String>, place: Place, naming: Naming, settings: ImageSettings, listener: Arc<dyn JobListener>) -> Arc<Job> {
        let job = Job::new();
        let handle = job.clone();
        std::thread::spawn(move || {
            let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
            let plan = jobs::plan(&inputs, MediaKind::Image, &(&place).into());
            *job.created_roots.lock().unwrap() = plan.created_roots.clone();
            let image_job = settings.job(&naming);
            let decoder = |p: &Path| self.decode(p);
            // 开始处理某张时报 0 进度，界面据此把这一行标成「处理中」。
            let on_start = |i: usize| listener.on_progress(i as u32, 0.0);
            let on_result = |r: &ItemResult| listener.on_item(JobItemResult::from(r));
            let results = jobs::run_images(&plan, &image_job, &decoder, &on_start, &on_result, &job.cancel);
            *job.results.lock().unwrap() = results;
            job.finish(&listener);
        });
        handle
    }

    pub fn run_videos(self: Arc<Self>, inputs: Vec<String>, place: Place, naming: Naming, settings: VideoSettings, listener: Arc<dyn JobListener>) -> Arc<Job> {
        let job = Job::new();
        let handle = job.clone();
        std::thread::spawn(move || {
            let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
            let plan = jobs::plan(&inputs, MediaKind::Video, &(&place).into());
            *job.created_roots.lock().unwrap() = plan.created_roots.clone();
            let tools = video::Tools::locate();
            let mut reserved = std::collections::HashSet::new();
            for (i, item) in plan.items.iter().enumerate() {
                if job.cancel.load(Ordering::Relaxed) {
                    job.record(&listener, ItemResult { index: i, source: item.source.clone(), before_bytes: 0, outputs: vec![], status: ItemStatus::Cancelled });
                    continue;
                }
                let r = match &tools {
                    Ok(tools) => self.video_item(tools, i, item, &naming, &settings, &mut reserved, &listener, &job.cancel),
                    Err(e) => failed(i, &item.source, e.to_string()),
                };
                job.record(&listener, r);
            }
            job.finish(&listener);
        });
        handle
    }

    pub fn run_pdfs(self: Arc<Self>, inputs: Vec<String>, place: Place, naming: Naming, settings: PdfSettings, listener: Arc<dyn JobListener>) -> Arc<Job> {
        let job = Job::new();
        let handle = job.clone();
        std::thread::spawn(move || {
            let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
            let kind = if matches!(settings.operation, PdfOperation::FromImages { .. }) { MediaKind::Image } else { MediaKind::Pdf };
            let plan = jobs::plan(&inputs, kind, &(&place).into());
            *job.created_roots.lock().unwrap() = plan.created_roots.clone();
            for r in self.pdf_items(&plan, &naming, &settings, &listener, &job.cancel) {
                job.record(&listener, r);
            }
            job.finish(&listener);
        });
        handle
    }
}

impl Engine {
    /// HEIC 这类引擎不认识的格式交给平台；引擎解码失败时也再试一次平台。
    fn decode(&self, path: &Path) -> Result<Picture, String> {
        let ext = path.extension().map(|e| e.to_string_lossy().to_lowercase()).unwrap_or_default();
        let platform = |path: &Path| -> Option<Picture> {
            let d = self.decoder.as_ref()?;
            let raw = d.decode(path.to_string_lossy().into_owned())?;
            (raw.rgba.len() == raw.width as usize * raw.height as usize * 4)
                .then(|| img::from_rgba(raw.width, raw.height, raw.rgba, raw.has_alpha, SourceFormat::Png))
        };
        if matches!(ext.as_str(), "heic" | "heif") {
            return platform(path).ok_or_else(|| "这台电脑读不了 HEIC".to_string());
        }
        match img::load(path) {
            Ok(p) => Ok(p),
            Err(e) => platform(path).ok_or_else(|| e.to_string()),
        }
    }

    fn name(&self, naming: &Naming, source: &Path, folder: &str, index: usize, width: u32, height: u32) -> String {
        let vars = NameVars {
            name: stem(source),
            width,
            height,
            ratio: (width > 0 && height > 0).then(|| naming::ratio_label(width, height)),
            date: naming.date.clone(),
            index: index + 1,
            folder: folder.to_string(),
        };
        naming::render(&naming.rule(), &vars)
    }

    #[allow(clippy::too_many_arguments)]
    fn video_item(
        &self,
        tools: &video::Tools,
        index: usize,
        item: &jobs::PlannedItem,
        naming: &Naming,
        settings: &VideoSettings,
        reserved: &mut std::collections::HashSet<PathBuf>,
        listener: &Arc<dyn JobListener>,
        cancel: &AtomicBool,
    ) -> ItemResult {
        let before = file_bytes(&item.source);
        let info = match video::probe(tools, &item.source) {
            Ok(i) => i,
            Err(e) => return failed(index, &item.source, e.to_string()),
        };
        let mut progress = |f: f64| listener.on_progress(index as u32, f);
        let mut result = ItemResult { index, source: item.source.clone(), before_bytes: before, outputs: vec![], status: ItemStatus::Done };
        let stem_name = self.name(naming, &item.source, &item.folder, index, 0, 0);
        let frame_options = |pick: video::FrameSelect, width: Option<u32>| video::FrameOptions { select: pick, width, trim: settings.trim(), crop: settings.crop(), hdr_to_sdr: settings.hdr_to_sdr };
        let outcome = match &settings.output {
            VideoOutput::Mp4 => {
                let path = naming::unique_path(&item.out_dir, &stem_name, "mp4", reserved);
                let _ = std::fs::create_dir_all(&item.out_dir);
                video::transcode(tools, &item.source, &path, &info, &settings.recipe(), &mut progress, cancel).map(|_| {
                    let bytes = file_bytes(&path);
                    let unchanged = settings.trim().is_none() && settings.crop.is_none() && settings.max_short_edge.is_none_or(|m| m >= info.width.min(info.height));
                    if unchanged && bytes >= before {
                        let _ = std::fs::remove_file(&path);
                        result.status = ItemStatus::KeptOriginal;
                    } else if let Ok(out) = video::probe(tools, &path) {
                        result.outputs.push(OutputFile { path, bytes, width: out.width, height: out.height });
                    }
                })
            }
            VideoOutput::Mute => {
                let ext = item.source.extension().map(|e| e.to_string_lossy().to_lowercase()).unwrap_or_else(|| "mp4".into());
                let ext = if ext == "mov" || ext == "m4v" { ext } else { "mp4".into() };
                let path = naming::unique_path(&item.out_dir, &stem_name, &ext, reserved);
                let _ = std::fs::create_dir_all(&item.out_dir);
                video::strip_audio(tools, &item.source, &path, &info, &mut progress, cancel).map(|_| {
                    result.outputs.push(OutputFile { bytes: file_bytes(&path), path, width: info.width, height: info.height });
                })
            }
            VideoOutput::Gif { fps, width, quality } => {
                let path = naming::unique_path(&item.out_dir, &stem_name, "gif", reserved);
                let _ = std::fs::create_dir_all(&item.out_dir);
                let options = frame_options(video::FrameSelect::PerSecond(*fps), Some(*width));
                video::make_gif(tools, &item.source, &path, &info, &options, *quality, cancel).map(|_| {
                    result.outputs.push(OutputFile { bytes: file_bytes(&path), path, width: *width, height: 0 });
                })
            }
            VideoOutput::Frames { pick, width, format, quality, zip, contact_sheet } => {
                let select = match *pick {
                    FramePick::PerSecond { count } => video::FrameSelect::PerSecond(count),
                    FramePick::EverySeconds { seconds } => video::FrameSelect::EverySeconds(seconds),
                    FramePick::Total { count } => video::FrameSelect::Total(count),
                    FramePick::All => video::FrameSelect::All,
                };
                let dir = naming::unique_path(&item.out_dir, &format!("{stem_name}_frames"), "", reserved);
                let format: OutputFormat = (*format).into();
                let format = if format == OutputFormat::Original { OutputFormat::Jpeg } else { format };
                let mut outputs = Vec::new();
                let expected = match select {
                    video::FrameSelect::Total(n) => n as f64,
                    video::FrameSelect::All => info.duration * info.fps,
                    other => match other {
                        video::FrameSelect::PerSecond(n) => info.duration * n,
                        video::FrameSelect::EverySeconds(s) => info.duration / s.max(0.01),
                        _ => 1.0,
                    },
                }
                .max(1.0);
                let r = video::extract_frames(tools, &item.source, &info, &frame_options(select, *width), &mut |i, _, picture| {
                    let bytes = img::encode(&picture, format, *quality, false).map_err(|e| video::VideoError::Encode(e.to_string()))?;
                    let path = dir.join(format!("{stem_name}_{:04}.{}", i + 1, ext_for(format)));
                    jobs::write_atomic(&path, &bytes)?;
                    outputs.push(OutputFile { path, bytes: bytes.len() as u64, width: picture.width, height: picture.height });
                    listener.on_progress(index as u32, ((i + 1) as f64 / expected).min(1.0));
                    Ok(())
                }, cancel);
                let r = r.map(|_| ()).and_then(|_| {
                    let files: Vec<PathBuf> = outputs.iter().map(|o| o.path.clone()).collect();
                    if *contact_sheet && !files.is_empty() {
                        // 最多取 48 帧均匀排开。
                        let step = (files.len() / 48).max(1);
                        let picks: Vec<Picture> = files.iter().step_by(step).take(48).filter_map(|f| img::load(f).ok()).collect();
                        if let Some(sheet) = jobs::contact_sheet(&picks, 6, 320) {
                            let bytes = img::encode(&sheet, OutputFormat::Jpeg, 85, false).map_err(|e| video::VideoError::Encode(e.to_string()))?;
                            let path = item.out_dir.join(format!("{stem_name}_总览.jpg"));
                            jobs::write_atomic(&path, &bytes)?;
                            outputs.push(OutputFile { path, bytes: bytes.len() as u64, width: sheet.width, height: sheet.height });
                        }
                    }
                    if *zip && !files.is_empty() {
                        let zip_path = naming::unique_path(&item.out_dir, &format!("{stem_name}_frames"), "zip", reserved);
                        jobs::write_zip(&files, &item.out_dir, &zip_path).map_err(video::VideoError::Encode)?;
                        let _ = std::fs::remove_dir_all(&dir);
                        outputs.retain(|o| !o.path.starts_with(&dir));
                        outputs.push(OutputFile { bytes: file_bytes(&zip_path), path: zip_path, width: 0, height: 0 });
                    }
                    Ok(())
                });
                result.outputs = outputs;
                r
            }
        };
        let outcome = outcome.and_then(|_| {
            let Some(at) = settings.cover_at else { return Ok(()) };
            if !matches!(settings.output, VideoOutput::Mp4 | VideoOutput::Mute) || result.status != ItemStatus::Done {
                return Ok(());
            }
            let picture = video::grab_frame(tools, &item.source, &info, at, settings.crop(), settings.hdr_to_sdr)?;
            let picture = match settings.max_short_edge {
                Some(edge) if edge < picture.width.min(picture.height) => {
                    let mode = if picture.width <= picture.height { SizeMode::Width } else { SizeMode::Height };
                    img::resize(&picture, mode, edge).map_err(|e| video::VideoError::Encode(e.to_string()))?
                }
                _ => picture,
            };
            let bytes = img::encode(&picture, OutputFormat::Jpeg, 88, false).map_err(|e| video::VideoError::Encode(e.to_string()))?;
            let path = naming::unique_path(&item.out_dir, &format!("{stem_name}-cover"), "jpg", reserved);
            jobs::write_atomic(&path, &bytes)?;
            result.outputs.push(OutputFile { path, bytes: bytes.len() as u64, width: picture.width, height: picture.height });
            Ok(())
        });
        if let Err(e) = outcome {
            result.status = match e {
                video::VideoError::Cancelled => ItemStatus::Cancelled,
                other => ItemStatus::Failed(other.to_string()),
            };
        }
        result
    }

    fn pdf_items(&self, plan: &Plan, naming: &Naming, settings: &PdfSettings, listener: &Arc<dyn JobListener>, cancel: &AtomicBool) -> Vec<ItemResult> {
        let mut reserved = std::collections::HashSet::new();
        let mut out = Vec::new();
        let Some(first) = plan.items.first() else { return out };
        match &settings.operation {
            PdfOperation::Merge | PdfOperation::FromImages { .. } | PdfOperation::Assemble { .. } => {
                let sources: Vec<PathBuf> = plan.items.iter().map(|i| i.source.clone()).collect();
                let name = self.name(naming, &first.source, &first.folder, 0, 0, 0);
                let path = naming::unique_path(&first.out_dir, &name, "pdf", &mut reserved);
                let before: u64 = sources.iter().map(|s| file_bytes(s)).sum();
                let r = match &settings.operation {
                    PdfOperation::Merge => pdf::merge(&sources, &path, settings.strip_metadata),
                    PdfOperation::Assemble { pages } => {
                        let refs: Vec<pdf::PageRef> = pages.iter().map(|p| pdf::PageRef { doc: p.file, page: p.page }).collect();
                        pdf::assemble(&sources, &refs, &path, settings.strip_metadata)
                    }
                    PdfOperation::FromImages { page_mode } => {
                        let mode = match page_mode {
                            PdfPageMode::FitImage => pdf::PageMode::FitImage,
                            PdfPageMode::A4 => pdf::PageMode::A4,
                        };
                        pdf::images_to_pdf(&sources, mode, &path, &|p| self.decode(p))
                    }
                    _ => unreachable!(),
                };
                out.push(match r {
                    Ok(()) => ItemResult { index: 0, source: first.source.clone(), before_bytes: before, outputs: vec![OutputFile { bytes: file_bytes(&path), path, width: 0, height: 0 }], status: ItemStatus::Done },
                    Err(e) => failed(0, &first.source, e.to_string()),
                });
            }
            op => {
                for (i, item) in plan.items.iter().enumerate() {
                    if cancel.load(Ordering::Relaxed) {
                        out.push(ItemResult { index: i, source: item.source.clone(), before_bytes: 0, outputs: vec![], status: ItemStatus::Cancelled });
                        continue;
                    }
                    listener.on_progress(i as u32, 0.0);
                    let before = file_bytes(&item.source);
                    let name = self.name(naming, &item.source, &item.folder, i, 0, 0);
                    let mut result = ItemResult { index: i, source: item.source.clone(), before_bytes: before, outputs: vec![], status: ItemStatus::Done };
                    let _ = std::fs::create_dir_all(&item.out_dir);
                    let r: Result<(), String> = match op {
                        PdfOperation::Compress { level, target_bytes, subset_fonts, grayscale } => {
                            let level = match level {
                                PdfLevel::Screen => pdf::CompressLevel::SCREEN,
                                PdfLevel::Email => pdf::CompressLevel::EMAIL,
                                PdfLevel::Print => pdf::CompressLevel::PRINT,
                            };
                            let path = naming::unique_path(&item.out_dir, &name, "pdf", &mut reserved);
                            let recipe = pdf::PdfRecipe { level, target_bytes: *target_bytes, subset_fonts: *subset_fonts, strip_metadata: settings.strip_metadata, grayscale: *grayscale };
                            pdf::compress(&item.source, &path, &recipe).map_err(|e| e.to_string()).map(|o| match o {
                                pdf::CompressOutcome::Written { bytes, .. } => result.outputs.push(OutputFile { path, bytes, width: 0, height: 0 }),
                                pdf::CompressOutcome::KeptOriginal => result.status = ItemStatus::KeptOriginal,
                            })
                        }
                        PdfOperation::Split { every } => {
                            let dir = naming::unique_path(&item.out_dir, &format!("{name}-拆分"), "", &mut reserved);
                            let _ = std::fs::create_dir_all(&dir);
                            pdf::split_every(&item.source, *every, &dir, settings.strip_metadata).map_err(|e| e.to_string()).map(|files| {
                                result.outputs = files.into_iter().map(|path| OutputFile { bytes: file_bytes(&path), path, width: 0, height: 0 }).collect();
                            })
                        }
                        PdfOperation::ToImages { width, format, quality, long_image } => {
                            let format: OutputFormat = (*format).into();
                            let format = if format == OutputFormat::Original { OutputFormat::Jpeg } else { format };
                            // WebP 单张最高 16383 像素，JPEG 65535。
                            let max_h = if format == OutputFormat::Webp { 16_383 } else { 65_000 };
                            let pictures: Result<Vec<Picture>, String> = if *long_image {
                                pdf::render_long_image(&item.source, *width, max_h).map_err(|e| e.to_string())
                            } else {
                                pdf::info(&item.source).map_err(|e| e.to_string()).and_then(|i| {
                                    (0..i.pages).map(|p| pdf::render_page(&item.source, p, *width).map_err(|e| e.to_string())).collect()
                                })
                            };
                            pictures.and_then(|pictures| {
                                let total = pictures.len();
                                // 逐页的多张图放进「原名-图片」文件夹；长图（通常一张）放在旁边。编号按张数补零。
                                let digits = total.to_string().len().max(2);
                                let dir = if !*long_image && total > 1 {
                                    let d = naming::unique_path(&item.out_dir, &format!("{name}-图片"), "", &mut reserved);
                                    let _ = std::fs::create_dir_all(&d);
                                    d
                                } else {
                                    item.out_dir.clone()
                                };
                                for (k, picture) in pictures.into_iter().enumerate() {
                                    let bytes = img::encode(&picture, format, *quality, false).map_err(|e| e.to_string())?;
                                    let stem = if total == 1 { name.clone() } else { format!("{name}-{:0digits$}", k + 1) };
                                    let path = naming::unique_path(&dir, &stem, ext_for(format), &mut reserved);
                                    jobs::write_atomic(&path, &bytes).map_err(|e| e.to_string())?;
                                    result.outputs.push(OutputFile { path, bytes: bytes.len() as u64, width: picture.width, height: picture.height });
                                    listener.on_progress(i as u32, (k + 1) as f64 / total as f64);
                                }
                                Ok(())
                            })
                        }
                        _ => unreachable!(),
                    };
                    if let Err(e) = r {
                        result.status = ItemStatus::Failed(e);
                    }
                    listener.on_progress(i as u32, 1.0);
                    out.push(result);
                }
            }
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc;

    struct Collect(Mutex<mpsc::Sender<Option<JobItemResult>>>);
    impl JobListener for Collect {
        fn on_progress(&self, _: u32, _: f64) {}
        fn on_item(&self, r: JobItemResult) {
            let _ = self.0.lock().unwrap().send(Some(r));
        }
        fn on_finished(&self) {
            let _ = self.0.lock().unwrap().send(None);
        }
    }

    fn wait(rx: &mpsc::Receiver<Option<JobItemResult>>) -> Vec<JobItemResult> {
        let mut v = Vec::new();
        while let Ok(Some(r)) = rx.recv_timeout(std::time::Duration::from_secs(120)) {
            v.push(r);
        }
        v
    }

    fn naming(t: &str) -> Naming {
        Naming { template: t.into(), lowercase: true, pinyin: true, hyphenate: true, date: "20261016".into() }
    }

    fn settings() -> ImageSettings {
        ImageSettings { format: ImageFormat::Webp, quality: 78, lossless: false, target_bytes: None, sizes: vec![800], size_by: SizeBy::Width, crop: None, crop_boxes: vec![] }
    }

    #[test]
    fn 图片任务端到端_试压缓存_撤销() {
        let dir = std::env::temp_dir().join(format!("smaller-api-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let src = dir.join("新品 海报.png");
        let rgba: Vec<u8> = (0..1600u32 * 1000).flat_map(|k| [(k % 251) as u8, (k / 1600 % 256) as u8, 90, 255]).collect();
        image::save_buffer(&src, &rgba, 1600, 1000, image::ExtendedColorType::Rgba8).unwrap();

        let engine = Engine::new(None);
        let preview = engine.preview_image(src.to_string_lossy().into(), settings()).unwrap();
        assert_eq!((preview.width, preview.height, preview.format), (800, 500, ImageFormat::Webp));
        let again = engine.preview_image(src.to_string_lossy().into(), settings()).unwrap();
        assert_eq!(again.bytes, preview.bytes);

        let (tx, rx) = mpsc::channel();
        let job = engine.clone().run_images(vec![src.to_string_lossy().into()], Place::BesideSource { folder_suffix: "-small".into() }, naming("{name}-{w}"), settings(), Arc::new(Collect(Mutex::new(tx))));
        let results = wait(&rx);
        assert!(job.is_finished());
        assert_eq!(results.len(), 1);
        assert_eq!(results[0].state, ItemState::Done);
        assert!(results[0].outputs[0].path.ends_with("xin-pin-hai-bao-800.webp"), "{}", results[0].outputs[0].path);
        // 试压体积与正式输出一致。
        assert_eq!(results[0].outputs[0].bytes, preview.bytes);
        job.undo().unwrap();
        assert!(!Path::new(&results[0].outputs[0].path).exists());
    }

    #[test]
    fn 平台解码接管_heic() {
        struct Fake;
        impl PlatformDecoder for Fake {
            fn decode(&self, _: String) -> Option<RawImage> {
                Some(RawImage { width: 4, height: 2, rgba: vec![200; 32], has_alpha: false })
            }
        }
        let dir = std::env::temp_dir().join(format!("smaller-api-heic-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let heic = dir.join("IMG_0001.HEIC");
        std::fs::write(&heic, b"fake").unwrap();
        let meta = Engine::new(Some(Arc::new(Fake))).image_meta(heic.to_string_lossy().into()).unwrap();
        assert_eq!((meta.width, meta.height), (4, 2));
        assert!(Engine::new(None).image_meta(heic.to_string_lossy().into()).is_err());
    }
}
