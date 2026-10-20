//! 实况：从视频里截一段（不超过 3 秒）、选一帧当封面，做成手机相册认的「会动的照片」。
//!
//! - 苹果实况：同名的一对文件，JPEG 静态图 + MOV。两边写同一个标识：
//!   JPEG 的 EXIF 里放 Apple MakerNote（第 17 项），MOV 的 moov 级元数据写
//!   `com.apple.quicktime.content.identifier`；MOV 另加一条 `mebx` 元数据轨，
//!   在封面那一刻写 `com.apple.quicktime.still-image-time`。ffmpeg 写不了这条轨，
//!   由这里直接改 MOV 的 box：原数据一个字节不动，末尾追加一小段 mdat 和新的 moov
//!   （旧 moov 在末尾就截掉，不在末尾就改成 free），原有轨道的 chunk 偏移因此不变。
//! - 安卓动态照片（Google Motion Photo，小米、三星等相册也认）：一张 JPEG，XMP 里写
//!   `GCamera:MotionPhoto` 与 `Container:Directory`，文件末尾直接接上 MP4；同时写旧的
//!   `GCamera:MicroVideo*`，老设备也认。
//! - 视频一律 H.264，最长边默认不超过 1920，帧率超过 30 的降到 30。

use std::path::{Path, PathBuf};
use std::sync::atomic::AtomicBool;

use crate::image::{self as img, OutputFormat};
use crate::jobs::{self, OutputFile};
use crate::video::{self, Crop, EncoderChoice, Rate, Tools, VideoCodec, VideoError, VideoInfo};

type Result<T> = std::result::Result<T, VideoError>;

/// 片段最长几秒（手机相册里的实况也是 3 秒左右）。
pub const MAX_SECONDS: f64 = 3.0;
/// 视频最长边默认上限。
pub const DEFAULT_LONG_EDGE: u32 = 1920;
const MAX_FPS: f64 = 30.0;

#[derive(Debug, Clone)]
pub struct LiveRecipe {
    pub apple: bool,
    pub android: bool,
    /// 封面取原视频的第几秒。
    pub cover_at: f64,
    /// 截取（秒）；超过 3 秒时以封面为中心取 3 秒。
    pub trim: Option<(f64, f64)>,
    pub crop: Option<Crop>,
    pub hdr_to_sdr: bool,
    pub keep_audio: bool,
    /// 视频最长边上限；0 表示不缩小。
    pub max_long_edge: u32,
    pub rate: Rate,
    pub encoder: EncoderChoice,
    /// 静态图的 JPEG 质量。
    pub still_quality: u8,
}

/// 输出位置：苹果实况是一对（JPEG, MOV），安卓是一张 JPEG。
#[derive(Debug, Clone, Default)]
pub struct LiveTargets {
    pub apple: Option<(PathBuf, PathBuf)>,
    pub android: Option<PathBuf>,
}

/// 实际用的片段与封面：(开始, 结束, 封面)，都是原视频里的秒数。
/// 截取范围（没截取就是全片）不超过 3 秒时照用；更长时以封面为中心取 3 秒，靠边时贴边。
pub fn clip_window(duration: f64, trim: Option<(f64, f64)>, cover: f64) -> (f64, f64, f64) {
    let duration = duration.max(0.0);
    let (lo, hi) = match trim {
        Some((s, e)) if e > s => (s.clamp(0.0, duration), e.clamp(0.0, duration)),
        _ => (0.0, duration),
    };
    let cover = cover.clamp(lo, hi);
    let (start, end) = if hi - lo <= MAX_SECONDS { (lo, hi) } else {
        let start = (cover - MAX_SECONDS / 2.0).clamp(lo, hi - MAX_SECONDS);
        (start, start + MAX_SECONDS)
    };
    // 封面不能落在最后一刻之后（那里已经没有帧）。
    let cover = cover.clamp(start, (end - 0.04).max(start));
    (start, end, cover)
}

/// 裁切后按最长边缩小（不放大），宽高取偶数。
pub fn clip_size(info: &VideoInfo, crop: Option<Crop>, max_long_edge: u32) -> (u32, u32) {
    let (w, h) = crop.map(|c| (c.width, c.height)).unwrap_or((info.width, info.height));
    let long = w.max(h) as f64;
    let scale = if max_long_edge > 0 && (max_long_edge as f64) < long { max_long_edge as f64 / long } else { 1.0 };
    (video::even(w as f64 * scale), video::even(h as f64 * scale))
}

fn output_fps(info: &VideoInfo) -> (Option<f64>, f64) {
    if info.fps > MAX_FPS + 0.5 { (Some(MAX_FPS), MAX_FPS) } else { (None, info.fps.max(1.0)) }
}

