//! 对外接口（UniFFI）：Swift / C# 端只通过这里调用引擎。
//!
//! - 配方：图片、视频、PDF 各一套设置，对应界面上的选项。
//! - 任务：后台线程执行，逐个文件回调进度与结果，可取消、可整批撤销。
//! - 平台解码：mac 端注入 ImageIO，用来读 HEIC 等引擎不认识的格式。

mod engine;
mod job;
mod pdf_items;
mod types;
mod video_item;
#[cfg(test)]
mod tests;

pub use engine::*;
pub use job::*;
pub use types::*;
