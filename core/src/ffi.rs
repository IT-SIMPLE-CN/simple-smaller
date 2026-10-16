//! native/ 编码库外壳的 Rust 侧声明与安全封装。

use std::os::raw::c_int;

unsafe extern "C" {
    fn smaller_jpegli_encode(
        rgb: *const u8, width: u32, height: u32, stride: u32,
        distance: f32, progressive: c_int, chroma_444: c_int,
        icc: *const u8, icc_len: usize,
        out: *mut *mut u8, out_len: *mut usize,
    ) -> c_int;
    fn smaller_jpegli_quality_to_distance(quality: c_int) -> f32;
    fn smaller_jpegli_free(data: *mut u8);

    fn smaller_webp_encode(
        rgba: *const u8, width: u32, height: u32, stride: u32,
        has_alpha: c_int, quality: f32, lossless: c_int, method: c_int, sharp_yuv: c_int,
        target_size: c_int, out: *mut *mut u8, out_len: *mut usize,
    ) -> c_int;
    fn smaller_webp_free(data: *mut u8);

    fn smaller_avif_encode(
        rgba: *const u8, width: u32, height: u32, stride: u32,
        has_alpha: c_int, quality: c_int, speed: c_int, yuv444: c_int,
        icc: *const u8, icc_len: usize,
        out: *mut *mut u8, out_len: *mut usize,
    ) -> c_int;
    fn smaller_avif_decode(
        data: *const u8, len: usize,
        rgba: *mut *mut u8, width: *mut u32, height: *mut u32, has_alpha: *mut c_int,
    ) -> c_int;
    fn smaller_avif_free(data: *mut u8, len: usize);
    fn smaller_avif_free_pixels(rgba: *mut u8);
}

/// 编码库返回的负数错误码。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CodecError(pub i32);

fn icc_parts(icc: Option<&[u8]>) -> (*const u8, usize) {
    match icc {
        Some(b) if !b.is_empty() => (b.as_ptr(), b.len()),
        _ => (std::ptr::null(), 0),
    }
}

pub struct JpegParams {
    pub distance: f32,
    pub progressive: bool,
    pub chroma_444: bool,
}

pub fn jpegli_quality_to_distance(quality: u8) -> f32 {
    unsafe { smaller_jpegli_quality_to_distance(quality as c_int) }
}

/// `rgb` 为紧凑排列的 RGB8。
pub fn jpegli_encode(rgb: &[u8], width: u32, height: u32, params: &JpegParams, icc: Option<&[u8]>) -> Result<Vec<u8>, CodecError> {
    assert_eq!(rgb.len(), width as usize * height as usize * 3);
    let (icc_ptr, icc_len) = icc_parts(icc);
    let mut out = std::ptr::null_mut();
    let mut len = 0usize;
    let code = unsafe {
        smaller_jpegli_encode(
            rgb.as_ptr(), width, height, width * 3,
            params.distance, params.progressive as c_int, params.chroma_444 as c_int,
            icc_ptr, icc_len, &mut out, &mut len,
        )
    };
    if code != 0 {
        return Err(CodecError(code));
    }
    let bytes = unsafe { std::slice::from_raw_parts(out, len).to_vec() };
    unsafe { smaller_jpegli_free(out) };
    Ok(bytes)
}

pub struct WebpParams {
    pub quality: f32,
    pub lossless: bool,
    /// 0–6，越大越慢越小。
    pub method: u8,
    pub sharp_yuv: bool,
    pub target_size: Option<u32>,
}