/// 做一个实况：按 `targets` 出苹果实况、安卓动态照片或两种都出。返回写出的文件。
pub fn make(
    tools: &Tools,
    input: &Path,
    info: &VideoInfo,
    recipe: &LiveRecipe,
    targets: &LiveTargets,
    progress: &mut dyn FnMut(f64),
    cancel: &AtomicBool,
) -> Result<Vec<OutputFile>> {
    let (start, end, cover) = clip_window(info.duration, recipe.trim, recipe.cover_at);
    if end - start < 0.1 {
        return Err(VideoError::Encode("视频太短，做不成实况".into()));
    }
    let (_, fps) = output_fps(info);
    // 封面在片段里的时刻，对齐到帧。
    let still_offset = (((cover - start) * fps).round() / fps).clamp(0.0, (end - start - 1.0 / fps).max(0.0));

    let first_dir = targets.apple.as_ref().map(|t| t.0.clone()).or_else(|| targets.android.clone());
    let Some(first) = first_dir else { return Ok(vec![]) };
    let clip = video::temp_path(&first.with_extension("clip.mov"));
    let mut created: Vec<PathBuf> = Vec::new();
    let result = (|| -> Result<Vec<OutputFile>> {
        if let Some(dir) = first.parent() {
            std::fs::create_dir_all(dir)?;
        }
        encode_clip(tools, input, info, recipe, (start, end), &clip, progress, cancel)?;
        let (vw, vh) = clip_size(info, recipe.crop, recipe.max_long_edge);

        let still = video::grab_frame(tools, input, info, start + still_offset, recipe.crop, recipe.hdr_to_sdr)?;
        let still_jpeg = img::encode(&still, OutputFormat::Jpeg, recipe.still_quality, false).map_err(|e| VideoError::Encode(e.to_string()))?;
        let mut outputs = Vec::new();

        if let Some(path) = &targets.android {
            let mp4 = video::temp_path(&path.with_extension("mp4"));
            created.push(mp4.clone());
            let mut args = video::input_args(&clip, None);
            args.extend(["-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-map_metadata", "-1", "-movflags", "+faststart", "-f", "mp4"].map(String::from));
            args.push(mp4.to_string_lossy().into_owned());
            video::run_ffmpeg(tools, &args, end - start, (0.8, 0.9), progress, cancel)?;
            let video_bytes = std::fs::read(&mp4)?;
            let _ = std::fs::remove_file(&mp4);
            let ts_us = (still_offset * 1_000_000.0).round() as i64;
            let mut file = insert_app1(&still_jpeg, &[motion_photo_xmp(video_bytes.len() as u64, ts_us)])
                .ok_or_else(|| VideoError::Encode("封面不是 JPEG".into()))?;
            file.extend_from_slice(&video_bytes);
            jobs::write_atomic(path, &file)?;
            created.push(path.clone());
            outputs.push(OutputFile { path: path.clone(), bytes: file.len() as u64, width: still.width, height: still.height });
        }

        if let Some((jpg, mov)) = &targets.apple {
            let id = new_content_id();
            let data = std::fs::read(&clip)?;
            let movie = add_live_metadata(&data, &id, still_offset).map_err(VideoError::Encode)?;
            jobs::write_atomic(mov, &movie)?;
            created.push(mov.clone());
            let photo = insert_app1(&still_jpeg, &[apple_exif(&id)]).ok_or_else(|| VideoError::Encode("封面不是 JPEG".into()))?;
            jobs::write_atomic(jpg, &photo)?;
            created.push(jpg.clone());
            // 静态图在前：完成页、访达里先看到照片。
            outputs.insert(0, OutputFile { path: mov.clone(), bytes: movie.len() as u64, width: vw, height: vh });
            outputs.insert(0, OutputFile { path: jpg.clone(), bytes: photo.len() as u64, width: still.width, height: still.height });
        }
        progress(1.0);
        Ok(outputs)
    })();
    let _ = std::fs::remove_file(&clip);
    if result.is_err() {
        for p in created {
            let _ = std::fs::remove_file(p);
        }
    }
    result
}

/// 截取 → 裁切 → HDR 转 SDR → 缩放 → H.264，写成 MOV（不前置文件头：moov 在末尾，之后好追加）。
#[allow(clippy::too_many_arguments)]
fn encode_clip(
    tools: &Tools,
    input: &Path,
    info: &VideoInfo,
    recipe: &LiveRecipe,
    window: (f64, f64),
    output: &Path,
    progress: &mut dyn FnMut(f64),
    cancel: &AtomicBool,
) -> Result<()> {
    let size = clip_size(info, recipe.crop, recipe.max_long_edge);
    let (fps_filter, _) = output_fps(info);
    let vf = video::filter_chain(info, recipe.crop, recipe.hdr_to_sdr, size, fps_filter, "yuv420p");
    let audio = recipe.keep_audio && info.has_audio;
    let rate = match recipe.rate {
        Rate::TargetBytes(_) => Rate::High,
        r => r,
    };
    let mut args = video::input_args(input, Some(window));
    args.extend(["-map".into(), "0:v:0".into()]);
    if audio {
        args.extend(["-map".into(), "0:a:0".into()]);
    }
    args.extend(["-vf".into(), vf]);
    match recipe.encoder {
        EncoderChoice::Quality => {
            let crf = match rate {
                Rate::High => "20",
                Rate::Medium => "23",
                _ => "27",
            };
            args.extend(["-c:v", "libx264", "-preset", "slow", "-crf", crf, "-profile:v", "high", "-pix_fmt", "yuv420p", "-tag:v", "avc1"].map(String::from));
        }
        EncoderChoice::Fast => {
            args.extend(video::hardware_encoder(VideoCodec::H264));
            args.extend(video::hardware_quality(rate));
        }
    }
    if audio {
        let aac = if cfg!(target_os = "macos") { "aac_at" } else { "aac" };
        args.extend(["-c:a".into(), aac.into(), "-b:a".into(), format!("{}k", video::AUDIO_KBPS)]);
    } else {
        args.push("-an".into());
    }
    args.extend(["-map_metadata", "-1", "-map_chapters", "-1"].map(String::from));
    args.extend(["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-f", "mov"].map(String::from));
    args.push(output.to_string_lossy().into_owned());
    video::run_ffmpeg(tools, &args, window.1 - window.0, (0.0, 0.75), progress, cancel)
}

