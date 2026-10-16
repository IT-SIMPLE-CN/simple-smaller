//! 批量任务：把一次「开始」变成实际的文件输出。
//!
//! - 识别文件类型；拖入文件夹时递归展开，输出放进旁边的新文件夹并照搬目录结构。
//! - 套用配方：裁切 → 一档或多档宽度 → 编码 → 命名 → 先写隐藏临时文件再改名。
//! - 原格式、未裁切、未缩小且压完不比原文件小时，保留原文件、不写出。
//! - 每个文件单独汇报结果，一个失败不影响其他；产出可整批撤销（移到废纸篓）。

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, Ordering};

use rayon::prelude::*;

use crate::image::{self as img, CropRect, ImageRecipe, OutputFormat, Picture, SizeMode};
use crate::naming::{self, NameVars, NamingRule};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum MediaKind {
    Image,
    Video,
    Pdf,
}

pub fn media_kind(path: &Path) -> Option<MediaKind> {
    let ext = path.extension()?.to_string_lossy().to_lowercase();
    match ext.as_str() {
        "jpg" | "jpeg" | "png" | "webp" | "avif" | "heic" | "heif" | "gif" | "tif" | "tiff" | "bmp" => Some(MediaKind::Image),
        "mp4" | "mov" | "m4v" | "mkv" | "webm" | "avi" | "mts" | "m2ts" => Some(MediaKind::Video),
        "pdf" => Some(MediaKind::Pdf),
        _ => None,
    }
}

/// 输出放在哪。
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum OutputPlace {
    /// 原文件旁边；拖入的文件夹则放进旁边的「原名 + 后缀」新文件夹。
    BesideSource { folder_suffix: String },
    /// 指定文件夹；拖入的文件夹在里面照搬结构。
    Folder(PathBuf),
}

#[derive(Debug, Clone)]
pub struct PlannedItem {
    pub source: PathBuf,
    pub out_dir: PathBuf,
    /// 来自哪个拖入的文件夹（用于 `{folder}`）。
    pub folder: String,
}

#[derive(Debug, Clone, Default)]
pub struct Plan {
    pub items: Vec<PlannedItem>,
    /// 为拖入的文件夹新建的输出根目录，撤销时整个移到废纸篓。
    pub created_roots: Vec<PathBuf>,
    /// 跳过的文件与原因。
    pub skipped: Vec<(PathBuf, String)>,
}

pub const MAX_FOLDER_FILES: usize = 5000;

fn is_hidden(path: &Path) -> bool {
    path.file_name().is_some_and(|n| n.to_string_lossy().starts_with('.'))
}

/// 文件包（App、照片图库这类）在访达里看着像一个文件，不往里找。
fn is_package(path: &Path) -> bool {
    path.extension().is_some_and(|e| {
        matches!(e.to_string_lossy().to_lowercase().as_str(), "app" | "photoslibrary" | "bundle" | "framework" | "fcpbundle" | "logicx")
    })
}

fn walk(dir: &Path, kind: MediaKind, out: &mut Vec<PathBuf>, skipped: &mut Vec<(PathBuf, String)>) {
    let Ok(entries) = std::fs::read_dir(dir) else { return };
    let mut entries: Vec<PathBuf> = entries.filter_map(|e| e.ok().map(|e| e.path())).collect();
    entries.sort();
    for path in entries {
        if out.len() >= MAX_FOLDER_FILES {
            skipped.push((path, format!("超过 {MAX_FOLDER_FILES} 个文件")));
            continue;
        }
        if is_hidden(&path) {
            continue;
        }
        if path.is_dir() {
            if !is_package(&path) {
                walk(&path, kind, out, skipped);
            }
        } else if media_kind(&path) == Some(kind) {
            out.push(path);
        }
    }
}

