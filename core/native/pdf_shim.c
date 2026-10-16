// MuPDF 外壳。MuPDF 的错误处理基于 setjmp（fz_try / fz_catch），在这里接住，
// 换成返回值与错误信息，不让异常跳转穿过 FFI。
#include <stdio.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

#include "mupdf/fitz.h"
#include "mupdf/pdf.h"
#include "pdf_shim.h"

static void set_error(fz_context *ctx, char *err, size_t err_len) {
  if (err == NULL || err_len == 0) return;
  snprintf(err, err_len, "%s", ctx ? fz_caught_message(ctx) : "无法创建 MuPDF 上下文");
}

// MuPDF 默认把错误和警告打印到 stderr；错误已通过返回值传出，这里静默。
static void quiet(void *user, const char *message) {
  (void)user;
  (void)message;
}

static fz_context *new_context(void) {
  fz_context *ctx = fz_new_context(NULL, NULL, FZ_STORE_DEFAULT);
  if (ctx == NULL) return NULL;
  fz_set_error_callback(ctx, quiet, NULL);
  fz_set_warning_callback(ctx, quiet, NULL);
  fz_try(ctx) { fz_register_document_handlers(ctx); }
  fz_catch(ctx) {
    fz_drop_context(ctx);
    return NULL;
  }
  return ctx;
}

static pdf_write_options compact_write_options(void) {
  pdf_write_options opts = pdf_default_write_options;
  opts.do_garbage = 4;  // 回收并深比较去重复对象
  opts.do_compress = 1;
  opts.do_compress_images = 1;
  opts.do_compress_fonts = 1;
  opts.do_use_objstms = 1;
  return opts;
}

static void strip_metadata(fz_context *ctx, pdf_document *doc) {
  pdf_obj *trailer = pdf_trailer(ctx, doc);
  pdf_dict_del(ctx, trailer, PDF_NAME(Info));
  pdf_obj *root = pdf_dict_get(ctx, trailer, PDF_NAME(Root));
  if (root) pdf_dict_del(ctx, root, PDF_NAME(Metadata));
}

int smaller_pdf_info(const char *path, int *pages, int *needs_password, char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  fz_document *doc = NULL;
  fz_var(doc);
  fz_try(ctx) {
    doc = fz_open_document(ctx, path);
    *needs_password = fz_needs_password(ctx, doc);
    *pages = *needs_password ? 0 : fz_count_pages(ctx, doc);
  }
  fz_always(ctx) { fz_drop_document(ctx, doc); }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}

int smaller_pdf_page_size(const char *path, int page_index, float *width_pt, float *height_pt,
                          char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  fz_document *doc = NULL;
  fz_page *page = NULL;
  fz_var(doc);
  fz_var(page);
  fz_try(ctx) {
    doc = fz_open_document(ctx, path);
    page = fz_load_page(ctx, doc, page_index);
    fz_rect r = fz_bound_page(ctx, page);
    *width_pt = r.x1 - r.x0;
    *height_pt = r.y1 - r.y0;
  }
  fz_always(ctx) {
    fz_drop_page(ctx, page);
    fz_drop_document(ctx, doc);
  }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}

static void set_lossy(int *method, int *threshold, int *to, int *recompress, char **quality, int dpi, char *q) {
  *method = FZ_SUBSAMPLE_BICUBIC;
  *threshold = dpi > 0 ? dpi + dpi / 2 : 0;  // 超过目标 1.5 倍才降采样，避免反复重采样
  *to = dpi;
  *recompress = FZ_RECOMPRESS_JPEG;
  *quality = q;
}