/// 苹果实况两边共用的标识：一个大写 UUID（v4）。
pub fn new_content_id() -> String {
    use std::hash::{BuildHasher, Hasher};
    let now = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(0);
    let mut b = [0u8; 16];
    for (i, chunk) in b.chunks_mut(8).enumerate() {
        let mut h = std::collections::hash_map::RandomState::new().build_hasher();
        h.write_u128(now);
        h.write_usize(i);
        h.write_u32(std::process::id());
        chunk.copy_from_slice(&h.finish().to_le_bytes());
    }
    b[6] = (b[6] & 0x0f) | 0x40;
    b[8] = (b[8] & 0x3f) | 0x80;
    let hex: String = b.iter().map(|x| format!("{x:02X}")).collect();
    format!("{}-{}-{}-{}-{}", &hex[0..8], &hex[8..12], &hex[12..16], &hex[16..20], &hex[20..32])
}

// ---------- JPEG：EXIF（Apple MakerNote）与 XMP（Motion Photo） ----------

fn be16(v: u16) -> [u8; 2] {
    v.to_be_bytes()
}

fn be32(v: u32) -> [u8; 4] {
    v.to_be_bytes()
}

/// TIFF 目录项（大端）：标签、类型、个数、值或偏移。
fn ifd_entry(tag: u16, kind: u16, count: u32, value: u32) -> Vec<u8> {
    [&be16(tag)[..], &be16(kind), &be32(count), &be32(value)].concat()
}

/// Apple MakerNote：`Apple iOS\0` + 版本 1 + `MM`，第 14 字节起是一个大端 IFD，
/// 只有第 17 项（内容标识，ASCII）。偏移从 MakerNote 开头算（与系统 ImageIO 写出的一致）。
pub fn apple_maker_note(id: &str) -> Vec<u8> {
    let mut m = b"Apple iOS\0".to_vec();
    m.extend_from_slice(&[0, 1]);
    m.extend_from_slice(b"MM");
    m.extend_from_slice(&be16(1));
    let value_at = 14 + 2 + 12 + 4;
    m.extend(ifd_entry(0x0011, 2, id.len() as u32 + 1, value_at));
    m.extend_from_slice(&be32(0));
    m.extend_from_slice(id.as_bytes());
    m.push(0);
    if m.len() % 2 == 1 {
        m.push(0);
    }
    m
}

/// APP1 的 EXIF 段（含 `Exif\0\0` 头）：IFD0 只指向 Exif IFD，Exif IFD 里只有 MakerNote。
pub fn apple_exif(id: &str) -> Vec<u8> {
    let note = apple_maker_note(id);
    let mut t = b"MM\0\x2a".to_vec();
    t.extend_from_slice(&be32(8));
    // IFD0 在 8：1 项 + 下一个 IFD（0），共 18 字节；Exif IFD 在 26，同样 18 字节；MakerNote 数据在 44。
    t.extend_from_slice(&be16(1));
    t.extend(ifd_entry(0x8769, 4, 1, 26));
    t.extend_from_slice(&be32(0));
    t.extend_from_slice(&be16(1));
    t.extend(ifd_entry(0x927C, 7, note.len() as u32, 44));
    t.extend_from_slice(&be32(0));
    t.extend(note);
    [&b"Exif\0\0"[..], &t].concat()
}

/// APP1 的 XMP 段（含命名空间头）：Motion Photo 第 1 版 + 旧的 MicroVideo 字段。
/// `video_len` 是接在文件末尾的 MP4 字节数；`ts_us` 是封面在视频里的时刻（微秒）。
pub fn motion_photo_xmp(video_len: u64, ts_us: i64) -> Vec<u8> {
    let xmp = format!(
        r#"<?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="SIMPLE SMALLER">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:GCamera="http://ns.google.com/photos/1.0/camera/"
    xmlns:Container="http://ns.google.com/photos/1.0/container/"
    xmlns:Item="http://ns.google.com/photos/1.0/container/item/"
   GCamera:MotionPhoto="1"
   GCamera:MotionPhotoVersion="1"
   GCamera:MotionPhotoPresentationTimestampUs="{ts_us}"
   GCamera:MicroVideo="1"
   GCamera:MicroVideoVersion="1"
   GCamera:MicroVideoOffset="{video_len}"
   GCamera:MicroVideoPresentationTimestampUs="{ts_us}">
   <Container:Directory>
    <rdf:Seq>
     <rdf:li rdf:parseType="Resource">
      <Container:Item
       Item:Mime="image/jpeg"
       Item:Semantic="Primary"
       Item:Length="0"
       Item:Padding="0"/>
     </rdf:li>
     <rdf:li rdf:parseType="Resource">
      <Container:Item
       Item:Mime="video/mp4"
       Item:Semantic="MotionPhoto"
       Item:Length="{video_len}"
       Item:Padding="0"/>
     </rdf:li>
    </rdf:Seq>
   </Container:Directory>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>"#
    );
    [&b"http://ns.adobe.com/xap/1.0/\0"[..], xmp.as_bytes()].concat()
}

/// 把若干 APP1 段插到 JPEG 的 SOI（和 JFIF APP0）之后。不是 JPEG 或段太长时返回 None。
pub fn insert_app1(jpeg: &[u8], payloads: &[Vec<u8>]) -> Option<Vec<u8>> {
    if jpeg.len() < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 {
        return None;
    }
    let mut at = 2;
    while at + 4 <= jpeg.len() && jpeg[at] == 0xFF && jpeg[at + 1] == 0xE0 {
        at += 2 + u16::from_be_bytes([jpeg[at + 2], jpeg[at + 3]]) as usize;
    }
    let at = at.min(jpeg.len());
    let mut out = Vec::with_capacity(jpeg.len() + payloads.iter().map(|p| p.len() + 4).sum::<usize>());
    out.extend_from_slice(&jpeg[..at]);
    for p in payloads {
        let len = u16::try_from(p.len() + 2).ok()?;
        out.extend_from_slice(&[0xFF, 0xE1]);
        out.extend_from_slice(&len.to_be_bytes());
        out.extend_from_slice(p);
    }
    out.extend_from_slice(&jpeg[at..]);
    Some(out)
}

