//! PDF 管线：MuPDF 负责图片降采样重压、字体子集化、去元数据、组装页面与渲染。
//!
//! 输出先写同目录隐藏临时文件，校验能打开再改名；压完比原文件还大时保留原文件。

use std::ffi::{CStr, CString, c_char};
use std::os::raw::c_int;
use std::path::{Path, PathBuf};

use crate::image::{self as img, Picture, SourceFormat};

unsafe extern "C" {
    fn smaller_pdf_info(path: *const c_char, pages: *mut c_int, needs_password: *mut c_int, err: *mut c_char, err_len: usize) -> c_int;
    fn smaller_pdf_page_size(path: *const c_char, page: c_int, w: *mut f32, h: *mut f32, err: *mut c_char, err_len: usize) -> c_int;
    fn smaller_pdf_compress(
        input: *const c_char, output: *const c_char, rewrite_images: c_int,
        color_dpi: c_int, gray_dpi: c_int, bitonal_dpi: c_int, jpeg_quality: c_int,
        subset_fonts: c_int, strip_meta: c_int, grayscale: c_int, err: *mut c_char, err_len: usize,
    ) -> c_int;
    fn smaller_pdf_assemble(
        inputs: *const *const c_char, input_count: c_int,
        doc_index: *const c_int, page_index: *const c_int, page_count: c_int,
        output: *const c_char, strip_meta: c_int, err: *mut c_char, err_len: usize,
    ) -> c_int;
    fn smaller_pdf_render(
        path: *const c_char, page: c_int, target_width: c_int,
        rgba: *mut *mut u8, w: *mut c_int, h: *mut c_int, err: *mut c_char, err_len: usize,
    ) -> c_int;
    fn smaller_pdf_free_pixels(rgba: *mut u8);
    fn smaller_pdf_unlock(input: *const c_char, output: *const c_char, password: *const c_char, err: *mut c_char, err_len: usize) -> c_int;
    fn smaller_pdf_encrypt(input: *const c_char, output: *const c_char, password: *const c_char, err: *mut c_char, err_len: usize) -> c_int;
    fn smaller_pdf_from_images(
        images: *const *const u8, lengths: *const usize, count: c_int, page_mode: c_int,
        dpi: f32, output: *const c_char, err: *mut c_char, err_len: usize,
    ) -> c_int;
}

