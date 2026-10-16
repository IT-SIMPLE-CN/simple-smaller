// 编译 native/ 下的编码库外壳，并链接 scripts/build-codecs-mac.sh 装好的静态库。
// 编码库位置可用 SMALLER_CODECS_DIR 覆盖，默认 <仓库>/.local/codecs/<架构>。
use std::env;
use std::path::PathBuf;

fn main() {
    let manifest = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let root = manifest.parent().unwrap().to_path_buf();
    let arch = match env::var("CARGO_CFG_TARGET_ARCH").unwrap().as_str() {
        "aarch64" => "arm64".to_string(),
        other => other.to_string(),
    };
    let codecs = env::var("SMALLER_CODECS_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|_| root.join(".local/codecs").join(&arch));
    let jpegli_src = env::var("SMALLER_JPEGLI_SRC")
        .map(PathBuf::from)
        .unwrap_or_else(|_| root.join(".local/src/jpegli"));
    if !codecs.join("lib/libjpegli.a").exists() {
        panic!(
            "找不到编码库：{}。先运行 scripts/build-codecs-mac.sh",
            codecs.display()
        );
    }
    let include = codecs.join("include");

    cc::Build::new()
        .cpp(true)
        .std("c++17")
        .file("native/jpegli_shim.cc")
        .include("native")
        .include(&jpegli_src)
        .include(include.join("jpegli"))
        .warnings(false)
        .compile("smaller_jpegli_shim");

    cc::Build::new()
        .file("native/webp_shim.c")
        .file("native/avif_shim.c")
        .include("native")
        .include(&include)
        .compile("smaller_codec_shims");

    println!("cargo:rustc-link-search=native={}", codecs.join("lib").display());
    for lib in ["jpegli", "jpegli_cms", "lcms2", "hwy", "webp", "sharpyuv", "avif", "aom"] {
        println!("cargo:rustc-link-lib=static={lib}");
    }
    println!("cargo:rustc-link-lib=c++");
    println!("cargo:rerun-if-changed=native");
    println!("cargo:rerun-if-env-changed=SMALLER_CODECS_DIR");
}