// ---------- MOV：content.identifier 与 still-image-time 轨 ----------

/// 一个 box 在数据里的位置：类型、开头、头长、总长。
#[derive(Debug, Clone, Copy)]
pub struct BoxRef {
    pub kind: [u8; 4],
    pub start: usize,
    pub header: usize,
    pub size: usize,
}

impl BoxRef {
    pub fn body<'a>(&self, data: &'a [u8]) -> &'a [u8] {
        &data[self.start + self.header..self.start + self.size]
    }
}

/// 列出 `data[from..to]` 里的一层 box。长度字段来自文件本身，不可信：所有加法都查溢出，取下标前先查长度。
pub fn boxes(data: &[u8], from: usize, to: usize) -> std::result::Result<Vec<BoxRef>, String> {
    let to = to.min(data.len());
    let mut out = Vec::new();
    let mut p = from;
    while p.checked_add(8).is_some_and(|end| end <= to) {
        let size32 = u32::from_be_bytes(data[p..p + 4].try_into().unwrap()) as usize;
        let kind: [u8; 4] = data[p + 4..p + 8].try_into().unwrap();
        let (header, size) = match size32 {
            0 => (8, to - p),
            1 => {
                if p + 16 > to {
                    return Err("box 头不完整".into());
                }
                let large = u64::from_be_bytes(data[p + 8..p + 16].try_into().unwrap());
                (16, usize::try_from(large).map_err(|_| "box 长度不对".to_string())?)
            }
            n => (8, n),
        };
        let end = p.checked_add(size).filter(|&end| size >= header && end <= to);
        let Some(end) = end else {
            return Err(format!("box {} 长度不对", String::from_utf8_lossy(&kind)));
        };
        out.push(BoxRef { kind, start: p, header, size });
        p = end;
    }
    Ok(out)
}

fn bx(kind: &[u8; 4], body: &[u8]) -> Vec<u8> {
    [&be32(body.len() as u32 + 8)[..], kind, body].concat()
}

/// 带版本与标志的 box。
fn full(kind: &[u8; 4], version: u8, flags: u32, body: &[u8]) -> Vec<u8> {
    let vf = ((version as u32) << 24) | (flags & 0x00FF_FFFF);
    bx(kind, &[&be32(vf)[..], body].concat())
}

const KEY_CONTENT_ID: &str = "com.apple.quicktime.content.identifier";
const KEY_STILL_TIME: &str = "com.apple.quicktime.still-image-time";
/// QuickTime 的「常见类型」：UTF-8 文本 1，有符号 8 位整数 65。
const TYPE_UTF8: u32 = 1;
const TYPE_INT8: u32 = 65;
/// 元数据轨每个样本：长度 9 = 4 长度 + 4 本地键 1 + 1 字节值 −1（与 iPhone 拍的一样）。
const STILL_SAMPLE: [u8; 9] = [0, 0, 0, 9, 0, 0, 0, 1, 0xFF];
const META_TIMESCALE: u32 = 1000;

fn matrix() -> Vec<u8> {
    [0x0001_0000u32, 0, 0, 0, 0x0001_0000, 0, 0, 0, 0x4000_0000].iter().flat_map(|v| be32(*v)).collect()
}

/// QuickTime 的老式 hdlr：组件类型、子类型、厂商、标志、Pascal 字符串名字。
fn qt_hdlr(component: &[u8; 4], subtype: &[u8; 4], flags: u32, name: &str) -> Vec<u8> {
    let mut b = Vec::new();
    b.extend_from_slice(component);
    b.extend_from_slice(subtype);
    b.extend_from_slice(b"appl");
    b.extend_from_slice(&be32(flags));
    b.extend_from_slice(&be32(0));
    b.push(name.len() as u8);
    b.extend_from_slice(name.as_bytes());
    full(b"hdlr", 0, 0, &b)
}

/// moov 级元数据（QuickTime 式 meta，没有版本字段）：mdta 的 keys + ilst，只写内容标识。
fn content_id_meta(id: &str) -> Vec<u8> {
    let hdlr = full(b"hdlr", 0, 0, &[&be32(0)[..], b"mdta", &[0u8; 12], &[0u8; 2]].concat());
    let key = bx(b"mdta", KEY_CONTENT_ID.as_bytes());
    let keys = full(b"keys", 0, 0, &[&be32(1)[..], &key].concat());
    let data = bx(b"data", &[&be32(TYPE_UTF8)[..], &be32(0), id.as_bytes()].concat());
    let item = bx(&1u32.to_be_bytes(), &data);
    let ilst = bx(b"ilst", &item);
    bx(b"meta", &[hdlr, keys, ilst].concat())
}

