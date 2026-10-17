//! 视频：一段一段处理（转码、只去声音、动图、序列帧与封面）。

use std::path::PathBuf;
use std::sync::atomic::AtomicBool;
use std::sync::Arc;

use crate::image::{self as img, OutputFormat, Picture, SizeMode};
use crate::jobs::{self, ItemResult, ItemStatus, OutputFile};
use crate::naming::{self};
use crate::video;
use super::engine::*;
use super::types::*;

impl Engine {
    #[allow(clippy::too_many_arguments)]
    pub(super) fn video_item(
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
                    // 没截取、没裁、没缩小，压完还更大就保留原文件（包括动图转 MP4 反而变大的情况）。
                    let unchanged = settings.trim().is_none() && settings.crop.is_none() && settings.max_short_edge.is_none_or(|m| m >= info.width.min(info.height));
                    if unchanged && bytes >= before {
                        let _ = std::fs::remove_file(&path);
                        result.status = ItemStatus::KeptOriginal;
                    } else if let Ok(out) = video::probe(tools, &path) {
                        result.outputs.push(OutputFile { path, bytes, width: out.width, height: out.height });
                    }
                })
            }
            // 本来就没有声音：不用处理，保留原文件。
            VideoOutput::Mute if !info.has_audio => {
                result.status = ItemStatus::KeptOriginal;
                Ok(())
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
}
