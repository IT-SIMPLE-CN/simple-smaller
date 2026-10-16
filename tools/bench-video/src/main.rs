//! 视频编码实测。
//!
//! 用法：cargo run --release -p bench-video -- <视频>...
//! 每段素材压到几档目标体积，比较 x264 两遍编码与 VideoToolbox H.264 / HEVC 的
//! 实际体积、画质（SSIM，换算成 dB：-10·log10(1-SSIM)，越高越好）与耗时。

use std::path::Path;
use std::process::Command;
use std::sync::atomic::AtomicBool;
use std::time::Instant;

use smaller_core::video::{self, EncoderChoice, Rate, Tools, VideoCodec, VideoRecipe};

fn ssim(tools: &Tools, distorted: &Path, reference: &Path) -> f64 {
    let out = Command::new(&tools.ffmpeg)
        .args(["-hide_banner", "-nostdin", "-i"])
        .arg(distorted)
        .arg("-i")
        .arg(reference)
        .args(["-lavfi", "[0:v][1:v]ssim", "-f", "null", "-"])
        .output()
        .unwrap();
    let log = String::from_utf8_lossy(&out.stderr);
    let all = log.rsplit("All:").next().unwrap().split_whitespace().next().unwrap();
    all.parse().unwrap()
}

fn main() {
    let tools = Tools::locate().expect("找不到 ffmpeg");
    let targets_mb = [2.5f64, 6.0, 12.0];
    let encoders = [
        ("H.264 · x264 两遍（本工具默认）", VideoCodec::H264, EncoderChoice::Quality),
        ("H.264 · VideoToolbox 硬件", VideoCodec::H264, EncoderChoice::Fast),
        ("HEVC · VideoToolbox 硬件", VideoCodec::Hevc, EncoderChoice::Fast),
    ];
    let tmp = std::env::temp_dir().join("smaller-bench-video");
    std::fs::create_dir_all(&tmp).unwrap();

    for input in std::env::args().skip(1) {
        let input = Path::new(&input);
        let info = video::probe(&tools, input).unwrap();
        println!(
            "\n## {}（{}×{}，{:.1} 秒，原码率 {:.1} Mbps）\n",
            input.file_name().unwrap().to_string_lossy(),
            info.width, info.height, info.duration, info.bitrate as f64 / 1e6
        );
        println!("| 目标体积 | 编码方式 | 实际体积 | SSIM (dB) | 耗时 |");
        println!("|---|---|---|---|---|");
        for mb in targets_mb {
            let target = (mb * 1_000_000.0) as u64;
            for (name, codec, encoder) in encoders {
                let recipe = VideoRecipe {
                    codec,
                    encoder,
                    rate: Rate::TargetBytes(target),
                    max_short_edge: None,
                    keep_audio: false,
                    trim: None,
                    crop: None,
                    hdr_to_sdr: false,
                    max_fps: None,
                };
                let out = tmp.join("out.mp4");
                let t = Instant::now();
                video::transcode(&tools, input, &out, &info, &recipe, &mut |_| {}, &AtomicBool::new(false)).unwrap();
                let secs = t.elapsed().as_secs_f64();
                let size = std::fs::metadata(&out).unwrap().len() as f64 / 1e6;
                let s = ssim(&tools, &out, input);
                let db = -10.0 * (1.0 - s).log10();
                println!("| {mb} MB | {name} | {size:.2} MB | {db:.2} | {secs:.1} 秒 |");
                eprintln!("  ✓ {mb} MB {name}");
            }
        }
    }
}
