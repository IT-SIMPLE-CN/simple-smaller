// libwebp 外壳：用高级配置接口，方便设 method、sharp_yuv 与目标体积。
#include "codecs.h"
#include "webp/encode.h"

int smaller_webp_encode(const uint8_t *rgba, uint32_t width, uint32_t height, uint32_t stride,
                        int has_alpha, float quality, int lossless, int method, int sharp_yuv,
                        int target_size, uint8_t **out, size_t *out_len) {
  WebPConfig config;
  if (!WebPConfigInit(&config)) return -1;
  if (lossless) {
    // 无损预设按压缩等级同时设 method 与 quality，再按调用方的 method 覆盖速度档。
    if (!WebPConfigLosslessPreset(&config, method > 6 ? 6 : method)) return -1;
  } else {
    config.quality = quality;
  }
  config.method = method;
  config.use_sharp_yuv = sharp_yuv;
  config.thread_level = 1;
  config.alpha_quality = 100;
  if (target_size > 0 && !lossless) {
    config.target_size = target_size;
    config.pass = 6;
  }
  if (!WebPValidateConfig(&config)) return -2;

  WebPPicture picture;
  if (!WebPPictureInit(&picture)) return -1;
  picture.use_argb = lossless ? 1 : 0;
  picture.width = (int)width;
  picture.height = (int)height;
  int imported = has_alpha ? WebPPictureImportRGBA(&picture, rgba, (int)stride)
                           : WebPPictureImportRGBX(&picture, rgba, (int)stride);
  if (!imported) {
    WebPPictureFree(&picture);
    return -3;
  }

  WebPMemoryWriter writer;
  WebPMemoryWriterInit(&writer);
  picture.writer = WebPMemoryWrite;
  picture.custom_ptr = &writer;
  int ok = WebPEncode(&config, &picture);
  int error = picture.error_code;
  WebPPictureFree(&picture);
  if (!ok) {
    WebPMemoryWriterClear(&writer);
    return -10 - error;
  }
  *out = writer.mem;
  *out_len = writer.size;
  return 0;
}

void smaller_webp_free(uint8_t *data) { WebPFree(data); }