/// 展开输入：文件直接收，文件夹递归收同类文件。
pub fn plan(inputs: &[PathBuf], kind: MediaKind, place: &OutputPlace) -> Plan {
    let mut plan = Plan::default();
    let mut used_roots: HashSet<PathBuf> = HashSet::new();
    for input in inputs {
        if input.is_dir() {
            let folder_name = input.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
            let root = match place {
                OutputPlace::BesideSource { folder_suffix } => {
                    let parent = input.parent().unwrap_or(Path::new("/"));
                    naming::unique_path(parent, &format!("{folder_name}{folder_suffix}"), "", &mut used_roots)
                }
                OutputPlace::Folder(dir) => naming::unique_path(dir, &folder_name, "", &mut used_roots),
            };
            let mut files = Vec::new();
            walk(input, kind, &mut files, &mut plan.skipped);
            if files.is_empty() {
                continue;
            }
            plan.created_roots.push(root.clone());
            for file in files {
                let rel = file.parent().and_then(|p| p.strip_prefix(input).ok()).map(Path::to_path_buf).unwrap_or_default();
                plan.items.push(PlannedItem { source: file, out_dir: root.join(rel), folder: folder_name.clone() });
            }
        } else if media_kind(input) == Some(kind) {
            let parent = input.parent().map(Path::to_path_buf).unwrap_or_default();
            let out_dir = match place {
                OutputPlace::BesideSource { .. } => parent.clone(),
                OutputPlace::Folder(dir) => dir.clone(),
            };
            let folder = parent.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
            plan.items.push(PlannedItem { source: input.clone(), out_dir, folder });
        } else {
            plan.skipped.push((input.clone(), "不是这一类文件".into()));
        }
    }
    plan
}

// ---------- 结果与撤销 ----------

#[derive(Debug, Clone, PartialEq)]
pub struct OutputFile {
    pub path: PathBuf,
    pub bytes: u64,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, PartialEq)]
