//! 图片编码器实测。
//!
//! 用法：cargo run --release -p bench-image -- <图片目录>...
//! 每张图按质量 20–95 扫一遍，记录体积与 SSIMULACRA2 分数（越高越接近原图，
//! 70 分以上算好、80 分以上算高），再插值出各编码器达到 70 / 80 分需要的体积。
//! 对照组：macOS ImageIO（sips，等同「预览」导出）与普通 JPEG 编码（image crate）。

use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Instant;

use smaller_core::image::{self as img, OutputFormat, Picture};
use ssimulacra2::{ColorPrimaries, Rgb, TransferCharacteristic, compute_frame_ssimulacra2};

#[derive(Clone, Copy, Debug, PartialEq)]
enum Encoder {
    Jpegli,
    JpegBasic,
    SipsJpeg,
    Webp,
    Avif,
    SipsHeic,
}

impl Encoder {
    fn name(self) -> &'static str {
        match self {
            Encoder::Jpegli => "JPEG · jpegli（本工具）",
            Encoder::JpegBasic => "JPEG · 普通编码",
            Encoder::SipsJpeg => "JPEG · macOS 预览",
            Encoder::Webp => "WebP · libwebp（本工具）",
            Encoder::Avif => "AVIF · aom iq（本工具）",
            Encoder::SipsHeic => "HEIC · macOS 预览",
        }
    }
}

const ENCODERS: [Encoder; 6] = [Encoder::Jpegli, Encoder::JpegBasic, Encoder::SipsJpeg, Encoder::Webp, Encoder::Avif, Encoder::SipsHeic];

struct Sample {
    bytes: usize,
    score: f64,
}

fn to_rgb(p: &Picture) -> Rgb {
    let data = p
        .rgba
        .chunks_exact(4)
        .map(|c| [c[0] as f32 / 255.0, c[1] as f32 / 255.0, c[2] as f32 / 255.0])
        .collect();
    Rgb::new(data, p.width as usize, p.height as usize, TransferCharacteristic::SRGB, ColorPrimaries::BT709).unwrap()
}

fn sips(src: &Path, format: &str, quality: u8, tmp: &Path) -> Vec<u8> {
    let out = tmp.join(format!("sips.{format}"));
    let status = Command::new("sips")
        .args(["-s", "format", format, "-s", "formatOptions", &quality.to_string()])
        .arg(src)
        .arg("--out")
        .arg(&out)
        .output()
        .expect("sips 运行失败");
    assert!(status.status.success(), "sips 失败");
    std::fs::read(&out).unwrap()
}

/// sips 输出的 HEIC 由系统解码回 PNG 再打分。
fn sips_decode(data: &[u8], ext: &str, tmp: &Path) -> Picture {
    let src = tmp.join(format!("in.{ext}"));
    let out = tmp.join("back.png");
    std::fs::write(&src, data).unwrap();
    Command::new("sips").args(["-s", "format", "png"]).arg(&src).arg("--out").arg(&out).output().unwrap();
    img::load(&out).unwrap()
}

fn encode(enc: Encoder, src_path: &Path, pic: &Picture, q: u8, tmp: &Path) -> (Vec<u8>, Picture) {
    match enc {
        Encoder::Jpegli => {
            let b = img::encode(pic, OutputFormat::Jpeg, q, false).unwrap();
            let d = img::decode(&b).unwrap();
            (b, d)
        }
        Encoder::Webp => {
            let b = img::encode(pic, OutputFormat::Webp, q, false).unwrap();
            let d = img::decode(&b).unwrap();
            (b, d)
        }
        Encoder::Avif => {
            let b = img::encode(pic, OutputFormat::Avif, q, false).unwrap();
            let d = img::decode(&b).unwrap();
            (b, d)
        }
        Encoder::JpegBasic => {
            let mut b = Vec::new();
            let rgb: Vec<u8> = pic.rgba.chunks_exact(4).flat_map(|c| [c[0], c[1], c[2]]).collect();
            image::codecs::jpeg::JpegEncoder::new_with_quality(&mut b, q)
                .encode(&rgb, pic.width, pic.height, image::ExtendedColorType::Rgb8)
                .unwrap();
            let d = img::decode(&b).unwrap();
            (b, d)
        }
        Encoder::SipsJpeg => {
            let b = sips(src_path, "jpeg", q, tmp);
            let d = img::decode(&b).unwrap();
            (b, d)
        }
        Encoder::SipsHeic => {
            let b = sips(src_path, "heic", q, tmp);
            let d = sips_decode(&b, "heic", tmp);
            (b, d)
        }
    }
}

