//! 视频管线：调用随包的 ffmpeg / ffprobe（独立进程）。
//!
//! - 转码：截取 → 裁切 → HDR 转 SDR → 缩放（宽高取偶数）→ 编码。
//!   H.264 默认 x264；限体积走两遍编码，超出目标再降码率重压。
//!   HEVC 只用系统硬件编码（规避专利，不内置 x265）。
//! - 静态帧（序列帧、GIF）：ffmpeg 输出原始 RGBA，交给图片管线或 gifski。
//! - 输出先写同目录的隐藏临时文件，完成后改名；失败或取消时删掉临时文件。

use std::io::{BufRead, BufReader, Read};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};

use serde::Deserialize;

use crate::image::{self as img, Picture, SourceFormat};

#[derive(Debug, thiserror::Error)]
pub enum VideoError {
    #[error("找不到 ffmpeg：{0}")]
    Tool(String),
    #[error("读不懂这个视频：{0}")]
    Probe(String),
    #[error("转码失败：{0}")]
    Encode(String),
    #[error("已取消")]
    Cancelled,
    #[error("读写失败：{0}")]
    Io(#[from] std::io::Error),
}

type Result<T> = std::result::Result<T, VideoError>;

// ---------- 工具定位 ----------

pub struct Tools {
    pub ffmpeg: PathBuf,
    pub ffprobe: PathBuf,
}

impl Tools {
    /// 依次找：环境变量 SMALLER_FFMPEG_DIR → 应用包 Contents/Resources/bin → 可执行文件旁边 → 开发目录 .local/ffmpeg。
    pub fn locate() -> Result<Self> {
        let mut dirs: Vec<PathBuf> = Vec::new();
        if let Ok(dir) = std::env::var("SMALLER_FFMPEG_DIR") {
            dirs.push(dir.into());
        }
        if let Ok(exe) = std::env::current_exe()
            && let Some(parent) = exe.parent()
        {
            dirs.push(parent.join("../Resources/bin"));
            dirs.push(parent.to_path_buf());
        }
        let arch = if cfg!(target_arch = "aarch64") { "arm64" } else { "x86_64" };
        dirs.push(Path::new(env!("CARGO_MANIFEST_DIR")).join("../.local/ffmpeg").join(arch).join("bin"));
        for dir in dirs {
            let (ffmpeg, ffprobe) = (dir.join("ffmpeg"), dir.join("ffprobe"));
            if ffmpeg.is_file() && ffprobe.is_file() {
                return Ok(Tools { ffmpeg, ffprobe });
            }
        }
        Err(VideoError::Tool("先运行 scripts/build-ffmpeg-mac.sh".into()))
    }
}

// ---------- 探测 ----------

#[derive(Debug, Clone)]
pub struct VideoInfo {
    pub duration: f64,
    /// 按旋转修正后的显示宽高。
    pub width: u32,
    pub height: u32,
    pub fps: f64,
    pub codec: String,
    /// 整体码率（bit/s），读不到时为 0。
    pub bitrate: u64,
    pub has_audio: bool,
    pub hdr: bool,
}

#[derive(Deserialize)]
struct ProbeOutput {
    streams: Vec<ProbeStream>,
    format: ProbeFormat,
}

#[derive(Deserialize)]
struct ProbeStream {
    codec_type: String,
    codec_name: Option<String>,
    width: Option<u32>,
    height: Option<u32>,
    avg_frame_rate: Option<String>,
    r_frame_rate: Option<String>,
    color_transfer: Option<String>,
    #[serde(default)]
    side_data_list: Vec<serde_json::Value>,
}

#[derive(Deserialize)]
struct ProbeFormat {
    duration: Option<String>,
    bit_rate: Option<String>,
}

fn parse_rate(s: &str) -> Option<f64> {
    let (a, b) = s.split_once('/')?;
    let (a, b): (f64, f64) = (a.parse().ok()?, b.parse().ok()?);
    (b > 0.0 && a > 0.0).then_some(a / b)
}

pub fn probe(tools: &Tools, path: &Path) -> Result<VideoInfo> {
    let out = Command::new(&tools.ffprobe)
        .args(["-v", "error", "-print_format", "json", "-show_format", "-show_streams"])
        .arg(path)
        .output()?;
    if !out.status.success() {
        return Err(VideoError::Probe(String::from_utf8_lossy(&out.stderr).trim().to_string()));
    }
    let parsed: ProbeOutput = serde_json::from_slice(&out.stdout).map_err(|e| VideoError::Probe(e.to_string()))?;
    let video = parsed
        .streams
        .iter()
        .find(|s| s.codec_type == "video")
        .ok_or_else(|| VideoError::Probe("没有视频轨".into()))?;
    let rotation = video
        .side_data_list
        .iter()
        .find_map(|v| v.get("rotation").and_then(|r| r.as_f64()))
        .unwrap_or(0.0);
    let (mut width, mut height) = (video.width.unwrap_or(0), video.height.unwrap_or(0));
    if (rotation.abs() - 90.0).abs() < 1.0 {
        std::mem::swap(&mut width, &mut height);
    }
    let fps = video
        .avg_frame_rate
        .as_deref()
        .and_then(parse_rate)
        .or_else(|| video.r_frame_rate.as_deref().and_then(parse_rate))
        .unwrap_or(30.0);
    let hdr = matches!(video.color_transfer.as_deref(), Some("smpte2084") | Some("arib-std-b67"));
    Ok(VideoInfo {
        duration: parsed.format.duration.as_deref().and_then(|d| d.parse().ok()).unwrap_or(0.0),
        width,
        height,
        fps,
        codec: video.codec_name.clone().unwrap_or_default(),
        bitrate: parsed.format.bit_rate.as_deref().and_then(|b| b.parse().ok()).unwrap_or(0),
        has_audio: parsed.streams.iter().any(|s| s.codec_type == "audio"),
        hdr,
    })
}

// ---------- 配方 ----------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VideoCodec {
    H264,
    Hevc,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EncoderChoice {
    /// 画质优先：x264 软件编码（只对 H.264 有效）。
    Quality,
    /// 速度优先：系统硬件编码。
    Fast,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Rate {
    High,
    Medium,
    Low,
    /// 限体积（字节）。
    TargetBytes(u64),
}

/// 裁切框，显示坐标（已按旋转转正）。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Crop {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone)]
pub struct VideoRecipe {
    pub codec: VideoCodec,
    pub encoder: EncoderChoice,
    pub rate: Rate,
    /// 短边上限，如 1080 / 720。竖屏 1080×1920 的短边是 1080。
    pub max_short_edge: Option<u32>,
    pub keep_audio: bool,
    /// 截取（秒）：开始、结束。
    pub trim: Option<(f64, f64)>,
    pub crop: Option<Crop>,
    pub hdr_to_sdr: bool,
    pub max_fps: Option<f64>,
}

const AUDIO_KBPS: u64 = 128;

fn even(v: f64) -> u32 {
    ((v / 2.0).round() as u32 * 2).max(2)
}

/// 裁切与缩放后的输出宽高。
fn output_size(info: &VideoInfo, crop: Option<Crop>, max_short_edge: Option<u32>) -> (u32, u32) {
    let (w, h) = crop.map(|c| (c.width, c.height)).unwrap_or((info.width, info.height));
    let short = w.min(h) as f64;
    let scale = match max_short_edge {
        Some(limit) if (limit as f64) < short => limit as f64 / short,
        _ => 1.0,
    };
    (even(w as f64 * scale), even(h as f64 * scale))
}

/// 拼滤镜链。`pixel_format` 为空时不追加格式转换（输出 RGBA 帧时由调用方指定）。
fn filter_chain(info: &VideoInfo, crop: Option<Crop>, hdr_to_sdr: bool, size: (u32, u32), fps: Option<f64>, pixel_format: &str) -> String {
    let mut parts: Vec<String> = Vec::new();
    if let Some(c) = crop {
        parts.push(format!("crop={}:{}:{}:{}", c.width, c.height, c.x, c.y));
    }
    if hdr_to_sdr && info.hdr {
        parts.push(
            "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv"
                .into(),
        );
    }
    if let Some(fps) = fps {
        parts.push(format!("fps={fps}"));
    }
    parts.push(format!("scale={}:{}:flags=lanczos", size.0, size.1));
    if !pixel_format.is_empty() {
        parts.push(format!("format={pixel_format}"));
    }
    parts.join(",")
}

fn input_args(input: &Path, trim: Option<(f64, f64)>) -> Vec<String> {
    let mut args = vec!["-hide_banner".to_string(), "-nostdin".into(), "-y".into()];
    if let Some((start, end)) = trim {
        args.extend(["-ss".into(), format!("{start:.3}"), "-t".into(), format!("{:.3}", (end - start).max(0.1))]);
    }
    args.extend(["-i".into(), input.to_string_lossy().into_owned()]);
    args
}

fn temp_path(output: &Path) -> PathBuf {
    let name = output.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    output.with_file_name(format!(".{name}.smaller-tmp"))
}

/// 跑一次 ffmpeg，读 `-progress` 输出换算成 [from, to] 区间的进度；取消时结束进程。
fn run_ffmpeg(
    tools: &Tools,
    args: &[String],
    duration: f64,
    span: (f64, f64),
    progress: &mut dyn FnMut(f64),
    cancel: &AtomicBool,
) -> Result<()> {
    let mut child: Child = Command::new(&tools.ffmpeg)
        .args(args)
        .args(["-progress", "pipe:1", "-nostats", "-loglevel", "error"])
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    let stdout = child.stdout.take().unwrap();
    let mut stderr = child.stderr.take().unwrap();
    let err_thread = std::thread::spawn(move || {
        let mut s = String::new();
        let _ = stderr.read_to_string(&mut s);
        s
    });
    for line in BufReader::new(stdout).lines() {
        if cancel.load(Ordering::Relaxed) {
            let _ = child.kill();
            let _ = child.wait();
            return Err(VideoError::Cancelled);
        }
        let Ok(line) = line else { break };
        if let Some(us) = line.strip_prefix("out_time_us=")
            && let Ok(us) = us.parse::<f64>()
            && duration > 0.0
        {
            let f = (us / 1e6 / duration).clamp(0.0, 1.0);
            progress(span.0 + (span.1 - span.0) * f);
        }
    }
    let status = child.wait()?;
    let stderr = err_thread.join().unwrap_or_default();
    if cancel.load(Ordering::Relaxed) {
        return Err(VideoError::Cancelled);
    }
    if !status.success() {
        return Err(VideoError::Encode(stderr.trim().lines().last().unwrap_or("ffmpeg 失败").to_string()));
    }
    Ok(())
}

/// 码率倒推：目标体积留 3% 余量，扣掉音频。返回 kbps。
fn target_video_kbps(target_bytes: u64, duration: f64, audio: bool) -> u64 {
    let total_kbps = target_bytes as f64 * 8.0 / 1000.0 / duration.max(0.1) * 0.97;
    let audio_kbps = if audio { AUDIO_KBPS as f64 } else { 0.0 };
    (total_kbps - audio_kbps).max(80.0) as u64
}

pub fn transcode(
    tools: &Tools,
    input: &Path,
    output: &Path,
    info: &VideoInfo,
    recipe: &VideoRecipe,
    progress: &mut dyn FnMut(f64),
    cancel: &AtomicBool,
) -> Result<()> {
    let duration = recipe.trim.map(|(s, e)| e - s).unwrap_or(info.duration);
    let size = output_size(info, recipe.crop, recipe.max_short_edge);
    let fps = recipe.max_fps.filter(|m| *m < info.fps - 0.01);
    let vf = filter_chain(info, recipe.crop, recipe.hdr_to_sdr, size, fps, "yuv420p");
    let audio = recipe.keep_audio && info.has_audio;
    let audio_args: Vec<String> = if audio {
        vec!["-c:a".into(), "aac_at".into(), "-b:a".into(), format!("{AUDIO_KBPS}k")]
    } else {
        vec!["-an".into()]
    };
    let tmp = temp_path(output);
    let common_tail = |extra: Vec<String>| -> Vec<String> {
        let mut a = input_args(input, recipe.trim);
        a.extend(["-map".into(), "0:v:0".into()]);
        if audio {
            a.extend(["-map".into(), "0:a:0".into()]);
        }
        a.extend(["-vf".into(), vf.clone()]);
        a.extend(extra);
        a.extend(audio_args.clone());
        // 去掉位置等元数据；文件头前置，网页可边下边播。
        a.extend(["-map_metadata".into(), "-1".into(), "-movflags".into(), "+faststart".into()]);
        a.extend(["-colorspace".into(), "bt709".into(), "-color_primaries".into(), "bt709".into(), "-color_trc".into(), "bt709".into()]);
        a.extend(["-f".into(), "mp4".into(), tmp.to_string_lossy().into_owned()]);
        a
    };

    let software = recipe.codec == VideoCodec::H264 && recipe.encoder == EncoderChoice::Quality;
    let result = (|| -> Result<()> {
        match (software, recipe.rate) {
            (true, Rate::TargetBytes(target)) => {
                let mut kbps = target_video_kbps(target, duration, audio);
                if info.bitrate > 0 {
                    kbps = kbps.min(info.bitrate / 1000);
                }
                let passlog = std::env::temp_dir().join(format!("smaller-x264-{}", std::process::id()));
                let passlog = passlog.to_string_lossy().into_owned();
                // 第一遍只分析画面，不出文件。
                let mut first = input_args(input, recipe.trim);
                first.extend(["-map".into(), "0:v:0".into(), "-vf".into(), vf.clone()]);
                first.extend(x264_args(&format!("{kbps}k")));
                first.extend(["-pass".into(), "1".into(), "-passlogfile".into(), passlog.clone(), "-an".into(), "-f".into(), "null".into(), "-".into()]);
                run_ffmpeg(tools, &first, duration, (0.0, 0.4), progress, cancel)?;
                for _attempt in 0..2 {
                    let mut second = x264_args(&format!("{kbps}k"));
                    second.extend(["-pass".into(), "2".into(), "-passlogfile".into(), passlog.clone()]);
                    run_ffmpeg(tools, &common_tail(second), duration, (0.4, 1.0), progress, cancel)?;
                    let got = std::fs::metadata(&tmp)?.len();
                    if got <= target {
                        break;
                    }
                    kbps = ((kbps as f64) * target as f64 / got as f64 * 0.97) as u64;
                }
                cleanup_passlog(&passlog);
                Ok(())
            }
            (true, rate) => {
                let crf = match rate {
                    Rate::High => "20",
                    Rate::Medium => "23",
                    _ => "27",
                };
                let mut extra = vec!["-c:v".into(), "libx264".into(), "-preset".into(), "slow".into(), "-crf".into(), crf.into()];
                extra.extend(["-profile:v".into(), "high".into(), "-pix_fmt".into(), "yuv420p".into()]);
                run_ffmpeg(tools, &common_tail(extra), duration, (0.0, 1.0), progress, cancel)
            }
            (false, rate) => {
                let (name, tag) = match recipe.codec {
                    VideoCodec::H264 => ("h264_videotoolbox", "avc1"),
                    VideoCodec::Hevc => ("hevc_videotoolbox", "hvc1"),
                };
                let base = vec!["-c:v".to_string(), name.into(), "-tag:v".into(), tag.into(), "-allow_sw".into(), "0".into()];
                match rate {
                    Rate::TargetBytes(target) => {
                        let mut kbps = target_video_kbps(target, duration, audio);
                        for _attempt in 0..2 {
                            let mut extra = base.clone();
                            extra.extend(["-b:v".into(), format!("{kbps}k"), "-maxrate".into(), format!("{}k", kbps * 3 / 2), "-bufsize".into(), format!("{}k", kbps * 2)]);
                            run_ffmpeg(tools, &common_tail(extra), duration, (0.0, 1.0), progress, cancel)?;
                            let got = std::fs::metadata(&tmp)?.len();
                            if got <= target {
                                break;
                            }
                            kbps = ((kbps as f64) * target as f64 / got as f64 * 0.95) as u64;
                        }
                        Ok(())
                    }
                    rate => {
                        // Apple Silicon 的恒定质量模式，1–100。
                        let q = match rate {
                            Rate::High => "65",
                            Rate::Medium => "55",
                            _ => "45",
                        };
                        let mut extra = base;
                        extra.extend(["-q:v".into(), q.into()]);
                        run_ffmpeg(tools, &common_tail(extra), duration, (0.0, 1.0), progress, cancel)
                    }
                }
            }
        }
    })();
    match result {
        Ok(()) => {
            std::fs::rename(&tmp, output)?;
            progress(1.0);
            Ok(())
        }
        Err(e) => {
            let _ = std::fs::remove_file(&tmp);
            Err(e)
        }
    }
}

fn x264_args(bitrate: &str) -> Vec<String> {
    ["-c:v", "libx264", "-preset", "slow", "-b:v", bitrate, "-profile:v", "high", "-pix_fmt", "yuv420p"]
        .iter()
        .map(|s| s.to_string())
        .collect()
}

fn cleanup_passlog(prefix: &str) {
    for suffix in ["-0.log", "-0.log.mbtree", "-0.log.temp", "-0.log.mbtree.temp"] {
        let _ = std::fs::remove_file(format!("{prefix}{suffix}"));
    }
}

/// 只去掉声音：视频流原样复制，不重新编码，几秒完成。
pub fn strip_audio(tools: &Tools, input: &Path, output: &Path, info: &VideoInfo, progress: &mut dyn FnMut(f64), cancel: &AtomicBool) -> Result<()> {
    let tmp = temp_path(output);
    let mut args = input_args(input, None);
    args.extend(["-map", "0:v:0", "-c", "copy", "-an", "-map_metadata", "-1", "-movflags", "+faststart", "-f", "mp4"].map(String::from));
    args.push(tmp.to_string_lossy().into_owned());
    match run_ffmpeg(tools, &args, info.duration, (0.0, 1.0), progress, cancel) {
        Ok(()) => {
            std::fs::rename(&tmp, output)?;
            progress(1.0);
            Ok(())
        }
        Err(e) => {
            let _ = std::fs::remove_file(&tmp);
            Err(e)
        }
    }
}

/// 取某一时刻的一帧（封面）。
pub fn grab_frame(tools: &Tools, input: &Path, info: &VideoInfo, at: f64, crop: Option<Crop>, hdr_to_sdr: bool) -> Result<Picture> {
    let at = at.clamp(0.0, (info.duration - 0.05).max(0.0));
    let options = FrameOptions { select: FrameSelect::All, width: None, trim: Some((at, at + 0.2)), crop, hdr_to_sdr };
    let mut first: Option<Picture> = None;
    let stop = AtomicBool::new(false);
    let _ = extract_frames(tools, input, info, &options, &mut |_, _, p| {
        if first.is_none() {
            first = Some(p);
        }
        Err(VideoError::Cancelled)
    }, &stop);
    first.ok_or_else(|| VideoError::Encode("取不到这一帧".into()))
}

// ---------- 静态帧 ----------

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum FrameSelect {
    PerSecond(f64),
    EverySeconds(f64),
    Total(u32),
    All,
}

impl FrameSelect {
    fn fps(self, duration: f64) -> Option<f64> {
        match self {
            FrameSelect::PerSecond(n) => Some(n),
            FrameSelect::EverySeconds(s) => Some(1.0 / s.max(0.01)),
            FrameSelect::Total(n) => Some(n as f64 / duration.max(0.01)),
            FrameSelect::All => None,
        }
    }
}

pub struct FrameOptions {
    pub select: FrameSelect,
    /// 输出宽度，高度按比例；None 为原尺寸。
    pub width: Option<u32>,
    pub trim: Option<(f64, f64)>,
    pub crop: Option<Crop>,
    pub hdr_to_sdr: bool,
}

/// 逐帧解出 RGBA，交给 `on_frame(序号, 时间戳, 图)`。返回帧数。
pub fn extract_frames(
    tools: &Tools,
    input: &Path,
    info: &VideoInfo,
    options: &FrameOptions,
    on_frame: &mut dyn FnMut(usize, f64, Picture) -> Result<()>,
    cancel: &AtomicBool,
) -> Result<usize> {
    let duration = options.trim.map(|(s, e)| e - s).unwrap_or(info.duration);
    let (cw, ch) = options.crop.map(|c| (c.width, c.height)).unwrap_or((info.width, info.height));
    let (w, h) = match options.width {
        Some(w) if w < cw => (w, ((w as f64) * ch as f64 / cw as f64).round().max(1.0) as u32),
        _ => (cw, ch),
    };
    let fps = options.select.fps(duration);
    let vf = filter_chain(info, options.crop, options.hdr_to_sdr, (w, h), fps, "rgba");
    let mut args = input_args(input, options.trim);
    args.extend(["-map".into(), "0:v:0".into(), "-vf".into(), vf, "-f".into(), "rawvideo".into(), "-pix_fmt".into(), "rgba".into()]);
    args.extend(["-loglevel".into(), "error".into(), "pipe:1".into()]);
    let mut child = Command::new(&tools.ffmpeg).args(&args).stdout(Stdio::piped()).stderr(Stdio::piped()).spawn()?;
    let mut stdout = child.stdout.take().unwrap();
    let frame_len = w as usize * h as usize * 4;
    let mut index = 0usize;
    let step = fps.map(|f| 1.0 / f).unwrap_or(1.0 / info.fps.max(1.0));
    loop {
        if cancel.load(Ordering::Relaxed) {
            let _ = child.kill();
            let _ = child.wait();
            return Err(VideoError::Cancelled);
        }
        let mut buf = vec![0u8; frame_len];
        match stdout.read_exact(&mut buf) {
            Ok(()) => {}
            Err(e) if e.kind() == std::io::ErrorKind::UnexpectedEof => break,
            Err(e) => return Err(e.into()),
        }
        let picture = img::from_rgba(w, h, buf, false, SourceFormat::VideoFrame);
        if let Err(e) = on_frame(index, index as f64 * step, picture) {
            let _ = child.kill();
            let _ = child.wait();
            return Err(e);
        }
        index += 1;
    }
    let status = child.wait()?;
    if !status.success() && index == 0 {
        let mut s = String::new();
        if let Some(mut err) = child.stderr.take() {
            let _ = err.read_to_string(&mut s);
        }
        return Err(VideoError::Encode(s.trim().to_string()));
    }
    Ok(index)
}

/// 视频转 GIF：抽帧后交给 gifski。
pub fn make_gif(
    tools: &Tools,
    input: &Path,
    output: &Path,
    info: &VideoInfo,
    options: &FrameOptions,
    quality: u8,
    cancel: &AtomicBool,
) -> Result<()> {
    let settings = gifski::Settings { width: None, height: None, quality: quality.clamp(1, 100), fast: false, repeat: gifski::Repeat::Infinite };
    let (collector, writer) = gifski::new(settings).map_err(|e| VideoError::Encode(e.to_string()))?;
    let tmp = temp_path(output);
    let tmp_for_writer = tmp.clone();
    let write_thread = std::thread::spawn(move || -> std::result::Result<(), String> {
        let file = std::fs::File::create(&tmp_for_writer).map_err(|e| e.to_string())?;
        writer.write(file, &mut gifski::progress::NoProgress {}).map_err(|e| e.to_string())
    });
    let collect = extract_frames(tools, input, info, options, &mut |i, ts, picture| {
        let pixels: Vec<rgb::RGBA8> = picture.rgba.chunks_exact(4).map(|p| rgb::RGBA8::new(p[0], p[1], p[2], p[3])).collect();
        let frame = imgref::ImgVec::new(pixels, picture.width as usize, picture.height as usize);
        collector.add_frame_rgba(i, frame, ts).map_err(|e| VideoError::Encode(e.to_string()))
    }, cancel);
    drop(collector);
    let written = write_thread.join().unwrap_or_else(|_| Err("GIF 写入线程异常".into()));
    match (collect, written) {
        (Ok(_), Ok(())) => {
            std::fs::rename(&tmp, output)?;
            Ok(())
        }
        (Err(e), _) => {
            let _ = std::fs::remove_file(&tmp);
            Err(e)
        }
        (_, Err(e)) => {
            let _ = std::fs::remove_file(&tmp);
            Err(VideoError::Encode(e))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 用 ffmpeg 自带的测试信号生成一段带声音的视频。
    fn sample(tools: &Tools, dir: &Path, seconds: u32) -> PathBuf {
        let path = dir.join("sample.mp4");
        let status = Command::new(&tools.ffmpeg)
            .args(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i"])
            .arg(format!("testsrc2=size=1280x720:rate=30:duration={seconds}"))
            .args(["-f", "lavfi", "-i"])
            .arg(format!("sine=frequency=440:duration={seconds}"))
            .args(["-c:v", "libx264", "-preset", "ultrafast", "-crf", "12", "-c:a", "aac", "-shortest"])
            .arg(&path)
            .status()
            .unwrap();
        assert!(status.success());
        path
    }

    fn tools_or_skip() -> Option<Tools> {
        Tools::locate().ok()
    }

    fn scratch(name: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("smaller-video-test-{name}-{}", std::process::id()));
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    #[test]
    fn 探测尺寸时长与音轨() {
        let Some(tools) = tools_or_skip() else { return };
        let dir = scratch("probe");
        let info = probe(&tools, &sample(&tools, &dir, 3)).unwrap();
        assert_eq!((info.width, info.height), (1280, 720));
        assert!((info.duration - 3.0).abs() < 0.2);
        assert!(info.has_audio && !info.hdr);
    }

    #[test]
    fn 两遍编码限体积不超出() {
        let Some(tools) = tools_or_skip() else { return };
        let dir = scratch("target");
        let input = sample(&tools, &dir, 6);
        let info = probe(&tools, &input).unwrap();
        let output = dir.join("out.mp4");
        let recipe = VideoRecipe {
            codec: VideoCodec::H264,
            encoder: EncoderChoice::Quality,
            rate: Rate::TargetBytes(400_000),
            max_short_edge: Some(540),
            keep_audio: true,
            trim: None,
            crop: None,
            hdr_to_sdr: true,
            max_fps: None,
        };
        let mut last = 0.0;
        transcode(&tools, &input, &output, &info, &recipe, &mut |p| last = p, &AtomicBool::new(false)).unwrap();
        let size = std::fs::metadata(&output).unwrap().len();
        assert!(size <= 400_000, "输出 {size} 字节");
        assert!(size > 200_000, "体积利用太低：{size} 字节");
        assert_eq!(last, 1.0);
        let out = probe(&tools, &output).unwrap();
        assert_eq!((out.width, out.height), (960, 540));
        assert!(!temp_path(&output).exists());
    }

    #[test]
    fn 硬件编码_剪辑裁切静音() {
        let Some(tools) = tools_or_skip() else { return };
        let dir = scratch("hw");
        let input = sample(&tools, &dir, 4);
        let info = probe(&tools, &input).unwrap();
        let output = dir.join("vertical.mp4");
        let recipe = VideoRecipe {
            codec: VideoCodec::H264,
            encoder: EncoderChoice::Fast,
            rate: Rate::Medium,
            max_short_edge: None,
            keep_audio: false,
            trim: Some((1.0, 3.0)),
            crop: Some(Crop { x: 438, y: 0, width: 405, height: 720 }),
            hdr_to_sdr: false,
            max_fps: None,
        };
        transcode(&tools, &input, &output, &info, &recipe, &mut |_| {}, &AtomicBool::new(false)).unwrap();
        let out = probe(&tools, &output).unwrap();
        assert_eq!((out.width, out.height), (406, 720));
        assert!(!out.has_audio);
        assert!((out.duration - 2.0).abs() < 0.2, "时长 {}", out.duration);
    }

    #[test]
    fn 序列帧与_gif() {
        let Some(tools) = tools_or_skip() else { return };
        let dir = scratch("frames");
        let input = sample(&tools, &dir, 2);
        let info = probe(&tools, &input).unwrap();
        let options = FrameOptions { select: FrameSelect::PerSecond(5.0), width: Some(320), trim: None, crop: None, hdr_to_sdr: false };
        let mut sizes = Vec::new();
        let n = extract_frames(&tools, &input, &info, &options, &mut |_, _, p| {
            sizes.push((p.width, p.height));
            Ok(())
        }, &AtomicBool::new(false))
        .unwrap();
        assert_eq!(n, 10);
        assert!(sizes.iter().all(|s| *s == (320, 180)));

        let gif = dir.join("out.gif");
        make_gif(&tools, &input, &gif, &info, &options, 90, &AtomicBool::new(false)).unwrap();
        let bytes = std::fs::read(&gif).unwrap();
        assert_eq!(&bytes[..3], b"GIF");
    }

    #[test]
    fn 只去声音与取封面() {
        let Some(tools) = tools_or_skip() else { return };
        let dir = scratch("mute");
        let input = sample(&tools, &dir, 3);
        let info = probe(&tools, &input).unwrap();
        let out = dir.join("mute.mp4");
        strip_audio(&tools, &input, &out, &info, &mut |_| {}, &AtomicBool::new(false)).unwrap();
        let o = probe(&tools, &out).unwrap();
        assert!(!o.has_audio && o.codec == "h264" && (o.width, o.height) == (1280, 720));
        let cover = grab_frame(&tools, &input, &info, 1.5, None, false).unwrap();
        assert_eq!((cover.width, cover.height), (1280, 720));
    }

    #[test]
    fn 取消时不留临时文件() {
        let Some(tools) = tools_or_skip() else { return };
        let dir = scratch("cancel");
        let input = sample(&tools, &dir, 6);
        let info = probe(&tools, &input).unwrap();
        let output = dir.join("cancel.mp4");
        let cancel = AtomicBool::new(false);
        let recipe = VideoRecipe {
            codec: VideoCodec::H264,
            encoder: EncoderChoice::Quality,
            rate: Rate::High,
            max_short_edge: None,
            keep_audio: true,
            trim: None,
            crop: None,
            hdr_to_sdr: false,
            max_fps: None,
        };
        let err = transcode(&tools, &input, &output, &info, &recipe, &mut |p| {
            if p > 0.1 {
                cancel.store(true, Ordering::Relaxed);
            }
        }, &cancel)
        .unwrap_err();
        assert!(matches!(err, VideoError::Cancelled));
        assert!(!output.exists() && !temp_path(&output).exists());
    }
}