pub enum ItemStatus {
    Done,
    /// 压完不比原文件小，保留原文件。
    KeptOriginal,
    Failed(String),
    Cancelled,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ItemResult {
    pub index: usize,
    pub source: PathBuf,
    pub before_bytes: u64,
    pub outputs: Vec<OutputFile>,
    pub status: ItemStatus,
}

/// 把产出移到废纸篓。新建的输出根目录整个移走，其余逐个文件移走。
pub fn undo(results: &[ItemResult], created_roots: &[PathBuf]) -> Result<(), String> {
    let mut targets: Vec<PathBuf> = created_roots.iter().filter(|p| p.exists()).cloned().collect();
    for r in results {
        for o in &r.outputs {
            if o.path.exists() && !created_roots.iter().any(|root| o.path.starts_with(root)) {
                targets.push(o.path.clone());
            }
        }
    }
    if targets.is_empty() {
        return Ok(());
    }
    trash::delete_all(&targets).map_err(|e| e.to_string())
}

/// 先写同目录隐藏临时文件，写完再改名，失败不留半截文件。
pub fn write_atomic(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let tmp = path.with_file_name(format!(".{name}.smaller-tmp"));
    let result = std::fs::write(&tmp, bytes).and_then(|_| std::fs::rename(&tmp, path));
    if result.is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
    result
}

/// 把一批文件打成 zip：只存不压（图片视频本身已压缩）、文件名按 UTF-8 标记，
/// Windows 上打开中文不乱码；不带隐藏文件。
pub fn write_zip(files: &[PathBuf], base: &Path, zip_path: &Path) -> Result<(), String> {
    use std::io::Write;
    let tmp = zip_path.with_file_name(format!(".{}.smaller-tmp", zip_path.file_name().unwrap_or_default().to_string_lossy()));
    let result = (|| -> Result<(), String> {
        let file = std::fs::File::create(&tmp).map_err(|e| e.to_string())?;
        let mut zip = zip::ZipWriter::new(file);
        let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Stored).large_file(true);
        for f in files {
            let name = f.strip_prefix(base).unwrap_or(f).to_string_lossy().replace('\\', "/");
            zip.start_file(name, options).map_err(|e| e.to_string())?;
            zip.write_all(&std::fs::read(f).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
        }
        zip.finish().map_err(|e| e.to_string())?;
        std::fs::rename(&tmp, zip_path).map_err(|e| e.to_string())
    })();
    if result.is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
    result
}

/// 拼图总览：等宽缩略图按网格排开，白色间隔。
pub fn contact_sheet(pictures: &[Picture], columns: u32, cell_width: u32) -> Option<Picture> {
    let first = pictures.first()?;
    let cell_height = ((cell_width as f64) * first.height as f64 / first.width.max(1) as f64).round() as u32;
    let gap = 8u32;
    let rows = (pictures.len() as u32).div_ceil(columns);
    let width = columns * cell_width + (columns + 1) * gap;
    let height = rows * cell_height + (rows + 1) * gap;
    let mut rgba = vec![255u8; (width * height * 4) as usize];
    for (k, p) in pictures.iter().enumerate() {
        let thumb = img::resize(p, SizeMode::Width, cell_width).ok()?;
        let (cx, cy) = ((k as u32 % columns) * (cell_width + gap) + gap, (k as u32 / columns) * (cell_height + gap) + gap);
        for y in 0..thumb.height.min(cell_height) {
            let src = (y * thumb.width * 4) as usize;
            let dst = (((cy + y) * width + cx) * 4) as usize;
            let len = (thumb.width.min(cell_width) * 4) as usize;
            rgba[dst..dst + len].copy_from_slice(&thumb.rgba[src..src + len]);
        }
    }
    Some(img::from_rgba(width, height, rgba, false, img::SourceFormat::VideoFrame))
}

// ---------- 图片批量 ----------

#[derive(Debug, Clone)]
pub struct ImageCrop {
    /// 宽 ÷ 高；None 是自由比例：按每张手调的框，没调过的用整张图。
    pub aspect: Option<f64>,
    /// 输出宽度；高按比例算。
    pub width: u32,
    pub label: Option<String>,
}

impl ImageCrop {
    /// 没手调过的图用的框：有比例时居中取最大，自由比例用整张图。
    pub fn default_rect(&self, width: u32, height: u32) -> CropRect {
        match self.aspect {
            Some(a) => img::center_max_rect(width, height, a),
            None => CropRect { x: 0, y: 0, width, height },
        }
    }
}

#[derive(Debug, Clone)]
pub struct ImageJob {
    pub recipe: ImageRecipe,
    /// 输出宽度档位（按 `size_mode` 解释）。空表示不缩放；多档时一张图出多个文件。
    pub sizes: Vec<u32>,
    pub size_mode: SizeMode,
    pub crop: Option<ImageCrop>,
    /// 用户手调过的裁切框（原图像素坐标）；没调过的按居中最大。
    pub crop_overrides: HashMap<PathBuf, CropRect>,
    pub naming: NamingRule,
    pub date: String,
}

/// 解码入口：mac 端可以注入系统解码（如 HEIC），默认用引擎自己的。
pub type Decoder<'a> = &'a (dyn Fn(&Path) -> Result<Picture, String> + Sync);

pub fn default_decoder(path: &Path) -> Result<Picture, String> {
    img::load(path).map_err(|e| e.to_string())
}

fn extension(format: OutputFormat) -> &'static str {
    match format {
        OutputFormat::Jpeg => "jpg",
        OutputFormat::Webp => "webp",
        OutputFormat::Avif => "avif",
        OutputFormat::Png => "png",
        OutputFormat::Original => unreachable!(),
    }
}

fn same_format(format: OutputFormat, source: &Path) -> bool {
    let ext = source.extension().map(|e| e.to_string_lossy().to_lowercase()).unwrap_or_default();
    matches!(
        (format, ext.as_str()),
        (OutputFormat::Jpeg, "jpg" | "jpeg") | (OutputFormat::Webp, "webp") | (OutputFormat::Avif, "avif") | (OutputFormat::Png, "png")
    )
}

