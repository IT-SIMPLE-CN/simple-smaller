// libavif 外壳：编码固定选 aom 并开 tune=iq（为静态图画质调过的模式）。
#include <stdlib.h>
#include <string.h>

#include "avif/avif.h"
#include "codecs.h"

int smaller_avif_encode(const uint8_t *rgba, uint32_t width, uint32_t height, uint32_t stride,
                        int has_alpha, int quality, int speed, int yuv444,
                        const uint8_t *icc, size_t icc_len,
                        uint8_t **out, size_t *out_len) {
  avifImage *image = avifImageCreate(width, height, 8,
                                     yuv444 ? AVIF_PIXEL_FORMAT_YUV444 : AVIF_PIXEL_FORMAT_YUV420);
  if (image == NULL) return -1;
  image->yuvRange = AVIF_RANGE_FULL;
  image->colorPrimaries = AVIF_COLOR_PRIMARIES_BT709;
  image->transferCharacteristics = AVIF_TRANSFER_CHARACTERISTICS_SRGB;
  image->matrixCoefficients = AVIF_MATRIX_COEFFICIENTS_BT601;
  if (icc != NULL && icc_len > 0 && avifImageSetProfileICC(image, icc, icc_len) != AVIF_RESULT_OK) {
    avifImageDestroy(image);
    return -2;
  }

  avifRGBImage rgb;
  avifRGBImageSetDefaults(&rgb, image);
  rgb.format = AVIF_RGB_FORMAT_RGBA;
  rgb.depth = 8;
  rgb.pixels = (uint8_t *)rgba;
  rgb.rowBytes = stride;
  rgb.ignoreAlpha = has_alpha ? AVIF_FALSE : AVIF_TRUE;
  if (avifImageRGBToYUV(image, &rgb) != AVIF_RESULT_OK) {
    avifImageDestroy(image);
    return -3;
  }

  avifEncoder *encoder = avifEncoderCreate();
  if (encoder == NULL) {
    avifImageDestroy(image);
    return -1;
  }
  encoder->codecChoice = AVIF_CODEC_CHOICE_AOM;
  encoder->quality = quality;
  encoder->qualityAlpha = has_alpha ? (quality + 10 > 100 ? 100 : quality + 10) : 100;
  encoder->speed = speed;
  encoder->maxThreads = 8;
  // 只给颜色通道设 tune=iq：不带前缀会同时作用到透明通道，aom 的透明通道编码不接受，透明图会编码失败。
  if (avifEncoderSetCodecSpecificOption(encoder, "c:tune", "iq") != AVIF_RESULT_OK) {
    avifEncoderDestroy(encoder);
    avifImageDestroy(image);
    return -4;
  }

  avifRWData output = AVIF_DATA_EMPTY;
  avifResult result = avifEncoderWrite(encoder, image, &output);
  avifEncoderDestroy(encoder);
  avifImageDestroy(image);
  if (result != AVIF_RESULT_OK) {
    avifRWDataFree(&output);
    return -10 - (int)result;
  }
  *out = output.data;
  *out_len = output.size;
  return 0;
}

int smaller_avif_decode(const uint8_t *data, size_t len,
                        uint8_t **rgba, uint32_t *width, uint32_t *height, int *has_alpha) {
  avifDecoder *decoder = avifDecoderCreate();
  if (decoder == NULL) return -1;
  decoder->maxThreads = 8;
  avifResult result = avifDecoderSetIOMemory(decoder, data, len);
  if (result == AVIF_RESULT_OK) result = avifDecoderParse(decoder);
  if (result == AVIF_RESULT_OK) result = avifDecoderNextImage(decoder);
  if (result != AVIF_RESULT_OK) {
    avifDecoderDestroy(decoder);
    return -10 - (int)result;
  }

  avifRGBImage rgb;
  avifRGBImageSetDefaults(&rgb, decoder->image);
  rgb.format = AVIF_RGB_FORMAT_RGBA;
  rgb.depth = 8;
  size_t row_bytes = (size_t)rgb.width * 4;
  uint8_t *pixels = (uint8_t *)malloc(row_bytes * rgb.height);
  if (pixels == NULL) {
    avifDecoderDestroy(decoder);
    return -1;
  }
  rgb.pixels = pixels;
  rgb.rowBytes = (uint32_t)row_bytes;
  result = avifImageYUVToRGB(decoder->image, &rgb);
  *has_alpha = decoder->alphaPresent ? 1 : 0;
  *width = rgb.width;
  *height = rgb.height;
  avifDecoderDestroy(decoder);
  if (result != AVIF_RESULT_OK) {
    free(pixels);
    return -10 - (int)result;
  }
  *rgba = pixels;
  return 0;
}

void smaller_avif_free(uint8_t *data, size_t len) {
  avifRWData d = {data, len};
  avifRWDataFree(&d);
}

void smaller_avif_free_pixels(uint8_t *rgba) { free(rgba); }