/// still-image-time 的元数据轨：一个样本，放在封面那一刻（前面用空编辑占位）。
fn still_track(track_id: u32, movie_timescale: u32, still: f64, sample_ms: u32, chunk_offset: u64) -> Vec<u8> {
    let to_movie = |seconds: f64| (seconds * movie_timescale as f64).round() as u32;
    let empty = to_movie(still);
    let span = to_movie(sample_ms as f64 / 1000.0).max(1);
    let duration = empty + span;

    let mut tkhd = Vec::new();
    tkhd.extend_from_slice(&be32(0)); // 创建时间
    tkhd.extend_from_slice(&be32(0)); // 修改时间
    tkhd.extend_from_slice(&be32(track_id));
    tkhd.extend_from_slice(&be32(0));
    tkhd.extend_from_slice(&be32(duration));
    tkhd.extend_from_slice(&[0u8; 8]);
    tkhd.extend_from_slice(&[0u8; 8]); // 层、备用组、音量、保留
    tkhd.extend(matrix());
    tkhd.extend_from_slice(&[0u8; 8]); // 宽、高
    let tkhd = full(b"tkhd", 0, 0x0F, &tkhd);

    let mut entries: Vec<(u32, i32)> = Vec::new();
    if empty > 0 {
        entries.push((empty, -1));
    }
    entries.push((span, 0));
    let mut elst = be32(entries.len() as u32).to_vec();
    for (d, t) in entries {
        elst.extend_from_slice(&be32(d));
        elst.extend_from_slice(&t.to_be_bytes());
        elst.extend_from_slice(&be32(0x0001_0000));
    }
    let edts = bx(b"edts", &full(b"elst", 0, 0, &elst));

    let mdhd = full(b"mdhd", 0, 0, &[&be32(0)[..], &be32(0), &be32(META_TIMESCALE), &be32(sample_ms), &be16(0x55C4), &be16(0)].concat());
    let mdia_hdlr = qt_hdlr(b"mhlr", b"meta", 1, "Core Media Metadata");
    let gmin = full(b"gmin", 0, 0, &[&be16(0x0040)[..], &be16(0x8000), &be16(0x8000), &be16(0x8000), &be16(0), &be16(0)].concat());
    let gmhd = bx(b"gmhd", &gmin);
    let minf_hdlr = qt_hdlr(b"dhlr", b"alis", 0, "Core Media Data Handler");
    let dref = full(b"dref", 0, 0, &[&be32(1)[..], &full(b"alis", 0, 1, &[])].concat());
    let dinf = bx(b"dinf", &dref);

    let keyd = bx(b"keyd", &[&b"mdta"[..], KEY_STILL_TIME.as_bytes()].concat());
    let dtyp = bx(b"dtyp", &[&be32(0)[..], &be32(TYPE_INT8)].concat());
    let key = bx(&1u32.to_be_bytes(), &[keyd, dtyp].concat());
    let keys = bx(b"keys", &key);
    let mebx = bx(b"mebx", &[&[0u8; 6][..], &be16(1), &keys].concat());
    let stsd = full(b"stsd", 0, 0, &[&be32(1)[..], &mebx].concat());
    let stts = full(b"stts", 0, 0, &[&be32(1)[..], &be32(1), &be32(sample_ms)].concat());
    let stsc = full(b"stsc", 0, 0, &[&be32(1)[..], &be32(1), &be32(1), &be32(1)].concat());
    let stsz = full(b"stsz", 0, 0, &[&be32(STILL_SAMPLE.len() as u32)[..], &be32(1)].concat());
    let stco = match u32::try_from(chunk_offset) {
        Ok(o) => full(b"stco", 0, 0, &[&be32(1)[..], &be32(o)].concat()),
        Err(_) => full(b"co64", 0, 0, &[&be32(1)[..], &chunk_offset.to_be_bytes()].concat()),
    };
    let stbl = bx(b"stbl", &[stsd, stts, stsc, stsz, stco].concat());
    let minf = bx(b"minf", &[gmhd, minf_hdlr, dinf, stbl].concat());
    let mdia = bx(b"mdia", &[mdhd, mdia_hdlr, minf].concat());
    bx(b"trak", &[tkhd, edts, mdia].concat())
}