#[derive(Debug, thiserror::Error)]
pub enum PdfError {
    #[error("{0}")]
    Mupdf(String),
    #[error("文件有打开密码")]
    Password,
    #[error("密码不对")]
    WrongPassword,
    #[error("读写失败：{0}")]
    Io(#[from] std::io::Error),
}

type Result<T> = std::result::Result<T, PdfError>;

fn cpath(p: &Path) -> CString {
    CString::new(p.to_string_lossy().as_bytes()).expect("路径里不能有空字符")
}

/// 调用外壳，失败时取出它写的错误信息。
fn call(f: impl FnOnce(*mut c_char, usize) -> c_int) -> Result<()> {
    let mut buf = vec![0 as c_char; 512];
    let rc = f(buf.as_mut_ptr(), buf.len());
    if rc == 0 {
        return Ok(());
    }
    let msg = unsafe { CStr::from_ptr(buf.as_ptr()) }.to_string_lossy().into_owned();
    if msg.contains("打开密码") {
        return Err(PdfError::Password);
    }
    Err(PdfError::Mupdf(msg))
}

/// 用密码解出一份不加密的副本。密码不对返回 `PdfError::WrongPassword`。
pub fn unlock(input: &Path, output: &Path, password: &str) -> Result<()> {
    let (i, o) = (cpath(input), cpath(output));
    let pw = CString::new(password).map_err(|_| PdfError::WrongPassword)?;
    let mut buf = vec![0 as c_char; 512];
    let rc = unsafe { smaller_pdf_unlock(i.as_ptr(), o.as_ptr(), pw.as_ptr(), buf.as_mut_ptr(), buf.len()) };
    match rc {
        0 => Ok(()),
        -2 => Err(PdfError::WrongPassword),
        _ => Err(PdfError::Mupdf(unsafe { CStr::from_ptr(buf.as_ptr()) }.to_string_lossy().into_owned())),
    }
}

/// 给写好的 PDF 加打开密码：先写同目录临时文件，成功再替换。
pub fn encrypt_in_place(path: &Path, password: &str) -> Result<()> {
    let tmp = crate::jobs::temp_sibling(path, "");
    let (i, o) = (cpath(path), cpath(&tmp));
    // MuPDF 的密码缓冲区是 128 字节，超长会被悄悄截断，导致用完整密码打不开；直接拒绝。
    if password.len() > 127 {
        return Err(PdfError::Mupdf("密码太长（最多 127 个字节，约 40 个汉字）".into()));
    }
    let pw = CString::new(password).map_err(|_| PdfError::Mupdf("密码里不能有空字符".into()))?;
    let r = call(|e, n| unsafe { smaller_pdf_encrypt(i.as_ptr(), o.as_ptr(), pw.as_ptr(), e, n) });
    if r.is_err() {
        let _ = std::fs::remove_file(&tmp);
        return r;
    }
    std::fs::rename(&tmp, path)?;
    Ok(())
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PdfInfo {
    pub pages: u32,
    pub needs_password: bool,
}

pub fn info(path: &Path) -> Result<PdfInfo> {
    let p = cpath(path);
    let (mut pages, mut pw) = (0, 0);
    call(|e, n| unsafe { smaller_pdf_info(p.as_ptr(), &mut pages, &mut pw, e, n) })?;
    Ok(PdfInfo { pages: pages as u32, needs_password: pw != 0 })
}

/// 页面尺寸（pt，1 pt = 1/72 英寸）。
pub fn page_size(path: &Path, page: u32) -> Result<(f32, f32)> {
    let p = cpath(path);
    let (mut w, mut h) = (0f32, 0f32);
    call(|e, n| unsafe { smaller_pdf_page_size(p.as_ptr(), page as c_int, &mut w, &mut h, e, n) })?;
    Ok((w, h))
}

/// 压缩档位：图片目标 dpi 与 JPEG 质量。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CompressLevel {
    pub color_dpi: u32,
    pub gray_dpi: u32,
    pub bitonal_dpi: u32,
    pub jpeg_quality: u8,
}

impl CompressLevel {
    /// 屏幕阅读：手机电脑看。
    pub const SCREEN: Self = Self { color_dpi: 110, gray_dpi: 110, bitonal_dpi: 300, jpeg_quality: 70 };
    /// 邮件附件：目标 10 MB 内。
    pub const EMAIL: Self = Self { color_dpi: 150, gray_dpi: 150, bitonal_dpi: 300, jpeg_quality: 75 };
    /// 打印保真：只压超过 300 dpi 的图。
    pub const PRINT: Self = Self { color_dpi: 300, gray_dpi: 300, bitonal_dpi: 600, jpeg_quality: 88 };
}

#[derive(Debug, Clone)]
pub struct PdfRecipe {
    pub level: CompressLevel,
    /// 限体积（字节）。设了以后从 `level` 起逐档降，直到不超过目标。
    pub target_bytes: Option<u64>,
    pub subset_fonts: bool,
    pub strip_metadata: bool,
    /// 整页转灰度。
    pub grayscale: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CompressOutcome {
    /// 写出了新文件。
    Written { bytes: u64, level: CompressLevel },
    /// 压完没有变小（省不到 2%），保留原文件，没有写出。
    KeptOriginal,
}

fn temp_path(output: &Path) -> PathBuf {
    crate::jobs::temp_sibling(output, ".pdf")
}

fn compress_once(input: &Path, tmp: &Path, level: Option<CompressLevel>, recipe: &PdfRecipe) -> Result<u64> {
    let (i, o) = (cpath(input), cpath(tmp));
    let rewrite = level.is_some();
    let level = level.unwrap_or(recipe.level);
    call(|e, n| unsafe {
        smaller_pdf_compress(
            i.as_ptr(), o.as_ptr(), rewrite as c_int,
            level.color_dpi as c_int, level.gray_dpi as c_int, level.bitonal_dpi as c_int, level.jpeg_quality as c_int,
            recipe.subset_fonts as c_int, recipe.strip_metadata as c_int, recipe.grayscale as c_int, e, n,
        )
    })?;
    // 写出来的文件要能重新打开，页数不变，才算成功。
    let before = info(input)?.pages;
    let after = info(tmp)?.pages;
    if before != after {
        return Err(PdfError::Mupdf(format!("页数不一致：{before} → {after}")));
    }
    Ok(std::fs::metadata(tmp)?.len())
}

/// 输出至少要比原文件小 2% 才写出。
const MIN_SAVING: f64 = 0.98;

/// 限体积时逐档降的阶梯（dpi, 质量）。
const LADDER: [(u32, u8); 6] = [(150, 75), (130, 70), (110, 68), (96, 62), (80, 55), (72, 50)];

pub fn compress(input: &Path, output: &Path, recipe: &PdfRecipe) -> Result<CompressOutcome> {
    let original = std::fs::metadata(input)?.len();
    let tmp = temp_path(output);
    let result = (|| -> Result<CompressOutcome> {
        let mut level = recipe.level;
        let mut size = compress_once(input, &tmp, Some(level), recipe)?;
        if let Some(target) = recipe.target_bytes {
            let start_dpi = level.color_dpi;
            for (dpi, q) in LADDER.iter().copied().filter(|(d, _)| *d < start_dpi) {
                if size <= target {
                    break;
                }
                level = CompressLevel { color_dpi: dpi, gray_dpi: dpi, bitonal_dpi: level.bitonal_dpi, jpeg_quality: q };
                size = compress_once(input, &tmp, Some(level), recipe)?;
            }
        }
        // 重压效果不明显时，再试「只重新保存」，取更小的。图片很小、又被多页共用的文档
        // （如带截图的说明书），MuPDF 重写图片后对象反而变多，整体会比原文件大。
        if size as f64 > original as f64 * 0.9 {
            let plain = temp_path(&output.with_extension("plain.pdf"));
            match compress_once(input, &plain, None, recipe) {
                Ok(plain_size) if plain_size < size => {
                    std::fs::rename(&plain, &tmp)?;
                    size = plain_size;
                }
                _ => {
                    let _ = std::fs::remove_file(&plain);
                }
            }
        }
        // 省不到 2% 的不写出：对用户没有意义，只是多一个文件。
        if size as f64 > original as f64 * MIN_SAVING {
            let _ = std::fs::remove_file(&tmp);
            return Ok(CompressOutcome::KeptOriginal);
        }
        std::fs::rename(&tmp, output)?;
        Ok(CompressOutcome::Written { bytes: size, level })
    })();
    if result.is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
    result
}

/// 一页：第几个文件的第几页（都从 0 开始）。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PageRef {
    pub doc: u32,
    pub page: u32,
}

/// 按给定顺序挑页拼成新文件：合并、排序、删页、拆分、提取共用。
pub fn assemble(inputs: &[PathBuf], pages: &[PageRef], output: &Path, strip_metadata: bool) -> Result<()> {
    let cinputs: Vec<CString> = inputs.iter().map(|p| cpath(p)).collect();
    let ptrs: Vec<*const c_char> = cinputs.iter().map(|c| c.as_ptr()).collect();
    let docs: Vec<c_int> = pages.iter().map(|p| p.doc as c_int).collect();
    let idx: Vec<c_int> = pages.iter().map(|p| p.page as c_int).collect();
    let tmp = temp_path(output);
    let o = cpath(&tmp);
    let r = call(|e, n| unsafe {
        smaller_pdf_assemble(
            ptrs.as_ptr(), ptrs.len() as c_int, docs.as_ptr(), idx.as_ptr(), pages.len() as c_int,
            o.as_ptr(), strip_metadata as c_int, e, n,
        )
    })
    .and_then(|_| {
        let got = info(&tmp)?.pages as usize;
        if got != pages.len() {
            return Err(PdfError::Mupdf(format!("页数不一致：应为 {}，实际 {got}", pages.len())));
        }
        Ok(std::fs::rename(&tmp, output)?)
    });
    if r.is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
    r
}

/// 把多个文件按顺序整本合并。
pub fn merge(inputs: &[PathBuf], output: &Path, strip_metadata: bool) -> Result<()> {
    let mut pages = Vec::new();
    for (d, path) in inputs.iter().enumerate() {
        for p in 0..info(path)?.pages {
            pages.push(PageRef { doc: d as u32, page: p });
        }
    }
    assemble(inputs, &pages, output, strip_metadata)
}

/// 每 n 页拆成一个文件，返回写出的文件。命名「原名-1.pdf」「原名-2.pdf」……
pub fn split_every(input: &Path, n: u32, out_dir: &Path, strip_metadata: bool) -> Result<Vec<PathBuf>> {
    let total = info(input)?.pages;
    let stem = input.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_else(|| "document".into());
    let mut written = Vec::new();
    let inputs = vec![input.to_path_buf()];
    // 编号按份数补零（12 份写 01–12），访达里按名字排也是对的。
    let parts = total.div_ceil(n.max(1)).max(1);
    let digits = parts.to_string().len().max(2);
    for (k, start) in (0..total).step_by(n.max(1) as usize).enumerate() {
        let pages: Vec<PageRef> = (start..(start + n).min(total)).map(|p| PageRef { doc: 0, page: p }).collect();
        let out = out_dir.join(format!("{stem}-{:0digits$}.pdf", k + 1));
        assemble(&inputs, &pages, &out, strip_metadata)?;
        written.push(out);
    }
    Ok(written)
}

/// 按目标宽度渲染一页（白底）。
pub fn render_page(path: &Path, page: u32, width: u32) -> Result<Picture> {
    let p = cpath(path);
    let mut pixels = std::ptr::null_mut();
    let (mut w, mut h) = (0, 0);
    call(|e, n| unsafe { smaller_pdf_render(p.as_ptr(), page as c_int, width as c_int, &mut pixels, &mut w, &mut h, e, n) })?;
    let len = w as usize * h as usize * 4;
    let rgba = unsafe { std::slice::from_raw_parts(pixels, len).to_vec() };
    unsafe { smaller_pdf_free_pixels(pixels) };
    Ok(img::from_rgba(w as u32, h as u32, rgba, false, SourceFormat::VideoFrame))
}

// ---------- 图片转 PDF ----------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PageMode {
    /// 页面按图片尺寸，150 dpi 换算。
    FitImage,
    /// A4 竖版，图片等比放进页面并居中。
    A4,
}