/// 在 (体积, 分数) 曲线上插值出达到 target 分需要的体积。达不到返回 None。
fn bytes_at(samples: &[Sample], target: f64) -> Option<f64> {
    let mut s: Vec<&Sample> = samples.iter().collect();
    s.sort_by_key(|x| x.bytes);
    for w in s.windows(2) {
        let (a, b) = (w[0], w[1]);
        if a.score < target && b.score >= target {
            let t = (target - a.score) / (b.score - a.score);
            return Some(a.bytes as f64 + t * (b.bytes as f64 - a.bytes as f64));
        }
    }
    s.first().filter(|x| x.score >= target).map(|x| x.bytes as f64)
}

fn main() {
    let dirs: Vec<PathBuf> = std::env::args().skip(1).map(PathBuf::from).collect();
    assert!(!dirs.is_empty(), "用法：bench-image <图片目录>...");
    let tmp = std::env::temp_dir().join("smaller-bench");
    std::fs::create_dir_all(&tmp).unwrap();
    let qualities: Vec<u8> = (20..=95).step_by(5).collect();

    for dir in dirs {
        let mut files: Vec<PathBuf> = std::fs::read_dir(&dir)
            .unwrap()
            .filter_map(|e| e.ok().map(|e| e.path()))
            .filter(|p| p.extension().is_some_and(|e| e == "png"))
            .collect();
        files.sort();
        // 每个编码器：[70 分体积合计, 80 分体积合计, 达不到的张数, 编码耗时合计]
        let mut totals = vec![[0f64; 4]; ENCODERS.len()];
        let mut pixels = 0f64;
        for f in &files {
            let name = f.file_name().unwrap().to_string_lossy().to_string();
            let mut pic = img::load(f).unwrap();
            let mut source_path = f.clone();
            // 透明图先铺白底再比：打分比的是人眼实际看到的画面，否则 JPEG 铺白的角永远对不上。
            // sips 也要读铺白后的图，所以另存一份。
            if pic.has_alpha {
                for p in pic.rgba.chunks_exact_mut(4) {
                    let a = p[3] as u32;
                    for c in &mut p[..3] {
                        *c = ((*c as u32 * a + 255 * (255 - a) + 127) / 255) as u8;
                    }
                    p[3] = 255;
                }
                pic.has_alpha = false;
                source_path = tmp.join("flat.png");
                image::save_buffer(&source_path, &pic.rgba, pic.width, pic.height, image::ExtendedColorType::Rgba8).unwrap();
            }
            let f = &source_path;
            let src = to_rgb(&pic);
            pixels += (pic.width * pic.height) as f64;
            for (i, &enc) in ENCODERS.iter().enumerate() {
                let mut samples = Vec::new();
                for &q in &qualities {
                    let t = Instant::now();
                    let (bytes, decoded) = encode(enc, f, &pic, q, &tmp);
                    totals[i][3] += t.elapsed().as_secs_f64();
                    let score = compute_frame_ssimulacra2(src.clone(), to_rgb(&decoded)).unwrap();
                    samples.push(Sample { bytes: bytes.len(), score });
                }
                for (k, target) in [70.0, 80.0].iter().enumerate() {
                    match bytes_at(&samples, *target) {
                        Some(b) => totals[i][k] += b,
                        None => totals[i][2] += 1.0,
                    }
                }
            }
            eprintln!("  ✓ {name}");
        }

        let base70 = totals[1][0];
        let base80 = totals[1][1];
        println!("\n## {}（{} 张）\n", dir.display(), files.len());
        println!("| 编码器 | 70 分 bpp | 相对普通 JPEG | 80 分 bpp | 相对普通 JPEG | 单张耗时（每档） | 达不到的次数 |");
        println!("|---|---|---|---|---|---|---|");
        for (i, &enc) in ENCODERS.iter().enumerate() {
            let t = totals[i];
            let bpp70 = t[0] * 8.0 / pixels;
            let bpp80 = t[1] * 8.0 / pixels;
            let per = t[3] / (files.len() * qualities.len()) as f64;
            println!(
                "| {} | {:.3} | {:+.0}% | {:.3} | {:+.0}% | {:.0} ms | {} |",
                enc.name(), bpp70, (t[0] / base70 - 1.0) * 100.0, bpp80, (t[1] / base80 - 1.0) * 100.0, per * 1000.0, t[2]
            );
        }
    }
}
