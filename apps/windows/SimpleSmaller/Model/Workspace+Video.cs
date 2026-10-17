namespace SimpleSmaller;

/// 视频剪辑与序列帧：当前这段、比例裁切框（原画面像素）、截取范围、输出尺寸。
internal sealed partial class Workspace
{
    /// 已读出时长的视频。
    public IReadOnlyList<FileItem> VideoItems => Files.Where(f => f.Type == MediaType.Video && f.Duration > 0 && f.Width > 0).ToList();

    public FileItem? CurrentVideo
    {
        get
        {
            var items = VideoItems;
            if (items.Count == 0) return null;
            return items[Math.Min(Math.Max(0, VideoIndex), items.Count - 1)];
        }
    }

    public double? VideoAspect => Draft.Video.Crop?.Aspect;

    /// 按比例居中取最大的框；宽高取偶数（编码要求）。没设比例时为 null（整幅）。
    public PixelRect? DefaultVideoCrop(FileItem item)
    {
        if (VideoAspect is not { } a || item.Width <= 0 || item.Height <= 0) return null;
        return PixelRect.CenteredMax(item.Width, item.Height, a, PixelRect.Even);
    }

    /// 这一段实际用的框：拖过的 > 默认。
    public PixelRect? VideoCropBox(FileItem item)
    {
        if (VideoAspect == null) return null;
        return VideoCrop.TryGetValue(item.FilePath, out var r) ? r : DefaultVideoCrop(item);
    }

    public TimeRange TrimRange(FileItem item) =>
        Trim.TryGetValue(item.FilePath, out var r) ? r : new TimeRange(0, item.Duration);

    /// 输出画面尺寸：裁切后按短边上限缩小（不放大），取偶数。
    public PixelSize VideoOutputSize(FileItem item)
    {
        var size = VideoCropBox(item)?.Size ?? new PixelSize(item.Width, item.Height);
        if (Draft.Video.Codec == VideoCodec.Gif)
        {
            double w = Draft.Video.GifWidth;
            if (w < size.Width) size = new PixelSize(w, Num.Round(size.Height * w / size.Width));
            return size;
        }
        if (Draft.Video.ShortEdge is { } edge && edge < Math.Min(size.Width, size.Height))
        {
            var s = edge / Math.Min(size.Width, size.Height);
            size = new PixelSize(size.Width * s, size.Height * s);
        }
        static double Even(double v) => Num.Round(v / 2) * 2;
        return new PixelSize(Even(size.Width), Even(size.Height));
    }

    /// 换比例后各段的框回到默认。
    public void ResetVideoCrop() => VideoCrop = Empty<PixelRect>();

    /// 序列帧会生成多少张（每段按截取范围算）。
    public int FrameCount(FileItem item)
    {
        var r = TrimRange(item);
        var length = Math.Max(0, r.End - r.Start);
        var v = Draft.Video;
        return v.FrameMode switch
        {
            FrameMode.PerSecond => Math.Max(1, (int)Math.Floor(length * v.FrameValue)),
            FrameMode.Every => Math.Max(1, (int)Math.Floor(length / Math.Max(0.01, v.FrameValue))),
            FrameMode.Total => Math.Max(1, (int)v.FrameValue),
            _ => Math.Max(1, (int)Num.Round(length * item.Fps)),
        };
    }

    /// 第 k 张取自第几秒（与引擎按时间均匀取帧一致，用于预览）。
    public double FrameTime(FileItem item, int k)
    {
        var r = TrimRange(item);
        var length = Math.Max(0, r.End - r.Start);
        var n = FrameCount(item);
        var v = Draft.Video;
        return v.FrameMode switch
        {
            FrameMode.PerSecond => r.Start + k / Math.Max(0.01, v.FrameValue),
            FrameMode.Every => r.Start + k * v.FrameValue,
            FrameMode.Total => r.Start + length * k / Math.Max(1, n),
            _ => r.Start + k / Math.Max(1, item.Fps),
        };
    }
}
