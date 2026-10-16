//! 简单小一点的压缩引擎。
//!
//! 对外接口全部经 UniFFI 导出，mac 端生成 Swift、Windows 端生成 C#。
//! 这一版只打通链路：读版本号、探测图片尺寸。

uniffi::setup_scaffolding!();

pub mod ffi;
pub mod image;
pub mod video;
pub mod pdf;

/// 引擎对外报错。界面按种类给出提示，`message` 只用于日志。
#[derive(Debug, thiserror::Error, uniffi::Error)]
pub enum EngineError {
    #[error("文件读不到：{message}")]
    Io { message: String },
    #[error("不认识的文件格式：{message}")]
    Unsupported { message: String },
}

/// 图片的基本信息，只读文件头，不解码像素。
#[derive(Debug, Clone, uniffi::Record)]
pub struct ImageInfo {
    pub width: u32,
    pub height: u32,
    pub format: String,
}

#[uniffi::export]
pub fn engine_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

#[uniffi::export]
pub fn probe_image(path: String) -> Result<ImageInfo, EngineError> {
    let bytes = std::fs::read(&path).map_err(|e| EngineError::Io { message: e.to_string() })?;
    let size = imagesize::blob_size(&bytes)
        .map_err(|e| EngineError::Unsupported { message: e.to_string() })?;
    let format = imagesize::image_type(&bytes)
        .map(|t| format!("{t:?}").to_lowercase())
        .unwrap_or_else(|_| "unknown".to_string());
    Ok(ImageInfo {
        width: size.width as u32,
        height: size.height as u32,
        format,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn 探测不存在的文件返回读取错误() {
        let err = probe_image("/不存在/的文件.jpg".to_string()).unwrap_err();
        assert!(matches!(err, EngineError::Io { .. }));
    }
}
