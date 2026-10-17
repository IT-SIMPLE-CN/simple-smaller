//! PDF：压缩、合并、拆分、转图片、图片转 PDF 的逐个处理。

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use crate::image::{self as img, OutputFormat, Picture};
use crate::jobs::{self, ItemResult, ItemStatus, OutputFile, Plan};
use crate::naming::{self};
use crate::pdf;
use super::engine::*;
use super::types::*;

impl Engine {
    pub(super) fn pdf_items(&self, plan: &Plan, naming: &Naming, settings: &PdfSettings, listener: &Arc<dyn JobListener>, cancel: &AtomicBool) -> Vec<ItemResult> {
        let mut reserved = std::collections::HashSet::new();
        let mut out = Vec::new();
        let Some(first) = plan.items.first() else { return out };
        match &settings.operation {
            PdfOperation::Merge | PdfOperation::FromImages { .. } | PdfOperation::Assemble { .. } => {
                let sources: Vec<PathBuf> = plan.items.iter().map(|i| i.source.clone()).collect();
                let name = self.name(naming, &first.source, &first.folder, 0, 0, 0);
                let path = naming::unique_path(&first.out_dir, &name, "pdf", &mut reserved);
                let before: u64 = sources.iter().map(|s| file_bytes(s)).sum();
                let r = match &settings.operation {
                    PdfOperation::Merge => pdf::merge(&sources, &path, settings.strip_metadata),
                    PdfOperation::Assemble { pages } => {
                        let refs: Vec<pdf::PageRef> = pages.iter().map(|p| pdf::PageRef { doc: p.file, page: p.page }).collect();
                        pdf::assemble(&sources, &refs, &path, settings.strip_metadata)
                    }
                    PdfOperation::FromImages { page_mode } => {
                        let mode = match page_mode {
                            PdfPageMode::FitImage => pdf::PageMode::FitImage,
                            PdfPageMode::A4 => pdf::PageMode::A4,
                        };
                        pdf::images_to_pdf(&sources, mode, &path, &|p| self.decode(p))
                    }
                    _ => unreachable!(),
                };
                out.push(match r {
                    Ok(()) => ItemResult { index: 0, source: first.source.clone(), before_bytes: before, outputs: vec![OutputFile { bytes: file_bytes(&path), path, width: 0, height: 0 }], status: ItemStatus::Done },
                    Err(e) => failed(0, &first.source, e.to_string()),
                });
            }
            op => {
                for (i, item) in plan.items.iter().enumerate() {
                    if cancel.load(Ordering::Relaxed) {
                        out.push(ItemResult { index: i, source: item.source.clone(), before_bytes: 0, outputs: vec![], status: ItemStatus::Cancelled });
                        continue;
                    }
                    listener.on_progress(i as u32, 0.0);
                    let before = file_bytes(&item.source);
                    let name = self.name(naming, &item.source, &item.folder, i, 0, 0);
                    let mut result = ItemResult { index: i, source: item.source.clone(), before_bytes: before, outputs: vec![], status: ItemStatus::Done };
                    let _ = std::fs::create_dir_all(&item.out_dir);
                    let r: Result<(), String> = match op {
                        PdfOperation::Compress { level, target_bytes, subset_fonts, grayscale } => {
                            let level = match level {
                                PdfLevel::Screen => pdf::CompressLevel::SCREEN,
                                PdfLevel::Email => pdf::CompressLevel::EMAIL,
                                PdfLevel::Print => pdf::CompressLevel::PRINT,
                            };
                            let path = naming::unique_path(&item.out_dir, &name, "pdf", &mut reserved);
                            let recipe = pdf::PdfRecipe { level, target_bytes: *target_bytes, subset_fonts: *subset_fonts, strip_metadata: settings.strip_metadata, grayscale: *grayscale };
                            pdf::compress(&item.source, &path, &recipe).map_err(|e| e.to_string()).map(|o| match o {
                                pdf::CompressOutcome::Written { bytes, .. } => result.outputs.push(OutputFile { path, bytes, width: 0, height: 0 }),
                                pdf::CompressOutcome::KeptOriginal => result.status = ItemStatus::KeptOriginal,
                            })
                        }
                        PdfOperation::Split { every } => {
                            let dir = naming::unique_path(&item.out_dir, &format!("{name}-拆分"), "", &mut reserved);
                            let _ = std::fs::create_dir_all(&dir);
                            pdf::split_every(&item.source, *every, &dir, settings.strip_metadata).map_err(|e| e.to_string()).map(|files| {
                                result.outputs = files.into_iter().map(|path| OutputFile { bytes: file_bytes(&path), path, width: 0, height: 0 }).collect();
                            })
                        }
                        PdfOperation::ToImages { width, format, quality, long_image } => {
                            let format: OutputFormat = (*format).into();
                            let format = if format == OutputFormat::Original { OutputFormat::Jpeg } else { format };
                            // WebP 单张最高 16383 像素，JPEG 65535。
                            let max_h = if format == OutputFormat::Webp { 16_383 } else { 65_000 };
                            let pictures: Result<Vec<Picture>, String> = if *long_image {
                                pdf::render_long_image(&item.source, *width, max_h).map_err(|e| e.to_string())
                            } else {
                                pdf::info(&item.source).map_err(|e| e.to_string()).and_then(|i| {
                                    (0..i.pages).map(|p| pdf::render_page(&item.source, p, *width).map_err(|e| e.to_string())).collect()
                                })
                            };
                            pictures.and_then(|pictures| {
                                let total = pictures.len();
                                // 逐页的多张图放进「原名-图片」文件夹；长图（通常一张）放在旁边。编号按张数补零。
                                let digits = total.to_string().len().max(2);
                                let dir = if !*long_image && total > 1 {
                                    let d = naming::unique_path(&item.out_dir, &format!("{name}-图片"), "", &mut reserved);
                                    let _ = std::fs::create_dir_all(&d);
                                    d
                                } else {
                                    item.out_dir.clone()
                                };
                                for (k, picture) in pictures.into_iter().enumerate() {
                                    let bytes = img::encode(&picture, format, *quality, false).map_err(|e| e.to_string())?;
                                    let stem = if total == 1 { name.clone() } else { format!("{name}-{:0digits$}", k + 1) };
                                    let path = naming::unique_path(&dir, &stem, format.extension(), &mut reserved);
                                    jobs::write_atomic(&path, &bytes).map_err(|e| e.to_string())?;
                                    result.outputs.push(OutputFile { path, bytes: bytes.len() as u64, width: picture.width, height: picture.height });
                                    listener.on_progress(i as u32, (k + 1) as f64 / total as f64);
                                }
                                Ok(())
                            })
                        }
                        _ => unreachable!(),
                    };
                    if let Err(e) = r {
                        result.status = ItemStatus::Failed(e);
                    }
                    listener.on_progress(i as u32, 1.0);
                    out.push(result);
                }
            }
        }
        out
    }
}
