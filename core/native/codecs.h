// 图片编码库的薄外壳：只收像素与参数，返回编码后的字节。
// 出错返回负数，不让 C 库的 longjmp 或内部结构穿过 FFI 边界。
// 输出缓冲区由对应的 *_free 释放。
#ifndef SMALLER_CODECS_H
#define SMALLER_CODECS_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// jpegli。rgb 为紧凑 RGB8；distance 是 butteraugli 距离，越小越清晰。
int smaller_jpegli_encode(const uint8_t *rgb, uint32_t width, uint32_t height, uint32_t stride,
                          float distance, int progressive, int chroma_444,
                          const uint8_t *icc, size_t icc_len,
                          uint8_t **out, size_t *out_len);
float smaller_jpegli_quality_to_distance(int quality);
void smaller_jpegli_free(uint8_t *data);

// libwebp。rgba 为 RGBA8；target_size > 0 时按目标字节数多遍逼近。
int smaller_webp_encode(const uint8_t *rgba, uint32_t width, uint32_t height, uint32_t stride,
                        int has_alpha, float quality, int lossless, int method, int sharp_yuv,
                        int target_size, uint8_t **out, size_t *out_len);
void smaller_webp_free(uint8_t *data);

// libavif + aom。quality 0–100，speed 0–10（越大越快）。
int smaller_avif_encode(const uint8_t *rgba, uint32_t width, uint32_t height, uint32_t stride,
                        int has_alpha, int quality, int speed, int yuv444,
                        const uint8_t *icc, size_t icc_len,
                        uint8_t **out, size_t *out_len);
// 解码 AVIF 为 RGBA8，输出由 smaller_avif_free_pixels 释放。
int smaller_avif_decode(const uint8_t *data, size_t len,
                        uint8_t **rgba, uint32_t *width, uint32_t *height, int *has_alpha);
void smaller_avif_free(uint8_t *data, size_t len);
void smaller_avif_free_pixels(uint8_t *rgba);

#ifdef __cplusplus
}
#endif

#endif