int smaller_pdf_compress(const char *input, const char *output, int rewrite_images, int color_dpi,
                         int gray_dpi, int bitonal_dpi, int jpeg_quality, int subset_fonts,
                         int strip_meta, int grayscale, char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  pdf_document *doc = NULL;
  fz_var(doc);
  char quality[8];
  snprintf(quality, sizeof quality, "%d", jpeg_quality);
  fz_try(ctx) {
    doc = pdf_open_document(ctx, input);
    if (pdf_needs_password(ctx, doc)) fz_throw(ctx, FZ_ERROR_ARGUMENT, "文件有打开密码");

    if (grayscale) {
      // 整页（文字、矢量、图片）转灰度，放在图片重压之前，灰度图按灰度档压。
      pdf_recolor_options ro = {1};
      int n = pdf_count_pages(ctx, doc);
      for (int i = 0; i < n; ++i) pdf_recolor_page(ctx, doc, i, &ro);
    }

    pdf_image_rewriter_options io;
    memset(&io, 0, sizeof io);
    // 彩色与灰度：原本有损和无损的图都重压为 JPEG；只在新图更小时替换。
    set_lossy(&io.color_lossy_image_subsample_method, &io.color_lossy_image_subsample_threshold,
              &io.color_lossy_image_subsample_to, &io.color_lossy_image_recompress_method,
              &io.color_lossy_image_recompress_quality, color_dpi, quality);
    set_lossy(&io.color_lossless_image_subsample_method, &io.color_lossless_image_subsample_threshold,
              &io.color_lossless_image_subsample_to, &io.color_lossless_image_recompress_method,
              &io.color_lossless_image_recompress_quality, color_dpi, quality);
    set_lossy(&io.gray_lossy_image_subsample_method, &io.gray_lossy_image_subsample_threshold,
              &io.gray_lossy_image_subsample_to, &io.gray_lossy_image_recompress_method,
              &io.gray_lossy_image_recompress_quality, gray_dpi, quality);
    set_lossy(&io.gray_lossless_image_subsample_method, &io.gray_lossless_image_subsample_threshold,
              &io.gray_lossless_image_subsample_to, &io.gray_lossless_image_recompress_method,
              &io.gray_lossless_image_recompress_quality, gray_dpi, quality);
    // 黑白图（扫描件文字）：降采样后用无损的 CCITT G4。
    io.bitonal_image_subsample_method = FZ_SUBSAMPLE_AVERAGE;
    io.bitonal_image_subsample_threshold = bitonal_dpi > 0 ? bitonal_dpi + bitonal_dpi / 2 : 0;
    io.bitonal_image_subsample_to = bitonal_dpi;
    io.bitonal_image_recompress_method = FZ_RECOMPRESS_FAX;
    io.recompress_when = FZ_RECOMPRESS_WHEN_SMALLER;
    if (rewrite_images) pdf_rewrite_images(ctx, doc, &io);

    if (subset_fonts) pdf_subset_fonts(ctx, doc, 0, NULL);
    if (strip_meta) strip_metadata(ctx, doc);
    pdf_write_options wo = compact_write_options();
    pdf_save_document(ctx, doc, output, &wo);
  }
  fz_always(ctx) { pdf_drop_document(ctx, doc); }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}

int smaller_pdf_assemble(const char **inputs, int input_count, const int *doc_index,
                         const int *page_index, int page_count, const char *output, int strip_meta,
                         char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  pdf_document *dst = NULL;
  pdf_document **docs = calloc((size_t)input_count, sizeof *docs);
  pdf_graft_map **maps = calloc((size_t)input_count, sizeof *maps);
  fz_var(dst);
  fz_try(ctx) {
    if (docs == NULL || maps == NULL) fz_throw(ctx, FZ_ERROR_SYSTEM, "内存不足");
    dst = pdf_create_document(ctx);
    for (int i = 0; i < input_count; ++i) {
      docs[i] = pdf_open_document(ctx, inputs[i]);
      if (pdf_needs_password(ctx, docs[i])) fz_throw(ctx, FZ_ERROR_ARGUMENT, "%s 有打开密码", inputs[i]);
      maps[i] = pdf_new_graft_map(ctx, dst);
    }
    for (int k = 0; k < page_count; ++k) {
      int d = doc_index[k];
      if (d < 0 || d >= input_count) fz_throw(ctx, FZ_ERROR_ARGUMENT, "文件序号越界");
      if (page_index[k] < 0 || page_index[k] >= pdf_count_pages(ctx, docs[d]))
        fz_throw(ctx, FZ_ERROR_ARGUMENT, "页码越界");
      pdf_graft_mapped_page(ctx, maps[d], -1, docs[d], page_index[k]);
    }
    if (strip_meta) strip_metadata(ctx, dst);
    pdf_write_options wo = compact_write_options();
    pdf_save_document(ctx, dst, output, &wo);
  }
  fz_always(ctx) {
    for (int i = 0; i < input_count && maps && docs; ++i) {
      pdf_drop_graft_map(ctx, maps[i]);
      pdf_drop_document(ctx, docs[i]);
    }
    pdf_drop_document(ctx, dst);
  }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  free(docs);
  free(maps);
  fz_drop_context(ctx);
  return rc;
}

int smaller_pdf_render(const char *path, int page_index, int target_width, uint8_t **rgba,
                       int *width, int *height, char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  fz_document *doc = NULL;
  fz_page *page = NULL;
  fz_pixmap *pix = NULL;
  fz_var(doc);
  fz_var(page);
  fz_var(pix);
  fz_try(ctx) {
    doc = fz_open_document(ctx, path);
    page = fz_load_page(ctx, doc, page_index);
    fz_rect r = fz_bound_page(ctx, page);
    float scale = (float)target_width / (r.x1 - r.x0);
    // 不带透明通道渲染时 MuPDF 先铺白底，正好对应纸面。
    pix = fz_new_pixmap_from_page(ctx, page, fz_scale(scale, scale), fz_device_rgb(ctx), 0);
    int w = fz_pixmap_width(ctx, pix), h = fz_pixmap_height(ctx, pix);
    int stride = fz_pixmap_stride(ctx, pix);
    unsigned char *src = fz_pixmap_samples(ctx, pix);
    uint8_t *out = malloc((size_t)w * h * 4);
    if (out == NULL) fz_throw(ctx, FZ_ERROR_SYSTEM, "内存不足");
    for (int y = 0; y < h; ++y) {
      const unsigned char *row = src + (size_t)y * stride;
      uint8_t *dst = out + (size_t)y * w * 4;
      for (int x = 0; x < w; ++x) {
        dst[x * 4 + 0] = row[x * 3 + 0];
        dst[x * 4 + 1] = row[x * 3 + 1];
        dst[x * 4 + 2] = row[x * 3 + 2];
        dst[x * 4 + 3] = 255;
      }
    }
    *rgba = out;
    *width = w;
    *height = h;
  }
  fz_always(ctx) {
    fz_drop_pixmap(ctx, pix);
    fz_drop_page(ctx, page);
    fz_drop_document(ctx, doc);
  }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}