/// 每张图一页。JPG / PNG 原样嵌入不重新压缩；其他格式（HEIC、WebP、AVIF……）
/// 先用 `decoder` 解码，再转成 JPEG（带透明的转 PNG）。
pub fn images_to_pdf(
    sources: &[PathBuf],
    mode: PageMode,
    output: &Path,
    decoder: &dyn Fn(&Path) -> std::result::Result<Picture, String>,
) -> Result<()> {
    let mut blobs: Vec<Vec<u8>> = Vec::with_capacity(sources.len());
    for src in sources {
        let ext = src.extension().map(|e| e.to_string_lossy().to_lowercase()).unwrap_or_default();
        if matches!(ext.as_str(), "jpg" | "jpeg" | "png") {
            blobs.push(std::fs::read(src)?);
        } else {
            let picture = decoder(src).map_err(PdfError::Mupdf)?;
            let format = if picture.has_alpha { img::OutputFormat::Png } else { img::OutputFormat::Jpeg };
            blobs.push(img::encode(&picture, format, 90, picture.has_alpha).map_err(|e| PdfError::Mupdf(e.to_string()))?);
        }
    }
    let ptrs: Vec<*const u8> = blobs.iter().map(|b| b.as_ptr()).collect();
    let lens: Vec<usize> = blobs.iter().map(|b| b.len()).collect();
    let tmp = temp_path(output);
    let o = cpath(&tmp);
    let mode = match mode {
        PageMode::FitImage => 0,
        PageMode::A4 => 1,
    };
    let r = call(|e, n| unsafe {
        smaller_pdf_from_images(ptrs.as_ptr(), lens.as_ptr(), ptrs.len() as c_int, mode, 150.0, o.as_ptr(), e, n)
    })
    .and_then(|_| Ok(std::fs::rename(&tmp, output)?));
    if r.is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
    r
}

