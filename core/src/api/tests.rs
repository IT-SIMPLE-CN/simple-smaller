use super::engine::*;
use super::types::*;
use std::path::Path;
use std::sync::{Arc, Mutex};

use std::sync::mpsc;

struct Collect(Mutex<mpsc::Sender<Option<JobItemResult>>>);
impl JobListener for Collect {
    fn on_progress(&self, _: u32, _: f64) {}
    fn on_item(&self, r: JobItemResult) {
        let _ = self.0.lock().unwrap_or_else(|e| e.into_inner()).send(Some(r));
    }
    fn on_finished(&self) {
        let _ = self.0.lock().unwrap_or_else(|e| e.into_inner()).send(None);
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
fn 剪贴板截图压成_png() {
    let dir = std::env::temp_dir().join(format!("smaller-clip-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    // 像截图：大块纯色、少量颜色，原图用无压缩的 BMP 当作「剪贴板里的大图」。
    let (w, h) = (900u32, 600u32);
    let mut rgba = Vec::new();
    for y in 0..h { for x in 0..w { rgba.extend_from_slice(if y < 60 { &[239, 57, 34, 255] } else if x % 150 == 0 { &[40, 40, 48, 255] } else { &[250, 250, 252, 255] }); } }
    let src = dir.join("clip.bmp");
    image::save_buffer(&src, &rgba, w, h, image::ExtendedColorType::Rgba8).unwrap();
    let engine = Engine::new(None);
    let out = engine.compress_for_clipboard(src.to_string_lossy().into(), 0).unwrap().expect("应压小");
    assert!(out.png, "截图应是 PNG");
    assert!((out.bytes.len() as u64) < out.before_bytes / 10, "{} → {}", out.before_bytes, out.bytes.len());
    let big = engine.compress_for_clipboard(src.to_string_lossy().into(), 450).unwrap().unwrap();
    assert_eq!((big.width, big.height), (450, 300));
}

#[test]
fn 命名示例() {
    let engine = Engine::new(None);
    assert_eq!(engine.preview_name(naming("{name}-{w}"), "新品 发布.jpg".into(), 2560, 1440), "xin-pin-fa-bu-2560");
}

#[test]
fn 图片任务端到端_撤销() {
    let dir = std::env::temp_dir().join(format!("smaller-api-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    let src = dir.join("新品 海报.png");
    let rgba: Vec<u8> = (0..1600u32 * 1000).flat_map(|k| [(k % 251) as u8, (k / 1600 % 256) as u8, 90, 255]).collect();
    image::save_buffer(&src, &rgba, 1600, 1000, image::ExtendedColorType::Rgba8).unwrap();

    let engine = Engine::new(None);

    let (tx, rx) = mpsc::channel();
    let job = engine.clone().run_images(vec![src.to_string_lossy().into()], Place::BesideSource { folder_suffix: "-small".into() }, naming("{name}-{w}"), settings(), Arc::new(Collect(Mutex::new(tx))));
    let results = wait(&rx);
    assert!(job.is_finished());
    assert_eq!(results.len(), 1);
    assert_eq!(results[0].state, ItemState::Done);
    assert!(results[0].outputs[0].path.ends_with("xin-pin-hai-bao-800.webp"), "{}", results[0].outputs[0].path);
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
    let picture = Engine::new(Some(Arc::new(Fake))).decode(&heic).unwrap();
    assert_eq!((picture.width, picture.height), (4, 2));
    assert!(Engine::new(None).decode(&heic).is_err());
}

/// 处理中出意外（panic）：整批不卡住，没报过的文件标成失败，一定报「完成」。
#[test]
fn 任务线程出意外_剩下的标失败并报完成() {
    struct Boom;
    impl PlatformDecoder for Boom {
        fn decode(&self, _: String) -> Option<RawImage> {
            panic!("测试用：解码器出意外");
        }
    }
    let dir = std::env::temp_dir().join(format!("smaller-api-panic-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    let ok = dir.join("a.png");
    image::save_buffer(&ok, &[90u8; 16 * 16 * 4], 16, 16, image::ExtendedColorType::Rgba8).unwrap();
    let bad = dir.join("b.heic");
    std::fs::write(&bad, b"fake").unwrap();
    let engine = Engine::new(Some(Arc::new(Boom)));
    let (tx, rx) = mpsc::channel();
    let inputs = vec![ok.to_string_lossy().into(), bad.to_string_lossy().into()];
    let job = engine.run_images(inputs, Place::BesideSource { folder_suffix: "-small".into() }, naming("{name}"), settings(), Arc::new(Collect(Mutex::new(tx))));
    let results = wait(&rx);
    assert!(job.is_finished());
    let mut indices: Vec<u32> = results.iter().map(|r| r.index).collect();
    indices.sort();
    assert_eq!(indices, [0, 1], "每个文件只报一次");
    let heic = results.iter().find(|r| r.index == 1).unwrap();
    assert_eq!(heic.state, ItemState::Failed);
    let _ = std::fs::remove_dir_all(&dir);
}

/// 实况走完整的任务：苹果实况一对同名，动态照片跟着叫 `.MP.jpg`；重名时整对一起换名字；撤销全部移走。
#[test]
fn 实况任务端到端_重名与撤销() {
    let Ok(tools) = crate::video::Tools::locate() else { return };
    let dir = std::env::temp_dir().join(format!("smaller-api-live-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    let src = dir.join("片段.mp4");
    // 有本地素材（.local/vtest/半秒.mp4，不进仓库）就用；没有就用 ffmpeg 的测试信号现场生成一段半秒的。
    let sample = Path::new(env!("CARGO_MANIFEST_DIR")).join("../.local/vtest/半秒.mp4");
    if sample.exists() {
        std::fs::copy(&sample, &src).unwrap();
    } else {
        let status = crate::video::tool(&tools.ffmpeg)
            .args(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=0.5"])
            .args(["-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p"])
            .arg(&src)
            .status()
            .unwrap();
        assert!(status.success(), "生成测试视频失败");
    }
    // 先占掉 .mov：整对要换成 -2。
    std::fs::write(dir.join("片段-live.mov"), b"x").unwrap();
    let settings = VideoSettings {
        codec: VideoCodecChoice::H264, encoder: VideoEncoderChoice::Quality, rate: VideoRate::High, max_short_edge: None, keep_audio: true,
        trim_start: None, trim_end: None, crop: None, hdr_to_sdr: true, max_fps: None,
        output: VideoOutput::Live { apple: true, android: true, cover_at: 0.2, max_long_edge: 1920 }, cover_at: None,
    };
    let engine = Engine::new(None);
    let (tx, rx) = mpsc::channel();
    let naming = Naming { template: "{name}-live".into(), lowercase: false, pinyin: false, hyphenate: false, date: "20261018".into() };
    let job = engine.clone().run_videos(vec![src.to_string_lossy().into()], Place::Folder { path: dir.to_string_lossy().into() }, naming, settings.clone(), Arc::new(Collect(Mutex::new(tx))));
    let results = wait(&rx);
    assert_eq!(results.len(), 1);
    assert_eq!(results[0].state, ItemState::Done, "{:?}", results[0].message);
    let names: Vec<String> = results[0].outputs.iter().map(|o| Path::new(&o.path).file_name().unwrap().to_string_lossy().into_owned()).collect();
    assert_eq!(names, ["片段-live-2.jpg", "片段-live-2.mov", "片段-live-2.MP.jpg"]);
    job.undo().unwrap();
    assert!(results[0].outputs.iter().all(|o| !Path::new(&o.path).exists()));

    // 只出动态照片、同名已有：换成「名-2.MP.jpg」（不是「名.MP-2.jpg」，安卓相册按 .MP.jpg 认）。
    std::fs::write(dir.join("片段-live.MP.jpg"), b"x").unwrap();
    let android_only = VideoSettings { output: VideoOutput::Live { apple: false, android: true, cover_at: 0.2, max_long_edge: 1920 }, ..settings };
    let (tx, rx) = mpsc::channel();
    let naming = Naming { template: "{name}-live".into(), lowercase: false, pinyin: false, hyphenate: false, date: "20261018".into() };
    let _job = engine.run_videos(vec![src.to_string_lossy().into()], Place::Folder { path: dir.to_string_lossy().into() }, naming, android_only, Arc::new(Collect(Mutex::new(tx))));
    let results = wait(&rx);
    assert_eq!(results[0].state, ItemState::Done, "{:?}", results[0].message);
    let names: Vec<String> = results[0].outputs.iter().map(|o| Path::new(&o.path).file_name().unwrap().to_string_lossy().into_owned()).collect();
    assert_eq!(names, ["片段-live-2.MP.jpg"]);
    let _ = std::fs::remove_dir_all(&dir);
}