/// 给 MOV 加上实况所需的元数据，返回新文件内容：
/// 原数据原样保留，末尾追加 mdat（still-image-time 样本）与新 moov；旧 moov 在末尾就截掉，否则改成 free。
pub fn add_live_metadata(data: &[u8], id: &str, still_time: f64) -> std::result::Result<Vec<u8>, String> {
    let top = boxes(data, 0, data.len())?;
    let moov = *top.iter().find(|b| &b.kind == b"moov").ok_or("没有 moov")?;
    let children = boxes(data, moov.start + moov.header, moov.start + moov.size)?;
    let mvhd = *children.iter().find(|b| &b.kind == b"mvhd").ok_or("没有 mvhd")?;
    let mut mvhd_bytes = data[mvhd.start..mvhd.start + mvhd.size].to_vec();
    let body = mvhd.header;
    let Some(&version) = mvhd_bytes.get(body) else { return Err("mvhd 不完整".into()) };
    let (timescale_at, duration_at, duration_len) = if version == 1 { (body + 20, body + 24, 8) } else { (body + 12, body + 16, 4) };
    if mvhd_bytes.len() < duration_at + duration_len + 4 {
        return Err("mvhd 不完整".into());
    }
    let timescale = u32::from_be_bytes(mvhd_bytes[timescale_at..timescale_at + 4].try_into().unwrap()).max(1);
    let movie_duration = if version == 1 {
        u64::from_be_bytes(mvhd_bytes[duration_at..duration_at + 8].try_into().unwrap())
    } else {
        u32::from_be_bytes(mvhd_bytes[duration_at..duration_at + 4].try_into().unwrap()) as u64
    };
    let next_at = mvhd_bytes.len() - 4;
    let track_id = u32::from_be_bytes(mvhd_bytes[next_at..].try_into().unwrap()).max(1);
    let next_track = track_id.checked_add(1).ok_or("轨道编号用完了")?;
    mvhd_bytes[next_at..].copy_from_slice(&be32(next_track));

    // 样本长一帧左右（33 毫秒）；封面贴着片尾时往前挪，整条轨不超出片长。
    let sample_ms: u32 = 33;
    let movie_seconds = movie_duration as f64 / timescale as f64;
    let still = if movie_seconds > 0.0 { still_time.clamp(0.0, (movie_seconds - sample_ms as f64 / 1000.0).max(0.0)) } else { still_time.max(0.0) };

    let moov_last = moov.start + moov.size == data.len();
    let mut out = if moov_last { data[..moov.start].to_vec() } else {
        let mut d = data.to_vec();
        d[moov.start + 4..moov.start + 8].copy_from_slice(b"free");
        d
    };
    let sample_offset = out.len() as u64 + 8;
    out.extend(bx(b"mdat", &STILL_SAMPLE));

    let mut moov_body = mvhd_bytes;
    for c in &children {
        // 旧的 moov 级 meta（如果有）换成新的。
        if &c.kind == b"mvhd" || &c.kind == b"meta" {
            continue;
        }
        moov_body.extend_from_slice(&data[c.start..c.start + c.size]);
    }
    moov_body.extend(still_track(track_id, timescale, still, sample_ms, sample_offset));
    moov_body.extend(content_id_meta(id));
    out.extend(bx(b"moov", &moov_body));
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn find<'a>(data: &'a [u8], path: &[&[u8; 4]]) -> Option<BoxRef> {
        let mut range = (0, data.len());
        let mut found = None;
        for (i, kind) in path.iter().enumerate() {
            let list = boxes(data, range.0, range.1).ok()?;
            let b = *list.iter().find(|b| &b.kind == *kind)?;
            // QuickTime 的 meta 没有版本字段，子 box 紧跟在头后面。
            range = (b.start + b.header, b.start + b.size);
            if i + 1 == path.len() {
                found = Some(b);
            }
        }
        found
    }

    #[test]
    fn 片段不超过三秒且包住封面() {
        assert_eq!(clip_window(10.0, None, 5.0), (3.5, 6.5, 5.0));
        assert_eq!(clip_window(10.0, None, 0.2), (0.0, 3.0, 0.2));
        assert_eq!(clip_window(10.0, None, 10.0), (7.0, 10.0, 9.96));
        assert_eq!(clip_window(2.0, None, 1.0), (0.0, 2.0, 1.0));
        // 截取 2–4 秒，封面在截取范围外：拉回范围里。
        assert_eq!(clip_window(10.0, Some((2.0, 4.0)), 8.0), (2.0, 4.0, 3.96));
        let (s, e, c) = clip_window(10.0, Some((1.0, 9.0)), 8.5);
        assert!((e - s - MAX_SECONDS).abs() < 1e-9 && s >= 1.0 && e <= 9.0 && c >= s && c < e);
    }

    #[test]
    fn makernote_与系统写的一致() {
        let id = "B345E7ED-8F8A-4BAC-A258-30526777072B";
        let note = apple_maker_note(id);
        // 与 ImageIO 写出的字节逐一对照（见 .local 里的参考文件）。
        let mut expect = b"Apple iOS\0\0\x01MM\0\x01".to_vec();
        expect.extend_from_slice(&[0x00, 0x11, 0x00, 0x02, 0x00, 0x00, 0x00, 0x25, 0x00, 0x00, 0x00, 0x20, 0, 0, 0, 0]);
        expect.extend_from_slice(id.as_bytes());
        expect.extend_from_slice(&[0, 0]);
        assert_eq!(note, expect);

        let exif = apple_exif(id);
        assert!(exif.starts_with(b"Exif\0\0MM\0\x2a\0\0\0\x08"));
        let t = &exif[6..];
        // IFD0 → Exif IFD 指针（0x8769）指向 26；Exif IFD 里 MakerNote（0x927C）指向 44。
        assert_eq!(&t[10..12], &[0x87, 0x69]);
        assert_eq!(u32::from_be_bytes(t[18..22].try_into().unwrap()), 26);
        assert_eq!(&t[28..30], &[0x92, 0x7C]);
        assert_eq!(u32::from_be_bytes(t[32..36].try_into().unwrap()), note.len() as u32);
        assert_eq!(u32::from_be_bytes(t[36..40].try_into().unwrap()), 44);
        assert_eq!(&t[44..], &note[..]);
    }

    #[test]
    fn 动态照片的_xmp_与插入位置() {
        let xmp = motion_photo_xmp(123_456, 1_500_000);
        let text = String::from_utf8_lossy(&xmp);
        assert!(xmp.starts_with(b"http://ns.adobe.com/xap/1.0/\0"));
        for field in [
            r#"GCamera:MotionPhoto="1""#,
            r#"GCamera:MotionPhotoVersion="1""#,
            r#"GCamera:MotionPhotoPresentationTimestampUs="1500000""#,
            r#"GCamera:MicroVideo="1""#,
            r#"GCamera:MicroVideoOffset="123456""#,
            r#"Item:Semantic="Primary""#,
            r#"Item:Semantic="MotionPhoto""#,
            r#"Item:Length="123456""#,
        ] {
            assert!(text.contains(field), "缺 {field}");
        }
        // 插在 JFIF APP0 之后。
        let jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 1, 2, 0xFF, 0xDA, 9, 9, 0xFF, 0xD9];
        let out = insert_app1(&jpeg, &[b"abc".to_vec()]).unwrap();
        assert_eq!(out, [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 1, 2, 0xFF, 0xE1, 0, 5, b'a', b'b', b'c', 0xFF, 0xDA, 9, 9, 0xFF, 0xD9]);
        assert!(insert_app1(b"GIF89a", &[]).is_none());
    }

    /// 造一个最小的「moov 在末尾」的 MOV：ftyp + mdat + moov(mvhd + 一个假 trak)。
    fn fake_mov(timescale: u32, duration: u32, next_track: u32) -> Vec<u8> {
        let mut mvhd = vec![0u8; 100];
        mvhd[12..16].copy_from_slice(&timescale.to_be_bytes());
        mvhd[16..20].copy_from_slice(&duration.to_be_bytes());
        mvhd[96..100].copy_from_slice(&next_track.to_be_bytes());
        let trak = bx(b"trak", &full(b"tkhd", 0, 0xF, &[0u8; 80]));
        [bx(b"ftyp", b"qt  \0\0\0\0qt  "), bx(b"mdat", &[7u8; 1000]), bx(b"moov", &[bx(b"mvhd", &mvhd), trak].concat())].concat()
    }

    #[test]
    fn 坏的_box_报错不崩() {
        // 64 位长度大得离谱（加上起点会溢出）
        let mut huge = Vec::new();
        huge.extend_from_slice(&1u32.to_be_bytes());
        huge.extend_from_slice(b"mdat");
        huge.extend_from_slice(&u64::MAX.to_be_bytes());
        assert!(boxes(&huge, 0, huge.len()).is_err());
        // 范围超出数据
        assert!(boxes(&huge, 0, usize::MAX).is_err());
        // mvhd 只有头没有内容
        let empty_mvhd = [bx(b"ftyp", b"qt  "), bx(b"moov", &bx(b"mvhd", &[]))].concat();
        assert!(add_live_metadata(&empty_mvhd, "x", 0.0).is_err());
        // 下一个轨道编号已到上限
        let full_ids = fake_mov(600, 600, u32::MAX);
        assert!(add_live_metadata(&full_ids, "x", 0.0).is_err());
    }

    #[test]
    fn mov_追加元数据轨_偏移与结构() {
        let src = fake_mov(600, 1800, 2);
        let out = add_live_metadata(&src, "ID-1", 1.5).unwrap();
        // 原数据（到 moov 之前）一个字节不动。
        let moov_at = boxes(&src, 0, src.len()).unwrap().iter().find(|b| &b.kind == b"moov").unwrap().start;
        assert_eq!(&out[..moov_at], &src[..moov_at]);
        let top: Vec<[u8; 4]> = boxes(&out, 0, out.len()).unwrap().iter().map(|b| b.kind).collect();
        assert_eq!(top, [*b"ftyp", *b"mdat", *b"mdat", *b"moov"]);

        // mvhd 的下一个轨道号 +1。
        let mvhd = find(&out, &[b"moov", b"mvhd"]).unwrap();
        assert_eq!(u32::from_be_bytes(mvhd.body(&out)[96..100].try_into().unwrap()), 3);

        // 第二条 trak 是元数据轨：轨道号 2，空编辑 1.5 秒（900 @600），样本偏移指向 9 字节样本。
        let moov = find(&out, &[b"moov"]).unwrap();
        let traks: Vec<BoxRef> = boxes(&out, moov.start + 8, moov.start + moov.size).unwrap().into_iter().filter(|b| &b.kind == b"trak").collect();
        assert_eq!(traks.len(), 2);
        let t = traks[1];
        let inner = &out[t.start..t.start + t.size];
        let tkhd = find(inner, &[b"trak", b"tkhd"]).unwrap();
        assert_eq!(u32::from_be_bytes(tkhd.body(inner)[12..16].try_into().unwrap()), 2);
        let elst = find(inner, &[b"trak", b"edts", b"elst"]).unwrap().body(inner).to_vec();
        assert_eq!(&elst[4..8], &be32(2));
        assert_eq!(&elst[8..16], &[&be32(900)[..], &(-1i32).to_be_bytes()].concat()[..]);
        let stco = find(inner, &[b"trak", b"mdia", b"minf", b"stbl", b"stco"]).unwrap().body(inner).to_vec();
        let offset = u32::from_be_bytes(stco[8..12].try_into().unwrap()) as usize;
        assert_eq!(&out[offset..offset + 9], &STILL_SAMPLE);
        let stsd = find(inner, &[b"trak", b"mdia", b"minf", b"stbl", b"stsd"]).unwrap().body(inner).to_vec();
        assert_eq!(&stsd[12..16], b"mebx");
        let text = String::from_utf8_lossy(&stsd);
        assert!(text.contains("mdtacom.apple.quicktime.still-image-time"));

        // moov 级 meta：keys 里是 content.identifier，ilst 第 1 项是 UTF-8 的标识。
        let meta = find(&out, &[b"moov", b"meta"]).unwrap();
        let m = meta.body(&out);
        let parts: Vec<[u8; 4]> = boxes(m, 0, m.len()).unwrap().iter().map(|b| b.kind).collect();
        assert_eq!(parts, [*b"hdlr", *b"keys", *b"ilst"]);
        let s = String::from_utf8_lossy(m);
        assert!(s.contains("mdtacom.apple.quicktime.content.identifier") && s.ends_with("ID-1"));
    }

    #[test]
    fn moov_不在末尾时改成_free() {
        let mut src = fake_mov(1000, 3000, 5);
        // 把 moov 挪到 mdat 前面。
        let list = boxes(&src, 0, src.len()).unwrap();
        let (ftyp, mdat, moov) = (list[0], list[1], list[2]);
        src = [&src[..ftyp.size], &src[moov.start..moov.start + moov.size], &src[mdat.start..mdat.start + mdat.size]].concat();
        let out = add_live_metadata(&src, "X", 9.0).unwrap();
        assert_eq!(&out[..src.len()][ftyp.size + 4..ftyp.size + 8], b"free");
        let top: Vec<[u8; 4]> = boxes(&out, 0, out.len()).unwrap().iter().map(|b| b.kind).collect();
        assert_eq!(top, [*b"ftyp", *b"free", *b"mdat", *b"mdat", *b"moov"]);
        // 封面超过片长：贴着片尾（3 秒 − 33 毫秒）。
        let moov = find(&out, &[b"moov"]).unwrap();
        let t = *boxes(&out, moov.start + 8, moov.start + moov.size).unwrap().iter().rfind(|b| &b.kind == b"trak").unwrap();
        let inner = &out[t.start..t.start + t.size];
        let elst = find(inner, &[b"trak", b"edts", b"elst"]).unwrap().body(inner).to_vec();
        assert_eq!(u32::from_be_bytes(elst[8..12].try_into().unwrap()), 2967);
    }

    #[test]
    fn 标识是大写_uuid_且每次不同() {
        let a = new_content_id();
        let b = new_content_id();
        assert_eq!(a.len(), 36);
        assert_eq!(a.matches('-').count(), 4);
        assert_eq!(&a[14..15], "4");
        assert_eq!(a, a.to_uppercase());
        assert_ne!(a, b);
    }

    /// 用真实素材跑通：出苹果实况一对 + 安卓动态照片，检查结构。
    /// 素材在 .local/vtest/jellyfish.mp4（不进仓库）；没有素材或 ffmpeg 时跳过。
    /// 设了 SMALLER_LIVE_OUT 时把结果留在那个目录（给系统接口验证程序用）。
    #[test]
    fn 真实素材_两种都出() {
        let Ok(tools) = Tools::locate() else { return };
        let sample = Path::new(env!("CARGO_MANIFEST_DIR")).join("../.local/vtest/jellyfish.mp4");
        if !sample.exists() {
            return;
        }
        let dir = std::env::var("SMALLER_LIVE_OUT").map(PathBuf::from)
            .unwrap_or_else(|_| std::env::temp_dir().join(format!("smaller-live-test-{}", std::process::id())));
        std::fs::create_dir_all(&dir).unwrap();
        let input = dir.join("jellyfish.mp4");
        std::fs::copy(&sample, &input).unwrap();
        let info = video::probe(&tools, &input).unwrap();
        let recipe = LiveRecipe {
            apple: true,
            android: true,
            cover_at: 4.0,
            trim: Some((2.0, 8.0)),
            crop: Some(Crop { x: 420, y: 0, width: 1080, height: 1080 }),
            hdr_to_sdr: true,
            keep_audio: true,
            max_long_edge: 720,
            rate: Rate::High,
            encoder: EncoderChoice::Quality,
            still_quality: 90,
        };
        let targets = LiveTargets {
            apple: Some((dir.join("jellyfish-live.jpg"), dir.join("jellyfish-live.mov"))),
            android: Some(dir.join("jellyfish-live.MP.jpg")),
        };
        let mut last = 0.0;
        let outputs = make(&tools, &input, &info, &recipe, &targets, &mut |p| last = p, &AtomicBool::new(false)).unwrap();
        assert_eq!(last, 1.0);
        assert_eq!(outputs.len(), 3);
        assert!(outputs[0].path.ends_with("jellyfish-live.jpg") && outputs[1].path.ends_with("jellyfish-live.mov"));
        assert_eq!((outputs[0].width, outputs[0].height), (1080, 1080));
        assert_eq!((outputs[1].width, outputs[1].height), (720, 720));

        // MOV：3 秒、720×720、H.264，多一条元数据轨。
        let mov = video::probe(&tools, &targets.apple.as_ref().unwrap().1).unwrap();
        assert_eq!((mov.width, mov.height, mov.codec.as_str()), (720, 720, "h264"));
        assert!((mov.duration - 3.0).abs() < 0.15, "时长 {}", mov.duration);
        let data = std::fs::read(&targets.apple.as_ref().unwrap().1).unwrap();
        let meta = find(&data, &[b"moov", b"meta"]).unwrap();
        let id_text = String::from_utf8_lossy(meta.body(&data)).into_owned();
        let id = &id_text[id_text.len() - 36..];
        let jpg = std::fs::read(&targets.apple.as_ref().unwrap().0).unwrap();
        assert!(jpg.windows(36).any(|w| w == id.as_bytes()), "JPEG 里没有同一个标识");

        // 安卓：XMP 写的长度等于末尾 MP4 的长度，切出来能读。
        let mp = std::fs::read(targets.android.as_ref().unwrap()).unwrap();
        let text = String::from_utf8_lossy(&mp[..4096.min(mp.len())]).into_owned();
        let len: usize = text.split("Item:Length=\"").nth(2).unwrap().split('"').next().unwrap().parse().unwrap();
        let video_part = &mp[mp.len() - len..];
        assert_eq!(&video_part[4..8], b"ftyp");
        let cut = dir.join("cut.mp4");
        std::fs::write(&cut, video_part).unwrap();
        let v = video::probe(&tools, &cut).unwrap();
        assert_eq!((v.width, v.height), (720, 720));
        // 截取 2–8 秒、封面 4 秒 → 片段 2.5–5.5 秒，封面在片段里约 1.5 秒（对齐到帧）。
        let ts: i64 = text.split("MotionPhotoPresentationTimestampUs=\"").nth(1).unwrap().split('"').next().unwrap().parse().unwrap();
        assert!((ts - 1_500_000).abs() < 40_000, "封面时刻 {ts}");
        let _ = std::fs::remove_file(&cut);
        let _ = std::fs::remove_file(&input);
        assert!(!video::temp_path(&dir.join("jellyfish-live.clip.mov")).exists());
    }
}
