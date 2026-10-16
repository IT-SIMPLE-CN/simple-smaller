// jpegli 外壳。libjpeg 系的出错处理是 longjmp，在这里接住，换成返回值。
#include <setjmp.h>
#include <stdlib.h>

#include "codecs.h"
#include "lib/jpegli/common.h"
#include "lib/jpegli/encode.h"

namespace {

struct ErrorContext {
  struct jpeg_error_mgr pub;
  jmp_buf jump;
};

void OnError(j_common_ptr cinfo) {
  ErrorContext *ctx = reinterpret_cast<ErrorContext *>(cinfo->err);
  longjmp(ctx->jump, 1);
}

void OnMessage(j_common_ptr) {}

}  // namespace

extern "C" int smaller_jpegli_encode(const uint8_t *rgb, uint32_t width, uint32_t height,
                                     uint32_t stride, float distance, int progressive,
                                     int chroma_444, const uint8_t *icc, size_t icc_len,
                                     uint8_t **out, size_t *out_len) {
  struct jpeg_compress_struct cinfo;
  ErrorContext err;
  unsigned char *buffer = nullptr;
  unsigned long size = 0;  // NOLINT：libjpeg 接口要求 unsigned long

  cinfo.err = jpegli_std_error(&err.pub);
  err.pub.error_exit = OnError;
  err.pub.output_message = OnMessage;
  if (setjmp(err.jump)) {
    jpegli_destroy_compress(&cinfo);
    free(buffer);
    return -1;
  }

  jpegli_create_compress(&cinfo);
  jpegli_mem_dest(&cinfo, &buffer, &size);
  cinfo.image_width = width;
  cinfo.image_height = height;
  cinfo.input_components = 3;
  cinfo.in_color_space = JCS_RGB;
  jpegli_set_defaults(&cinfo);
  jpegli_set_distance(&cinfo, distance, TRUE);
  jpegli_set_progressive_level(&cinfo, progressive ? 2 : 0);
  if (chroma_444) {
    for (int i = 0; i < cinfo.num_components; ++i) {
      cinfo.comp_info[i].h_samp_factor = 1;
      cinfo.comp_info[i].v_samp_factor = 1;
    }
  }
  jpegli_start_compress(&cinfo, TRUE);
  if (icc != nullptr && icc_len > 0) {
    jpegli_write_icc_profile(&cinfo, icc, static_cast<unsigned int>(icc_len));
  }
  while (cinfo.next_scanline < cinfo.image_height) {
    JSAMPROW row = const_cast<JSAMPROW>(rgb + static_cast<size_t>(cinfo.next_scanline) * stride);
    jpegli_write_scanlines(&cinfo, &row, 1);
  }
  jpegli_finish_compress(&cinfo);
  jpegli_destroy_compress(&cinfo);

  *out = buffer;
  *out_len = size;
  return 0;
}

extern "C" float smaller_jpegli_quality_to_distance(int quality) {
  return jpegli_quality_to_distance(quality);
}

extern "C" void smaller_jpegli_free(uint8_t *data) { free(data); }
