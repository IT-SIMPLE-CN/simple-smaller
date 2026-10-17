using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace SimpleSmaller.Views.Common;

/// 图片解码成屏幕用的尺寸：按 EXIF 转正（与引擎、ImageHeader 的像素坐标一致），长边不超过 maxPixel。
/// 解码在 WinRT 的后台线程做，只有最后生成 ImageSource 回到界面线程。在界面线程调用。
internal static class ImageLoader
{
    /// 同时解码的张数：一批几十张大图同时整张解码会把内存顶上去。
    private static readonly SemaphoreSlim Gate = new(3);

    public static async Task<ImageSource?> LoadAsync(string path, int maxPixel)
    {
        SoftwareBitmap? bitmap = null;
        await Gate.WaitAsync();
        try
        {
            bitmap = await Task.Run(async () =>
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using IRandomAccessStream stream = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                double w = decoder.PixelWidth, h = decoder.PixelHeight;
                var k = Math.Min(1.0, maxPixel / Math.Max(1.0, Math.Max(w, h)));
                // 先缩放再转正，等比缩放与转正的顺序无关。
                var transform = new BitmapTransform
                {
                    ScaledWidth = (uint)Math.Max(1, Math.Round(w * k)),
                    ScaledHeight = (uint)Math.Max(1, Math.Round(h * k)),
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };
                return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            });
        }
        catch
        {
            bitmap = null;
        }
        finally
        {
            Gate.Release();
        }
        if (bitmap is null)
        {
            // WIC 解不了的（没装 HEIF 扩展的 HEIC 等）退回系统缩略图。
            return await ShellThumbnailAsync(path, maxPixel);
        }
        // SoftwareBitmapSource 不复制像素：位图要一直活着（交给 GC），提前 Dispose 的话，
        // 换到另一个元素上显示（胶片条、大图）时 XAML 会读到已释放的位图直接崩溃。
        try
        {
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            return source;
        }
        catch
        {
            return null;
        }
    }

    /// 系统缩略图（视频、PDF、WIC 解不了的图片）；只有图标没有画面时返回 null。
    public static async Task<ImageSource?> ShellThumbnailAsync(string path, int size)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)Math.Clamp(size, 32, 1024), ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Type != ThumbnailType.Image)
            {
                return null;
            }
            var image = new BitmapImage();
            await image.SetSourceAsync(thumb);
            return image;
        }
        catch
        {
            return null;
        }
    }
}
