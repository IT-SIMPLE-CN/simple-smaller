// Windows（MSVC）上把 MuPDF 与编码库的静态库再交给链接器一次。
// 引擎库（smaller-core）作为 rlib 被链进可执行文件时，这些静态库被打包进 rlib；MuPDF 的库是用 /GL（整体程序优化）编的，
// 打包后链接器在 /LTCG 重启时找不到其中的符号（fz_* 无法解析）。直接再给一次原库即可（同一符号只会取一份）。
// 位置与 core/build.rs 相同：默认 <仓库>/.local/{mupdf,codecs}/win-<架构>，可用 SMALLER_MUPDF_DIR / SMALLER_CODECS_DIR 覆盖。
use std::env;
use std::path::PathBuf;

fn main() {
    println!("cargo:rerun-if-env-changed=SMALLER_MUPDF_DIR");
    println!("cargo:rerun-if-env-changed=SMALLER_CODECS_DIR");
    if env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }
    let root = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap()).parent().unwrap().to_path_buf();
    let arch = if env::var("CARGO_CFG_TARGET_ARCH").as_deref() == Ok("aarch64") { "win-arm64" } else { "win-x64" };
    let mupdf = env::var("SMALLER_MUPDF_DIR").map(PathBuf::from).unwrap_or_else(|_| root.join(".local/mupdf").join(arch)).join("lib");
    let codecs = env::var("SMALLER_CODECS_DIR").map(PathBuf::from).unwrap_or_else(|_| root.join(".local/codecs").join(arch)).join("lib");
    println!("cargo:rustc-link-search=native={}", mupdf.display());
    println!("cargo:rustc-link-search=native={}", codecs.display());
    for lib in ["mupdf", "mupdf-third", "mupdf-resources", "mupdf-pkcs7", "mupdf-harfbuzz", "mupdf-extract", "mupdf-barcode", "mupdf-zxing"] {
        if mupdf.join(format!("{lib}.lib")).exists() {
            println!("cargo:rustc-link-lib=static={lib}");
        }
    }
    for lib in ["jpegli", "jpegli_cms", "lcms2", "hwy", "webp", "sharpyuv", "avif", "aom"] {
        if codecs.join(format!("{lib}.lib")).exists() {
            println!("cargo:rustc-link-lib=static={lib}");
        }
    }
}