// ---------- PDF 转长图 ----------

/// 逐页按同一宽度渲染后上下拼接；超过 `max_height` 时分成几张。
pub fn render_long_image(path: &Path, width: u32, max_height: u32) -> Result<Vec<Picture>> {
    let pages = info(path)?.pages;
    let mut parts = Vec::new();
    let mut rgba: Vec<u8> = Vec::new();
    let mut height = 0u32;
    for page in 0..pages {
        let p = render_page(path, page, width)?;
        if height > 0 && height + p.height > max_height {
            parts.push(img::from_rgba(width, height, std::mem::take(&mut rgba), false, SourceFormat::VideoFrame));
            height = 0;
        }
        // 单页本身就超过上限时照样单独成一张，交给编码器决定能不能写。
        rgba.extend_from_slice(&p.rgba);
        height += p.height;
    }
    if height > 0 {
        parts.push(img::from_rgba(width, height, rgba, false, SourceFormat::VideoFrame));
    }
    Ok(parts)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("smaller-pdf-test-{name}-{}", std::process::id()));
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    /// 把一张无损大图（PNG 原样嵌入，150 dpi）做成单页 PDF：图片为主、可压空间大。
    fn image_pdf(dir: &Path, name: &str, seed: u32) -> PathBuf {
        let (w, h) = (2400u32, 1600u32);
        let mut rgba = Vec::with_capacity((w * h * 4) as usize);
        for y in 0..h {
            for x in 0..w {
                let n = ((x * 7919 + y * 104729 + seed) % 251) as u8;
                rgba.extend_from_slice(&[(x * 255 / w) as u8 ^ (n / 6), (y * 255 / h) as u8, n / 2 + 60, 255]);
            }
        }
        let png = dir.join(format!("{name}.png"));
        image::save_buffer(&png, &rgba, w, h, image::ExtendedColorType::Rgba8).unwrap();
        let pdf = dir.join(format!("{name}.pdf"));
        images_to_pdf(&[png], PageMode::FitImage, &pdf, &|p| img::load(p).map_err(|e| e.to_string())).unwrap();
        pdf
    }

    #[test]
    fn 加密码与解锁() {
        let dir = scratch("password");
        let input = image_pdf(&dir, "plain", 1);
        let locked = dir.join("locked.pdf");
        std::fs::copy(&input, &locked).unwrap();
        encrypt_in_place(&locked, "1234").unwrap();
        assert!(info(&locked).unwrap().needs_password, "加密后应要求密码");
        let open = dir.join("open.pdf");
        assert!(matches!(unlock(&locked, &open, "9999"), Err(PdfError::WrongPassword)));
        unlock(&locked, &open, "1234").unwrap();
        let i = info(&open).unwrap();
        assert!(!i.needs_password && i.pages == 1);
    }

    #[test]
    fn 压缩图片为主的_pdf() {
        let dir = scratch("compress");
        let input = image_pdf(&dir, "brochure", 1);
        let output = dir.join("brochure-small.pdf");
        let recipe = PdfRecipe { level: CompressLevel::EMAIL, target_bytes: None, subset_fonts: true, strip_metadata: true, grayscale: false };
        let original = std::fs::metadata(&input).unwrap().len();
        match compress(&input, &output, &recipe).unwrap() {
            CompressOutcome::Written { bytes, .. } => {
                assert!(bytes * 3 < original, "{original} → {bytes}");
                assert_eq!(info(&output).unwrap().pages, 1);
            }
            other => panic!("应写出新文件：{other:?}"),
        }
        assert!(!temp_path(&output).exists());
    }

    #[test]
    fn 限体积逐档降() {
        let dir = scratch("target");
        let input = image_pdf(&dir, "big", 2);
        let output = dir.join("big-small.pdf");
        let recipe = PdfRecipe { level: CompressLevel::PRINT, target_bytes: Some(150_000), subset_fonts: false, strip_metadata: true, grayscale: false };
        match compress(&input, &output, &recipe).unwrap() {
            CompressOutcome::Written { bytes, level } => {
                assert!(bytes <= 150_000, "{bytes} 字节，档位 {level:?}");
                assert!(level.color_dpi < 300);
            }
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn 已经很小的文件保留原样() {
        let dir = scratch("keep");
        let input = image_pdf(&dir, "a", 3);
        let small = dir.join("a-small.pdf");
        compress(&input, &small, &PdfRecipe { level: CompressLevel::SCREEN, target_bytes: None, subset_fonts: false, strip_metadata: true, grayscale: false }).unwrap();
        let again = dir.join("a-again.pdf");
        let outcome = compress(&small, &again, &PdfRecipe { level: CompressLevel::PRINT, target_bytes: None, subset_fonts: false, strip_metadata: true, grayscale: false }).unwrap();
        assert_eq!(outcome, CompressOutcome::KeptOriginal);
        assert!(!again.exists());
    }

    #[test]
    fn 合并_排序_拆分_渲染() {
        let dir = scratch("assemble");
        let a = image_pdf(&dir, "a", 4);
        let b = image_pdf(&dir, "b", 5);
        let merged = dir.join("merged.pdf");
        merge(&[a.clone(), b.clone(), a.clone()], &merged, true).unwrap();
        assert_eq!(info(&merged).unwrap().pages, 3);

        // 倒序、去掉中间一页。
        let picked = dir.join("picked.pdf");
        assemble(&[merged.clone()], &[PageRef { doc: 0, page: 2 }, PageRef { doc: 0, page: 0 }], &picked, true).unwrap();
        assert_eq!(info(&picked).unwrap().pages, 2);

        let parts = split_every(&merged, 2, &dir, true).unwrap();
        assert_eq!(parts.len(), 2);
        assert_eq!(info(&parts[0]).unwrap().pages, 2);
        assert_eq!(info(&parts[1]).unwrap().pages, 1);
        assert_eq!(parts[0].file_name().unwrap().to_string_lossy(), "merged-01.pdf");

        let (w, h) = page_size(&a, 0).unwrap();
        assert!((w - 1152.0).abs() < 1.0 && (h - 768.0).abs() < 1.0, "{w}×{h}");
        let pic = render_page(&a, 0, 800).unwrap();
        // MuPDF 渲染时边界向外取整，高可能多 1 像素。
        assert_eq!(pic.width, 800);
        assert!((533..=534).contains(&pic.height), "{}", pic.height);

        let err = assemble(&[a], &[PageRef { doc: 0, page: 9 }], &dir.join("bad.pdf"), true).unwrap_err();
        assert!(err.to_string().contains("页码越界"), "{err}");
    }

    #[test]
    fn 转灰度() {
        let dir = scratch("gray");
        let input = image_pdf(&dir, "color", 7);
        let output = dir.join("gray.pdf");
        let recipe = PdfRecipe { level: CompressLevel::EMAIL, target_bytes: None, subset_fonts: false, strip_metadata: true, grayscale: true };
        assert!(matches!(compress(&input, &output, &recipe).unwrap(), CompressOutcome::Written { .. }));
        let p = render_page(&output, 0, 200).unwrap();
        assert!(p.rgba.chunks_exact(4).all(|c| c[0].abs_diff(c[1]) <= 2 && c[1].abs_diff(c[2]) <= 2), "还有彩色像素");
    }

    #[test]
    fn 图片转_pdf_与长图() {
        let dir = scratch("images");
        let mut sources = Vec::new();
        for (i, (w, h)) in [(600u32, 400u32), (300, 600)].iter().enumerate() {
            let rgba: Vec<u8> = (0..w * h).flat_map(|k| [(k % 251) as u8, (i * 90) as u8, 120, 255]).collect();
            let png = dir.join(format!("p{i}.png"));
            image::save_buffer(&png, &rgba, *w, *h, image::ExtendedColorType::Rgba8).unwrap();
            sources.push(png);
        }
        // 第三张是 WebP，要先转码再嵌入。
        let webp = dir.join("p2.webp");
        std::fs::write(&webp, img::encode(&img::load(&sources[0]).unwrap(), img::OutputFormat::Webp, 80, false).unwrap()).unwrap();
        sources.push(webp);

        let fit = dir.join("fit.pdf");
        images_to_pdf(&sources, PageMode::FitImage, &fit, &|p| img::load(p).map_err(|e| e.to_string())).unwrap();
        assert_eq!(info(&fit).unwrap().pages, 3);
        let (w, h) = page_size(&fit, 1).unwrap();
        assert!((w - 144.0).abs() < 0.5 && (h - 288.0).abs() < 0.5, "{w}×{h}");

        let a4 = dir.join("a4.pdf");
        images_to_pdf(&sources, PageMode::A4, &a4, &|p| img::load(p).map_err(|e| e.to_string())).unwrap();
        let (w, h) = page_size(&a4, 0).unwrap();
        assert!((w - 595.3).abs() < 0.5 && (h - 841.9).abs() < 0.5);

        let parts = render_long_image(&fit, 300, 100_000).unwrap();
        assert_eq!(parts.len(), 1);
        assert_eq!(parts[0].width, 300);
        assert!(parts[0].height > 900, "{}", parts[0].height);
        let split = render_long_image(&fit, 300, 700).unwrap();
        assert!(split.len() >= 2 && split.iter().all(|p| p.width == 300));
    }
}
