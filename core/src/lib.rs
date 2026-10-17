//! 简单小一点的压缩引擎。
//!
//! 对外接口全部经 UniFFI 导出（见 `api`），mac 端生成 Swift、Windows 端生成 C#。

uniffi::setup_scaffolding!();

pub mod ffi;
pub mod image;
pub mod video;
pub mod pdf;
pub mod naming;
pub mod jobs;
pub mod api;

/// 引擎对外报错。界面按种类给出提示，`message` 只用于日志。
#[derive(Debug, thiserror::Error, uniffi::Error)]
pub enum EngineError {
    #[error("文件读不到：{message}")]
    Io { message: String },
    #[error("不认识的文件格式：{message}")]
    Unsupported { message: String },
    #[error("{message}")]
    Failed { message: String },
}