void smaller_pdf_free_pixels(uint8_t *rgba) { free(rgba); }

int smaller_pdf_from_images(const uint8_t **images, const size_t *lengths, int count, int page_mode,
                            float dpi, const char *output, char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  pdf_document *doc = NULL;
  fz_var(doc);
  fz_try(ctx) {
    doc = pdf_create_document(ctx);
    for (int i = 0; i < count; ++i) {
      fz_buffer *buf = NULL;
      fz_image *image = NULL;
      pdf_obj *resources = NULL;
      fz_buffer *contents = NULL;
      pdf_obj *page = NULL;
      fz_var(buf);
      fz_var(image);
      fz_var(resources);
      fz_var(contents);
      fz_var(page);
      fz_try(ctx) {
        buf = fz_new_buffer_from_copied_data(ctx, images[i], lengths[i]);
        // JPEG / PNG 的压缩数据原样嵌入，不重新编码。
        image = fz_new_image_from_buffer(ctx, buf);
        float iw = image->w * 72.0f / dpi, ih = image->h * 72.0f / dpi;
        float pw = iw, ph = ih, x = 0, y = 0, w = iw, h = ih;
        if (page_mode == 1) {
          // A4 竖版，留 0 边距等比放进页面并居中。
          pw = 595.276f;
          ph = 841.89f;
          float s = fminf(pw / iw, ph / ih);
          w = iw * s;
          h = ih * s;
          x = (pw - w) / 2;
          y = (ph - h) / 2;
        }
        resources = pdf_new_dict(ctx, doc, 1);
        pdf_obj *xobjects = pdf_dict_put_dict(ctx, resources, PDF_NAME(XObject), 1);
        pdf_dict_puts_drop(ctx, xobjects, "Im0", pdf_add_image(ctx, doc, image));
        contents = fz_new_buffer(ctx, 64);
        fz_append_printf(ctx, contents, "q %g 0 0 %g %g %g cm /Im0 Do Q", w, h, x, y);
        page = pdf_add_page(ctx, doc, fz_make_rect(0, 0, pw, ph), 0, resources, contents);
        pdf_insert_page(ctx, doc, -1, page);
      }
      fz_always(ctx) {
        pdf_drop_obj(ctx, page);
        fz_drop_buffer(ctx, contents);
        pdf_drop_obj(ctx, resources);
        fz_drop_image(ctx, image);
        fz_drop_buffer(ctx, buf);
      }
      fz_catch(ctx) { fz_rethrow(ctx); }
    }
    pdf_write_options wo = compact_write_options();
    pdf_save_document(ctx, doc, output, &wo);
  }
  fz_always(ctx) { pdf_drop_document(ctx, doc); }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}

// 用密码打开加密的 PDF，另存一份不加密的副本（只在本机临时目录里用，处理完即删）。
// 密码不对时返回 -2。
int smaller_pdf_unlock(const char *input, const char *output, const char *password, char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  pdf_document *doc = NULL;
  fz_var(doc);
  fz_try(ctx) {
    doc = pdf_open_document(ctx, input);
    if (pdf_needs_password(ctx, doc) && !pdf_authenticate_password(ctx, doc, password)) {
      rc = -2;
      snprintf(err, err_len, "密码不对");
    } else {
      pdf_write_options wo = pdf_default_write_options;
      wo.do_encrypt = PDF_ENCRYPT_NONE;
      pdf_save_document(ctx, doc, output, &wo);
    }
  }
  fz_always(ctx) { pdf_drop_document(ctx, doc); }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}

// 给 PDF 加打开密码（AES-256），另存到 output。
int smaller_pdf_encrypt(const char *input, const char *output, const char *password, char *err, size_t err_len) {
  fz_context *ctx = new_context();
  if (ctx == NULL) {
    set_error(NULL, err, err_len);
    return -1;
  }
  int rc = 0;
  pdf_document *doc = NULL;
  fz_var(doc);
  fz_try(ctx) {
    doc = pdf_open_document(ctx, input);
    pdf_write_options wo = compact_write_options();
    wo.do_encrypt = PDF_ENCRYPT_AES_256;
    wo.permissions = ~0;
    fz_strlcpy(wo.upwd_utf8, password, sizeof wo.upwd_utf8);
    fz_strlcpy(wo.opwd_utf8, password, sizeof wo.opwd_utf8);
    pdf_save_document(ctx, doc, output, &wo);
  }
  fz_always(ctx) { pdf_drop_document(ctx, doc); }
  fz_catch(ctx) {
    set_error(ctx, err, err_len);
    rc = -1;
  }
  fz_drop_context(ctx);
  return rc;
}
