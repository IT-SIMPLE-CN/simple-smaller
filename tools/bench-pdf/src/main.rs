//! PDF 压缩实测。
//!
//! 用法：cargo run --release -p bench-pdf -- <文字提取工具> <PDF>...
//! 文字提取工具是一个用 PDFKit 打印全部页文字的小程序（见 docs/bench）。
//! 每份 PDF 跑屏幕阅读 / 邮件附件 / 打印保真三档，记录体积、耗时、
//! 文字是否一字不差，以及每页按 150 dpi（屏幕阅读的清晰度）渲染后与原文件的 SSIMULACRA2 最低分。

use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Instant;

use smaller_core::image::Picture;
use smaller_core::pdf::{self, CompressLevel, CompressOutcome, PdfRecipe};
use ssimulacra2::{ColorPrimaries, Rgb, TransferCharacteristic, compute_frame_ssimulacra2};

fn text(tool: &Path, pdf: &Path) -> String {
    let out = Command::new(tool).arg(pdf).output().unwrap();
    String::from_utf8_lossy(&out.stdout).into_owned()
}

fn rgb(p: &Picture) -> Rgb {
    let data = p.rgba.chunks_exact(4).map(|c| [c[0] as f32 / 255.0, c[1] as f32 / 255.0, c[2] as f32 / 255.0]).collect();
    Rgb::new(data, p.width as usize, p.height as usize, TransferCharacteristic::SRGB, ColorPrimaries::BT709).unwrap()
}

fn worst_page_score(a: &Path, b: &Path, pages: u32) -> f64 {
    let mut worst = 100.0f64;
    for page in 0..pages {
        let (w_pt, _) = pdf::page_size(a, page).unwrap();
        let width = (w_pt / 72.0 * 150.0).round() as u32;
        let (x, y) = (pdf::render_page(a, page, width).unwrap(), pdf::render_page(b, page, width).unwrap());
        if (x.width, x.height) != (y.width, y.height) {
            return f64::NAN;
        }
        worst = worst.min(compute_frame_ssimulacra2(rgb(&x), rgb(&y)).unwrap());
    }
    worst
}

fn main() {
    let mut args = std::env::args().skip(1);
    let first = args.next().expect("缺文字提取工具");
    // --merge <输出> <输入>...：用引擎的合并功能拼测试素材。
    if first == "--merge" {
        let output = PathBuf::from(args.next().unwrap());
        let inputs: Vec<PathBuf> = args.map(PathBuf::from).collect();
        pdf::merge(&inputs, &output, true).unwrap();
        println!("{} 页 → {}", pdf::info(&output).unwrap().pages, output.display());
        return;
    }
    let tool = PathBuf::from(first);
    let tmp = std::env::temp_dir().join("smaller-bench-pdf");
    std::fs::create_dir_all(&tmp).unwrap();
    let levels = [("屏幕阅读", CompressLevel::SCREEN), ("邮件附件", CompressLevel::EMAIL), ("打印保真", CompressLevel::PRINT)];

    for input in args {
        let input = PathBuf::from(input);
        let info = pdf::info(&input).unwrap();
        let original = std::fs::metadata(&input).unwrap().len() as f64;
        let original_text = text(&tool, &input);
        println!(
            "\n## {}（{} 页，{:.2} MB，文字 {} 字）\n",
            input.file_name().unwrap().to_string_lossy(), info.pages, original / 1e6, original_text.chars().count()
        );
        println!("| 档位 | 输出体积 | 变化 | 文字 | 页面画质最低分 | 耗时 |");
        println!("|---|---|---|---|---|---|");
        for (name, level) in levels {
            let out = tmp.join("out.pdf");
            let _ = std::fs::remove_file(&out);
            let recipe = PdfRecipe { level, target_bytes: None, subset_fonts: true, strip_metadata: true };
            let t = Instant::now();
            let outcome = pdf::compress(&input, &out, &recipe).unwrap();
            let secs = t.elapsed().as_secs_f64();
            match outcome {
                CompressOutcome::KeptOriginal => println!("| {name} | 保留原文件 | — | — | — | {secs:.1} 秒 |"),
                CompressOutcome::Written { bytes, .. } => {
                    let same = text(&tool, &out) == original_text;
                    let score = worst_page_score(&input, &out, info.pages);
                    println!(
                        "| {name} | {:.2} MB | {:+.0}% | {} | {:.1} | {secs:.1} 秒 |",
                        bytes as f64 / 1e6,
                        (bytes as f64 / original - 1.0) * 100.0,
                        if same { "一字不差" } else { "有变化" },
                        score
                    );
                }
            }
            eprintln!("  ✓ {name}");
        }
    }
}
