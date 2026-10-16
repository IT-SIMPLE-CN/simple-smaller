// MuPDF 外壳接口。所有函数出错返回负数，并把可读的错误信息写进 err。
#ifndef SMALLER_PDF_SHIM_H
#define SMALLER_PDF_SHIM_H

#include <stddef.h>
#include <stdint.h>

int smaller_pdf_info(const char *path, int *pages, int *needs_password, char *err, size_t err_len);
int smaller_pdf_page_size(const char *path, int page_index, float *width_pt, float *height_pt,
                          char *err, size_t err_len);
// rewrite_images 为 0 时只重新保存（去重、压缩对象流、子集化字体）；
// dpi 为 0 表示该类图片不降采样；jpeg_quality 1–100。
int smaller_pdf_compress(const char *input, const char *output, int rewrite_images, int color_dpi,
                         int gray_dpi, int bitonal_dpi, int jpeg_quality, int subset_fonts,
                         int strip_meta, int grayscale, char *err, size_t err_len);
// 按 (doc_index[k], page_index[k]) 的顺序挑页拼成新文件：合并、排序、删页、拆分共用。
int smaller_pdf_assemble(const char **inputs, int input_count, const int *doc_index,
                         const int *page_index, int page_count, const char *output, int strip_meta,
                         char *err, size_t err_len);
// 按目标宽度渲染一页为 RGBA8（白底），由 smaller_pdf_free_pixels 释放。
int smaller_pdf_render(const char *path, int page_index, int target_width, uint8_t **rgba,
                       int *width, int *height, char *err, size_t err_len);
void smaller_pdf_free_pixels(uint8_t *rgba);
// 图片转 PDF：每张图一页。page_mode 0 = 页面按图片尺寸（按 dpi 换算），1 = A4 竖版等比居中。
int smaller_pdf_from_images(const uint8_t **images, const size_t *lengths, int count, int page_mode,
                            float dpi, const char *output, char *err, size_t err_len);

#endif