fn process_image(
    index: usize,
    item: &PlannedItem,
    job: &ImageJob,
    decoder: Decoder,
    reserved: &Mutex<HashSet<PathBuf>>,
) -> ItemResult {
    let before_bytes = std::fs::metadata(&item.source).map(|m| m.len()).unwrap_or(0);
    let mut result = ItemResult { index, source: item.source.clone(), before_bytes, outputs: Vec::new(), status: ItemStatus::Done };
    let picture = match decoder(&item.source) {
        Ok(p) => p,
        Err(e) => {
            result.status = ItemStatus::Failed(e);
            return result;
        }
    };

    let mut base = picture;
    let mut resized_or_cropped = false;
    let mut ratio = None;
    let mut sizes = job.sizes.clone();
    let mut mode = job.size_mode;
    if let Some(c) = &job.crop {
        let rect = job.crop_overrides.get(&item.source).copied().unwrap_or_else(|| c.default_rect(base.width, base.height));
        base = img::crop(&base, rect);
        resized_or_cropped = true;
        ratio = c.label.clone();
        sizes = vec![c.width];
        mode = SizeMode::Width;
    }
    if sizes.is_empty() {
        sizes.push(0);
    }

    let format = img::resolve_format(job.recipe.format, &base);
    let stem_source = item.source.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
    for size in sizes {
        let picture = if size > 0 { img::resize(&base, mode, size) } else { Ok(base.clone()) };
        let picture = match picture {
            Ok(p) => p,
            Err(e) => {
                result.status = ItemStatus::Failed(e.to_string());
                return result;
            }
        };
        let shrunk = picture.width < base.width || resized_or_cropped;
        let mut recipe = job.recipe.clone();
        recipe.resize = None;
        recipe.format = format;
        let encoded = match img::render(&picture, &recipe) {
            Ok(e) => e,
            Err(e) => {
                result.status = ItemStatus::Failed(e.to_string());
                return result;
            }
        };
        if !shrunk && same_format(format, &item.source) && encoded.bytes.len() as u64 >= before_bytes && job.sizes.len() <= 1 {
            result.status = ItemStatus::KeptOriginal;
            return result;
        }
        let vars = NameVars {
            name: stem_source.clone(),
            width: encoded.width,
            height: encoded.height,
            ratio: ratio.clone().or_else(|| Some(naming::ratio_label(encoded.width, encoded.height))),
            date: job.date.clone(),
            index: index + 1,
            folder: item.folder.clone(),
        };
        let mut stem = naming::render(&job.naming, &vars);
        // 多档时模板里没写 {w}，自动补上档位，避免几档互相覆盖。
        if job.sizes.len() > 1 && !job.naming.template.contains("{w}") {
            stem = format!("{stem}-{}", encoded.width);
        }
        let path = {
            let mut reserved = reserved.lock().unwrap();
            naming::unique_path(&item.out_dir, &stem, extension(format), &mut reserved)
        };
        if let Err(e) = write_atomic(&path, &encoded.bytes) {
            result.status = ItemStatus::Failed(format!("写入失败：{e}"));
            return result;
        }
        result.outputs.push(OutputFile { path, bytes: encoded.bytes.len() as u64, width: encoded.width, height: encoded.height });
    }
    result
}

