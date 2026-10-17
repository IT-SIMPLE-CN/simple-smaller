//! 引擎：对外的入口（规划、元信息、跑图片 / 视频 / PDF 任务），解码与命名。

use std::path::{Path, PathBuf};
use std::sync::atomic::Ordering;
use std::sync::Arc;

use crate::image::{self as img, ImageRecipe, OutputFormat, Picture, SizeMode, SourceFormat};
use crate::jobs::{self, ItemResult, ItemStatus, MediaKind, OutputPlace};
use crate::naming::{self, NameVars};
use crate::{EngineError, pdf, video};
use super::job::*;
use super::types::*;

// ---------- 引擎 ----------

#[derive(uniffi::Object)]
pub struct Engine {
    pub(super) decoder: Option<Arc<dyn PlatformDecoder>>,
}

pub(super) fn file_bytes(path: &Path) -> u64 {
    std::fs::metadata(path).map(|m| m.len()).unwrap_or(0)
}

pub(super) fn failed(index: usize, source: &Path, message: String) -> ItemResult {
    ItemResult { index, source: source.to_path_buf(), before_bytes: file_bytes(source), outputs: Vec::new(), status: ItemStatus::Failed(message) }
}

pub(super) fn stem(path: &Path) -> String {
    path.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default()
}

#[uniffi::export]
impl Engine {
    #[uniffi::constructor]
    pub fn new(decoder: Option<Arc<dyn PlatformDecoder>>) -> Arc<Self> {
        Arc::new(Engine { decoder })
    }

    /// 展开输入（含文件夹）后会处理多少个文件、跳过哪些。
    pub fn plan(&self, inputs: Vec<String>, kind: Kind, place: Place) -> PlanSummary {
        let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
        let p = jobs::plan(&inputs, kind.into(), &(&place).into());
        PlanSummary { files: p.items.len() as u32, skipped: p.skipped.iter().map(|(path, why)| format!("{}：{why}", path.display())).collect() }
    }

    /// 剪贴板用：把一张图压成字节直接返回，不写文件。
    /// 截图、图文类（颜色少）压成减色 PNG，照片压成 JPEG；长边超过 `max_long_edge` 时缩小。
    /// 压完不比原来小时返回 None（剪贴板保持原样）。
    pub fn compress_for_clipboard(&self, path: String, max_long_edge: u32) -> Result<Option<ClipboardImage>, EngineError> {
        let source = Path::new(&path);
        let picture = self.decode(source).map_err(|message| EngineError::Unsupported { message })?;
        let graphic = picture.kind == img::ContentKind::Graphic;
        let format = if graphic || picture.has_alpha { OutputFormat::Png } else { OutputFormat::Jpeg };
        let resize = (max_long_edge > 0 && picture.width.max(picture.height) > max_long_edge).then_some((SizeMode::LongEdge, max_long_edge));
        let recipe = ImageRecipe { format, quality: if graphic { 90 } else { 82 }, lossless: false, resize, target_bytes: None };
        let encoded = img::render(&picture, &recipe).map_err(|e| EngineError::Failed { message: e.to_string() })?;
        let before = file_bytes(source);
        if encoded.bytes.len() as u64 >= before && resize.is_none() {
            return Ok(None);
        }
        Ok(Some(ClipboardImage { png: format == OutputFormat::Png, width: encoded.width, height: encoded.height, before_bytes: before, bytes: encoded.bytes }))
    }

    /// 用密码解出一份不加密的副本（放在临时目录，处理时代替原文件）。密码不对报「密码不对」。
    pub fn unlock_pdf(&self, path: String, password: String, output: String) -> Result<(), EngineError> {
        if let Some(dir) = Path::new(&output).parent() { let _ = std::fs::create_dir_all(dir); }
        pdf::unlock(Path::new(&path), Path::new(&output), &password).map_err(|e| match e {
            pdf::PdfError::WrongPassword => EngineError::Unsupported { message: "密码不对".into() },
            other => EngineError::Failed { message: other.to_string() },
        })
    }

    /// 展开输入后每个文件的输出目录，以及会新建的输出根目录。视频逐段起任务、失败重试时用它
    /// 把每个文件送到原本该去的地方（拖入文件夹的照样进「文件夹-small」）。
    pub fn plan_targets(&self, inputs: Vec<String>, kind: Kind, place: Place) -> PlanTargets {
        let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
        let p = jobs::plan(&inputs, kind.into(), &(&place).into());
        PlanTargets {
            items: p.items.iter().map(|i| PlanTarget { source: i.source.to_string_lossy().into_owned(), out_dir: i.out_dir.to_string_lossy().into_owned() }).collect(),
            created_roots: p.created_roots.iter().map(|r| r.to_string_lossy().into_owned()).collect(),
        }
    }

