namespace SimpleSmaller;

internal sealed partial class Workspace
{
    /// 现在能不能开始（PDF 加密码没填、合并没有页面时不能）。
    public bool CanStart => BlockReason == null;

    public string? BlockReason
    {
        get
        {
            if (Type == MediaType.Pdf && PdfSegment != PdfSegment.ToImages && OutputPasswordOn && string.IsNullOrWhiteSpace(OutputPassword))
                return "先填打开密码";
            if (Type == MediaType.Pdf && PdfSegment == PdfSegment.FromImages)
                return Files.Any(f => f.Type == MediaType.Image) ? null : "先把图片拖进来";
            if (Type == MediaType.Pdf && PdfSegment == PdfSegment.Merge)
                return Files.Any(f => f.Type == MediaType.Pdf) && PageOrder.Count > 0 ? null : "先把 PDF 拖进来";
            return null;
        }
    }

    /// 「开始」下面写这次会做什么，只写确定的信息，不写预估体积。
    public string StartSummary
    {
        get
        {
            var n = Files.Count;
            switch (Type)
            {
                case MediaType.Image:
                {
                    if (IsCropping) return $"{CropItems.Count} 张 · {CropTitle} · 宽 {CropOutputWidth}";
                    var b = Draft.Image;
                    var parts = new List<string> { $"{n} 个文件", b.Format.Title() };
                    parts.Add(b.Size is { } s ? $"{b.SizeAxis.Title()} {s}" : "原尺寸");
                    return string.Join(" · ", parts);
                }
                case MediaType.Video:
                {
                    if (VideoSegment == VideoSegment.Frames) return $"生成 {VideoItems.Sum(FrameCount)} 张";
                    if (VideoSegment == VideoSegment.Live)
                    {
                        var kind = Draft.Video.LiveKind switch { LiveKind.Apple => "苹果实况", LiveKind.Android => "安卓动态照片", _ => "两种都要" };
                        return $"{n} 个视频 · {kind}";
                    }
                    var v = Draft.Video;
                    if (v.MuteOnly) return $"{n} 个视频 · 只去声音";
                    if (v.Codec == VideoCodec.Gif) return $"{n} 个视频 · GIF";
                    return $"{n} 个视频 · {(v.ShortEdge is { } e ? $"{e}p" : "原分辨率")}";
                }
                default:
                    return PdfSegment switch
                    {
                        PdfSegment.Compress => $"{n} 个文件 · {Base.Name}",
                        PdfSegment.Merge => $"合并成 1 个 · {PageOrder.Count} 页",
                        PdfSegment.Split => $"{n} 个文件 · 每 {Draft.Pdf.SplitEvery} 页一份",
                        PdfSegment.ToImages => Draft.Pdf.LongImage ? $"{n} 个文件 · 拼成长图" : $"{n} 个文件 · 每页一张",
                        _ => $"{Files.Count(f => f.Type == MediaType.Image)} 张图 · 做成 1 个 PDF",
                    };
            }
        }
    }
}
