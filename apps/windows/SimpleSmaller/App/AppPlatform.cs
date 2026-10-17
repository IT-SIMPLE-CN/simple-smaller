using SimpleSmaller.Views.Common;

namespace SimpleSmaller;

/// 界面层对 IPlatform 的补充：前台判断、任务栏角标、缩略图。
internal sealed class AppPlatform : WindowsPlatform
{
    /// 缩略图网格的方块最大 220、按 2 倍屏算，统一出长边 440 的一张，列表、胶片条也用它。
    private const int ThumbPixels = 440;

    /// 图片自己解码（按 EXIF 转正，缩略图上的裁切框才对得上）；视频、PDF 用系统缩略图。
    public override async Task<object?> LoadThumbnailAsync(string path, int size)
    {
        var px = Math.Max(size, ThumbPixels);
        // 按扩展名分：不读文件头（MediaTypes.Of 判断动图要读文件，这里在界面线程）；动图取第一帧。
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var image = ext is "jpg" or "jpeg" or "png" or "gif" or "webp" or "avif" or "heic" or "heif" or "tif" or "tiff" or "bmp";
        return image ? await ImageLoader.LoadAsync(path, px) : await ImageLoader.ShellThumbnailAsync(path, px);
    }
}