    /// 把一批路径移到废纸篓（撤销时清掉新建的输出文件夹）。不存在的跳过。
    pub fn trash(&self, paths: Vec<String>) -> Result<(), EngineError> {
        let existing: Vec<PathBuf> = paths.into_iter().map(PathBuf::from).filter(|p| p.exists()).collect();
        if existing.is_empty() { return Ok(()); }
        trash::delete_all(&existing).map_err(|e| EngineError::Failed { message: e.to_string() })
    }

    /// 展开输入后实际会处理的文件（与任务用同一套规则）。
    pub fn list_files(&self, inputs: Vec<String>, kind: Kind) -> Vec<String> {
        let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
        let p = jobs::plan(&inputs, kind.into(), &OutputPlace::BesideSource { folder_suffix: String::new() });
        p.items.iter().map(|i| i.source.to_string_lossy().into_owned()).collect()
    }

    /// 按命名规则算出示例文件名（不含扩展名），配方编辑里的「示例」用，与真实输出同一套规则。
    pub fn preview_name(&self, naming: Naming, source_name: String, width: u32, height: u32) -> String {
        self.name(&naming, Path::new(&source_name), "", 0, width, height)
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

    pub fn run_images(self: Arc<Self>, inputs: Vec<String>, place: Place, naming: Naming, settings: ImageSettings, listener: Arc<dyn JobListener>) -> Arc<Job> {
        let job = Job::new();
        let handle = job.clone();
        std::thread::spawn(move || {
            let inputs: Vec<PathBuf> = inputs.into_iter().map(PathBuf::from).collect();
            let plan = jobs::plan(&inputs, MediaKind::Image, &(&place).into());
            *job.created_roots.lock().unwrap_or_else(|e| e.into_inner()) = plan.created_roots.clone();
            let image_job = settings.job(&naming);
            let decoder = |p: &Path| self.decode(p);
            // 开始处理某张时报 0 进度，界面据此把这一行标成「处理中」。
            let on_start = |i: usize| listener.on_progress(i as u32, 0.0);
            let on_result = |r: &ItemResult| listener.on_item(JobItemResult::from(r));
            let results = jobs::run_images(&plan, &image_job, &decoder, &on_start, &on_result, &job.cancel);
            *job.results.lock().unwrap_or_else(|e| e.into_inner()) = results;
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
            *job.created_roots.lock().unwrap_or_else(|e| e.into_inner()) = plan.created_roots.clone();
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
            *job.created_roots.lock().unwrap_or_else(|e| e.into_inner()) = plan.created_roots.clone();
            for mut r in self.pdf_items(&plan, &naming, &settings, &listener, &job.cancel) {
                if let Some(pw) = settings.password.as_deref().filter(|p| !p.is_empty()) {
                    let mut failed = None;
                    for o in r.outputs.iter_mut().filter(|o| o.path.extension().is_some_and(|e| e.eq_ignore_ascii_case("pdf"))) {
                        match pdf::encrypt_in_place(&o.path, pw) {
                            Ok(()) => o.bytes = file_bytes(&o.path),
                            Err(e) => failed = Some(format!("加密码失败：{e}")),
                        }
                    }
                    // 加密码失败时不留下没加密的文件：用户以为发出去的是带密码的。
                    if let Some(message) = failed {
                        for o in &r.outputs {
                            let _ = std::fs::remove_file(&o.path);
                        }
                        r.outputs.clear();
                        r.status = ItemStatus::Failed(message);
                    }
                }
                job.record(&listener, r);
            }
            job.finish(&listener);
        });
        handle
    }
}

impl Engine {
    /// HEIC 这类引擎不认识的格式交给平台；引擎解码失败时也再试一次平台。
    pub(super) fn decode(&self, path: &Path) -> Result<Picture, String> {
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
            Err(e) => {
                // 引擎认识的格式解坏了，说明文件有问题（截断、损坏），直接报错；系统解码会把缺的部分补黑。
                let known = std::fs::read(path).map(|d| img::is_known_format(&d)).unwrap_or(false);
                if known { return Err(friendly_decode_error(&e.to_string())); }
                platform(path).ok_or_else(|| e.to_string())
            }
        }
    }

    pub(super) fn name(&self, naming: &Naming, source: &Path, folder: &str, index: usize, width: u32, height: u32) -> String {
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
}

/// 解码错误写成用户能看懂的话。
pub(super) fn friendly_decode_error(raw: &str) -> String {
    let lower = raw.to_lowercase();
    if lower.contains("不完整") || lower.contains("eof") || lower.contains("unexpected end") || lower.contains("truncat") {
        "文件不完整".into()
    } else {
        "文件损坏".into()
    }
}
