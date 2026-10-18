//! 文件系统小工具。

use std::io;
use std::path::Path;

/// 把写好的临时文件改成正式名字。
///
/// Windows 上刚写完的文件常被杀毒软件（Defender）、搜索索引或缩略图服务短暂打开，
/// 这时改名会报「另一个程序正在使用此文件」（32）或「拒绝访问」（5）。这类错误等一会儿就好，
/// 所以最多重试约 2 秒；其他错误和其他平台直接返回。
pub fn rename(from: &Path, to: &Path) -> io::Result<()> {
    let mut wait = std::time::Duration::from_millis(20);
    let mut waited = std::time::Duration::ZERO;
    loop {
        match std::fs::rename(from, to) {
            Err(e) if cfg!(windows) && is_transient(&e) && waited < std::time::Duration::from_secs(2) => {
                std::thread::sleep(wait);
                waited += wait;
                wait = (wait * 2).min(std::time::Duration::from_millis(250));
            }
            other => return other,
        }
    }
}

/// 32 = ERROR_SHARING_VIOLATION，33 = ERROR_LOCK_VIOLATION，5 = ERROR_ACCESS_DENIED。
fn is_transient(e: &io::Error) -> bool {
    matches!(e.raw_os_error(), Some(32 | 33 | 5))
}