/// 并行处理一批图片。`on_result` 在每个文件完成时调用（可能来自不同线程）。
pub fn run_images(
    plan: &Plan,
    job: &ImageJob,
    decoder: Decoder,
    on_start: &(dyn Fn(usize) + Sync),
    on_result: &(dyn Fn(&ItemResult) + Sync),
    cancel: &AtomicBool,
) -> Vec<ItemResult> {
    let reserved = Mutex::new(HashSet::new());
    let threads = (std::thread::available_parallelism().map(|n| n.get()).unwrap_or(4) / 2).max(2);
    let pool = rayon::ThreadPoolBuilder::new().num_threads(threads).build().expect("线程池");
    let mut results: Vec<ItemResult> = pool.install(|| {
        plan.items
            .par_iter()
            .enumerate()
            .map(|(i, item)| {
                let r = if cancel.load(Ordering::Relaxed) {
                    ItemResult { index: i, source: item.source.clone(), before_bytes: 0, outputs: Vec::new(), status: ItemStatus::Cancelled }
                } else {
                    on_start(i);
                    process_image(i, item, job, decoder, &reserved)
                };
                on_result(&r);
                r
            })
            .collect()
    });
    results.sort_by_key(|r| r.index);
    results
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("smaller-jobs-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    fn photo_file(path: &Path, w: u32, h: u32) {
        let mut rgba = Vec::with_capacity((w * h * 4) as usize);
        for y in 0..h {
            for x in 0..w {
                let n = ((x * 7919 + y * 104729) % 251) as u8;
                rgba.extend_from_slice(&[(x * 255 / w) as u8 ^ (n / 6), (y * 255 / h) as u8, n / 2 + 60, 255]);
            }
        }
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        image::save_buffer(path, &rgba, w, h, image::ExtendedColorType::Rgba8).unwrap();
    }

    fn job(format: OutputFormat) -> ImageJob {
        ImageJob {
            recipe: ImageRecipe { format, quality: 78, lossless: false, resize: None, target_bytes: None },
            sizes: vec![],
            size_mode: SizeMode::Width,
            crop: None,
            crop_overrides: HashMap::new(),
            naming: NamingRule { template: "{name}-small".into(), ..Default::default() },
            date: "20261016".into(),
        }
    }

    #[test]
    fn 文件夹照搬结构并跳过隐藏文件() {
        let dir = scratch("plan");
        let src = dir.join("秋季素材");
        photo_file(&src.join("a.png"), 64, 64);
        photo_file(&src.join("细节/b.png"), 64, 64);
        photo_file(&src.join(".隐藏/c.png"), 64, 64);
        std::fs::write(src.join("说明.txt"), b"x").unwrap();
        let p = plan(&[src.clone()], MediaKind::Image, &OutputPlace::BesideSource { folder_suffix: "-small".into() });
        assert_eq!(p.items.len(), 2);
        assert_eq!(p.created_roots, vec![dir.join("秋季素材-small")]);
        assert!(p.items.iter().any(|i| i.out_dir == dir.join("秋季素材-small/细节")));
    }

    #[test]
    fn 多档输出_命名与撤销() {
        let dir = scratch("multi");
        let src = dir.join("hero.png");
        photo_file(&src, 1600, 900);
        let mut j = job(OutputFormat::Webp);
        j.sizes = vec![1280, 640];
        j.naming.template = "{name}".into();
        let p = plan(&[src.clone()], MediaKind::Image, &OutputPlace::BesideSource { folder_suffix: "-small".into() });
        let results = run_images(&p, &j, &default_decoder, &|_| {}, &|_| {}, &AtomicBool::new(false));
        assert_eq!(results[0].status, ItemStatus::Done);
        let names: Vec<String> = results[0].outputs.iter().map(|o| o.path.file_name().unwrap().to_string_lossy().into_owned()).collect();
        assert_eq!(names, vec!["hero-1280.webp", "hero-640.webp"]);
        assert_eq!((results[0].outputs[1].width, results[0].outputs[1].height), (640, 360));
        assert!(src.exists());
        undo(&results, &p.created_roots).unwrap();
        assert!(results[0].outputs.iter().all(|o| !o.path.exists()));
        assert!(src.exists());
    }

    #[test]
    fn 裁切_默认居中与手调框() {
        let dir = scratch("crop");
        let a = dir.join("a.png");
        let b = dir.join("b.png");
        photo_file(&a, 1200, 800);
        photo_file(&b, 1200, 800);
        let mut j = job(OutputFormat::Jpeg);
        j.crop = Some(ImageCrop { aspect: Some(0.75), width: 300, label: Some("3x4".into()) });
        assert_eq!(ImageCrop { aspect: None, width: 300, label: None }.default_rect(640, 480), CropRect { x: 0, y: 0, width: 640, height: 480 });
        j.crop_overrides.insert(b.clone(), CropRect { x: 0, y: 0, width: 300, height: 400 });
        j.naming.template = "{name}-{ratio}-{w}x{h}".into();
        let p = plan(&[a, b], MediaKind::Image, &OutputPlace::BesideSource { folder_suffix: "-small".into() });
        let results = run_images(&p, &j, &default_decoder, &|_| {}, &|_| {}, &AtomicBool::new(false));
        for r in &results {
            assert_eq!(r.status, ItemStatus::Done);
            assert_eq!((r.outputs[0].width, r.outputs[0].height), (300, 400));
        }
        assert!(results[0].outputs[0].path.ends_with("a-3x4-300x400.jpg"));
    }

    #[test]
    fn 压完变大时保留原文件() {
        let dir = scratch("keep");
        // 用 jpegli 自己按质量 20 存的 JPEG，再按质量 78 原格式重压一定更大。
        // （普通编码器存的低质量 JPEG 不行：没做霍夫曼优化，jpegli 重压到 78 反而更小。）
        let png = dir.join("p.png");
        photo_file(&png, 400, 300);
        let src = dir.join("low.jpg");
        std::fs::write(&src, img::encode(&img::load(&png).unwrap(), OutputFormat::Jpeg, 20, false).unwrap()).unwrap();
        let p = plan(&[src], MediaKind::Image, &OutputPlace::BesideSource { folder_suffix: "-small".into() });
        let results = run_images(&p, &job(OutputFormat::Original), &default_decoder, &|_| {}, &|_| {}, &AtomicBool::new(false));
        assert_eq!(results[0].status, ItemStatus::KeptOriginal);
        assert!(results[0].outputs.is_empty());
        assert!(!dir.join("low-small.jpg").exists());
    }

    #[test]
    fn 打包_zip_与拼图总览() {
        let dir = scratch("zip");
        let a = dir.join("帧/秋季_0001.png");
        let b = dir.join("帧/秋季_0002.png");
        photo_file(&a, 120, 80);
        photo_file(&b, 120, 80);
        let zip_path = dir.join("帧.zip");
        write_zip(&[a.clone(), b.clone()], &dir, &zip_path).unwrap();
        let archive = zip::ZipArchive::new(std::fs::File::open(&zip_path).unwrap()).unwrap();
        let names: Vec<String> = archive.file_names().map(|n| n.unwrap().into_owned()).collect();
        assert!(names.iter().any(|n| n == "帧/秋季_0001.png") && names.len() == 2, "{names:?}");
        let sheet = contact_sheet(&[img::load(&a).unwrap(), img::load(&b).unwrap(), img::load(&a).unwrap()], 2, 60).unwrap();
        assert_eq!((sheet.width, sheet.height), (2 * 60 + 3 * 8, 2 * 40 + 3 * 8));
    }

    #[test]
    fn 重名不覆盖_失败不影响其他() {
        let dir = scratch("dup");
        let a = dir.join("x.png");
        photo_file(&a, 200, 200);
        std::fs::write(dir.join("x-small.webp"), b"old").unwrap();
        let broken = dir.join("broken.png");
        std::fs::write(&broken, b"not a png").unwrap();
        let p = plan(&[a, broken], MediaKind::Image, &OutputPlace::BesideSource { folder_suffix: "-small".into() });
        let results = run_images(&p, &job(OutputFormat::Webp), &default_decoder, &|_| {}, &|_| {}, &AtomicBool::new(false));
        assert!(results[0].outputs[0].path.ends_with("x-small-2.webp"));
        assert_eq!(std::fs::read(dir.join("x-small.webp")).unwrap(), b"old");
        assert!(matches!(results[1].status, ItemStatus::Failed(_)));
    }
}