/// `rgba` 为紧凑排列的 RGBA8。
pub fn webp_encode(rgba: &[u8], width: u32, height: u32, has_alpha: bool, params: &WebpParams) -> Result<Vec<u8>, CodecError> {
    assert_eq!(rgba.len(), width as usize * height as usize * 4);
    let mut out = std::ptr::null_mut();
    let mut len = 0usize;
    let code = unsafe {
        smaller_webp_encode(
            rgba.as_ptr(), width, height, width * 4,
            has_alpha as c_int, params.quality, params.lossless as c_int,
            params.method as c_int, params.sharp_yuv as c_int,
            params.target_size.map(|s| s as c_int).unwrap_or(0),
            &mut out, &mut len,
        )
    };
    if code != 0 {
        return Err(CodecError(code));
    }
    let bytes = unsafe { std::slice::from_raw_parts(out, len).to_vec() };
    unsafe { smaller_webp_free(out) };
    Ok(bytes)
}

pub struct AvifParams {
    pub quality: u8,
    /// 0–10，越大越快。
    pub speed: u8,
    pub yuv444: bool,
}

pub fn avif_encode(rgba: &[u8], width: u32, height: u32, has_alpha: bool, params: &AvifParams, icc: Option<&[u8]>) -> Result<Vec<u8>, CodecError> {
    assert_eq!(rgba.len(), width as usize * height as usize * 4);
    let (icc_ptr, icc_len) = icc_parts(icc);
    let mut out = std::ptr::null_mut();
    let mut len = 0usize;
    let code = unsafe {
        smaller_avif_encode(
            rgba.as_ptr(), width, height, width * 4,
            has_alpha as c_int, params.quality as c_int, params.speed as c_int, params.yuv444 as c_int,
            icc_ptr, icc_len, &mut out, &mut len,
        )
    };
    if code != 0 {
        return Err(CodecError(code));
    }
    let bytes = unsafe { std::slice::from_raw_parts(out, len).to_vec() };
    unsafe { smaller_avif_free(out, len) };
    Ok(bytes)
}

/// 返回 (RGBA8, 宽, 高, 是否带透明)。
pub fn avif_decode(data: &[u8]) -> Result<(Vec<u8>, u32, u32, bool), CodecError> {
    let mut pixels = std::ptr::null_mut();
    let (mut w, mut h, mut alpha) = (0u32, 0u32, 0 as c_int);
    let code = unsafe { smaller_avif_decode(data.as_ptr(), data.len(), &mut pixels, &mut w, &mut h, &mut alpha) };
    if code != 0 {
        return Err(CodecError(code));
    }
    let len = w as usize * h as usize * 4;
    let rgba = unsafe { std::slice::from_raw_parts(pixels, len).to_vec() };
    unsafe { smaller_avif_free_pixels(pixels) };
    Ok((rgba, w, h, alpha != 0))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn gradient(w: u32, h: u32) -> Vec<u8> {
        let mut v = Vec::with_capacity((w * h * 4) as usize);
        for y in 0..h {
            for x in 0..w {
                v.extend_from_slice(&[(x * 255 / w) as u8, (y * 255 / h) as u8, 128, 255]);
            }
        }
        v
    }

    #[test]
    fn 三种编码器都能编出有效文件() {
        let (w, h) = (64, 48);
        let rgba = gradient(w, h);
        let rgb: Vec<u8> = rgba.chunks(4).flat_map(|p| [p[0], p[1], p[2]]).collect();

        let jpg = jpegli_encode(&rgb, w, h, &JpegParams { distance: 1.0, progressive: true, chroma_444: false }, None).unwrap();
        assert_eq!(&jpg[..2], &[0xFF, 0xD8]);

        let webp = webp_encode(&rgba, w, h, false, &WebpParams { quality: 80.0, lossless: false, method: 4, sharp_yuv: true, target_size: None }).unwrap();
        assert_eq!(&webp[8..12], b"WEBP");

        let avif = avif_encode(&rgba, w, h, false, &AvifParams { quality: 60, speed: 8, yuv444: false }, None).unwrap();
        assert_eq!(&avif[4..8], b"ftyp");
        let (decoded, dw, dh, alpha) = avif_decode(&avif).unwrap();
        assert_eq!((dw, dh, alpha), (w, h, false));
        assert_eq!(decoded.len(), rgba.len());
    }
}
