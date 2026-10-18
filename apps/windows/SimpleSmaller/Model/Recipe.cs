using System.Text.Json.Serialization;
using uniffi.smaller_core;

namespace SimpleSmaller;

// MARK: - 文件类型

internal enum MediaType { Image, Video, Pdf }

internal static class MediaTypes
{
    public static readonly MediaType[] All = [MediaType.Image, MediaType.Video, MediaType.Pdf];

    public static string Title(this MediaType t) => t switch
    {
        MediaType.Image => "图片",
        MediaType.Video => "视频",
        _ => "PDF",
    };

    public static Kind EngineKind(this MediaType t) => t switch
    {
        MediaType.Image => uniffi.smaller_core.Kind.Image,
        MediaType.Video => uniffi.smaller_core.Kind.Video,
        _ => uniffi.smaller_core.Kind.Pdf,
    };

    public static MediaType? Of(string path)
    {
        switch (Path.GetExtension(path).TrimStart('.').ToLowerInvariant())
        {
            // 动图（多帧 GIF、动画 WebP、APNG）按视频处理，和引擎同一规则：压成静态图会把动画弄丢。
            case "gif" or "webp" or "png":
                return IsAnimated(path) ? MediaType.Video : MediaType.Image;
            case "jpg" or "jpeg" or "avif" or "heic" or "heif" or "tif" or "tiff" or "bmp":
                return MediaType.Image;
            case "mp4" or "mov" or "m4v" or "mkv" or "webm" or "avi" or "mts" or "m2ts":
                return MediaType.Video;
            case "pdf":
                return MediaType.Pdf;
            default:
                return null;
        }
    }

    public static bool IsAnimated(string path) => ImageHeader.FrameCount(path) > 1;
}

// MARK: - 命名

internal sealed record NamingSettings
{
    public string Template { get; init; } = "{name}-small";
    public bool Lowercase { get; init; }
    public bool Pinyin { get; init; }
    public bool Hyphenate { get; init; }

    public Naming Ffi(string date) => new(Template, Lowercase, Pinyin, Hyphenate, date);
}

// MARK: - 比例

internal sealed record CropPreset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// 比例份数；0 表示自由比例。
    public int RatioW { get; init; }
    public int RatioH { get; init; }
    public string RatioLabel { get; init; } = "";
    /// 推荐输出宽度。
    public int Width { get; init; }

    [JsonIgnore]
    public double? Aspect => RatioW > 0 && RatioH > 0 ? (double)RatioW / RatioH : null;

    private static CropPreset P(string id, string name, int w, int h, string label, int width) =>
        new() { Id = id, Name = name, RatioW = w, RatioH = h, RatioLabel = label, Width = width };

    /// 图片裁切的平台比例（设计稿 03）。
    public static readonly IReadOnlyList<CropPreset> ImagePresets =
    [
        P("xhs", "小红书", 3, 4, "3:4", 1242),
        P("dy", "抖音 · 视频号", 9, 16, "9:16", 1080),
        P("gzh", "公众号封面", 47, 20, "2.35:1", 900),
        P("gzh2", "公众号次图", 1, 1, "1:1", 500),
        P("shop", "电商主图", 1, 1, "1:1", 800),
        P("ig", "Instagram", 4, 5, "4:5", 1080),
        P("banner", "网站横幅", 16, 9, "16:9", 1920),
        P("free", "自由", 0, 0, "", 1920),
    ];

    /// 视频画面比例（设计稿 04）。
    public static readonly IReadOnlyList<CropPreset> VideoPresets =
    [
        P("v916", "9:16", 9, 16, "9:16", 1080),
        P("v34", "3:4", 3, 4, "3:4", 1080),
        P("v11", "1:1", 1, 1, "1:1", 1080),
        P("v45", "4:5", 4, 5, "4:5", 1080),
        P("v169", "16:9", 16, 9, "16:9", 1920),
    ];
}

// MARK: - 图片参数

internal enum ImageFormatChoice { Webp, Avif, Jpeg, Png, Original }

internal enum SizeAxis { Width, Height, LongEdge }

internal static class ImageChoices
{
    public static string Title(this ImageFormatChoice f) => f switch
    {
        ImageFormatChoice.Webp => "WebP",
        ImageFormatChoice.Avif => "AVIF",
        ImageFormatChoice.Jpeg => "JPG",
        ImageFormatChoice.Png => "PNG",
        _ => "原格式",
    };

    public static ImageFormat Ffi(this ImageFormatChoice f) => f switch
    {
        ImageFormatChoice.Webp => ImageFormat.Webp,
        ImageFormatChoice.Avif => ImageFormat.Avif,
        ImageFormatChoice.Jpeg => ImageFormat.Jpeg,
        ImageFormatChoice.Png => ImageFormat.Png,
        _ => ImageFormat.Original,
    };

    public static string Title(this SizeAxis a) => a switch
    {
        SizeAxis.Width => "宽",
        SizeAxis.Height => "高",
        _ => "长边",
    };

    public static SizeBy Ffi(this SizeAxis a) => a switch
    {
        SizeAxis.Width => SizeBy.Width,
        SizeAxis.Height => SizeBy.Height,
        _ => SizeBy.LongEdge,
    };
}

internal sealed record ImageBody
{
    public ImageFormatChoice Format { get; init; } = ImageFormatChoice.Webp;
    public int Quality { get; init; } = 78;
    public bool Lossless { get; init; }
    /// 限体积（KB）。
    public int? TargetKB { get; init; }
    public SizeAxis SizeAxis { get; init; } = SizeAxis.Width;
    /// null 表示原尺寸。
    public int? Size { get; init; } = 2560;
    /// 「同时出 1280 / 640」这类附加档位。
    public IReadOnlyList<int> ExtraSizes { get; init; } = [];
    public CropPreset? Crop { get; init; }
    /// 裁切时的输出宽度（可改，默认取预设的推荐宽度）。
    public int? CropWidth { get; init; }

    public ImageBody() { }

    /// 读 JSON 用：可空但默认不为空的字段，文件里没写就是 null（与 mac 版 Codable 一致），不能落回默认值。
    [JsonConstructor]
    public ImageBody(int? size) => Size = size;

    // 列表按内容比较，草稿与原配方才能判断「已修改」。
    public bool Equals(ImageBody? o) =>
        o is not null && Format == o.Format && Quality == o.Quality && Lossless == o.Lossless && TargetKB == o.TargetKB
        && SizeAxis == o.SizeAxis && Size == o.Size && ExtraSizes.SequenceEqual(o.ExtraSizes) && Crop == o.Crop && CropWidth == o.CropWidth;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Format); h.Add(Quality); h.Add(Lossless); h.Add(TargetKB); h.Add(SizeAxis); h.Add(Size);
        foreach (var s in ExtraSizes) h.Add(s);
        h.Add(Crop); h.Add(CropWidth);
        return h.ToHashCode();
    }
}

// MARK: - 视频参数

internal enum VideoCodec { H264, Hevc, Gif }
internal enum VideoRateChoice { High, Medium, Low, Size }
internal enum FrameMode { PerSecond, Every, Total, All }
/// 实况做成哪种：苹果实况（照片 + 视频一对）、安卓动态照片（一张会动的 JPEG）、两种都要。
internal enum LiveKind { Apple, Android, Both }

internal sealed record VideoBody
{
    public VideoCodec Codec { get; init; } = VideoCodec.H264;
    public VideoRateChoice Rate { get; init; } = VideoRateChoice.Medium;
    public double TargetMB { get; init; } = 25;
    /// 短边上限：1080 / 720 / 540；null 为原分辨率。
    public int? ShortEdge { get; init; } = 1080;
    public bool KeepAudio { get; init; } = true;
    public bool HdrToSdr { get; init; } = true;
    /// 速度优先（系统硬件编码）。
    public bool Fast { get; init; }
    public bool ExportCover { get; init; }
    public CropPreset? Crop { get; init; }
    public double GifFps { get; init; } = 15;
    public int GifWidth { get; init; } = 480;
    /// 只去声音，不重新编码。
    public bool MuteOnly { get; init; }
    /// 输出序列帧（选中这个配方时切到「序列帧」）。
    public bool OutputFrames { get; init; }
    public FrameMode FrameMode { get; init; } = FrameMode.PerSecond;
    public double FrameValue { get; init; } = 15;
    public ImageFormatChoice FrameFormat { get; init; } = ImageFormatChoice.Webp;
    public int FrameQuality { get; init; } = 80;
    public int? FrameWidth { get; init; } = 1920;
    public bool FrameZip { get; init; }
    public bool FrameContactSheet { get; init; }
    /// 输出实况（选中这个配方时切到「实况」）。
    public bool OutputLive { get; init; }
    /// 做成苹果实况（照片 + 视频一对）、安卓动态照片（一张会动的 JPEG），可以都要。
    public bool LiveApple { get; init; } = true;
    public bool LiveAndroid { get; init; }
    /// 实况视频的最长边（0 为原分辨率）。
    public int LiveLongEdge { get; init; } = 1920;

    [JsonIgnore]
    public LiveKind LiveKind => LiveApple && LiveAndroid ? LiveKind.Both : LiveAndroid ? LiveKind.Android : LiveKind.Apple;

    public VideoBody WithLiveKind(LiveKind k) => this with { LiveApple = k != LiveKind.Android, LiveAndroid = k != LiveKind.Apple };

    public VideoBody() { }

    /// 读 JSON 用：同 ImageBody，没写的可空字段是 null。
    [JsonConstructor]
    public VideoBody(int? shortEdge, int? frameWidth)
    {
        ShortEdge = shortEdge;
        FrameWidth = frameWidth;
    }
}

// MARK: - PDF 参数

internal enum PdfLevelChoice { Screen, Email, Print, Size }
internal enum PdfPageModeChoice { Fit, A4 }

internal sealed record PdfBody
{
    public PdfLevelChoice Level { get; init; } = PdfLevelChoice.Email;
    public double TargetMB { get; init; } = 5;
    public bool SubsetFonts { get; init; } = true;
    public bool Grayscale { get; init; }
    public int SplitEvery { get; init; } = 1;
    public int ImageWidth { get; init; } = 1600;
    public ImageFormatChoice ImageFormat { get; init; } = ImageFormatChoice.Jpeg;
    public int ImageQuality { get; init; } = 85;
    public bool LongImage { get; init; }
    public PdfPageModeChoice PageMode { get; init; } = PdfPageModeChoice.A4;
}

// MARK: - 配方

/// 值语义：一律用 `with` 改出新副本，草稿与原配方互不影响，`draft != base` 即「已修改」。
internal sealed record Recipe
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public MediaType Type { get; init; }
    public bool BuiltIn { get; init; }
    public ImageBody Image { get; init; } = new();
    public VideoBody Video { get; init; } = new();
    public PdfBody Pdf { get; init; } = new();
    public NamingSettings Naming { get; init; } = new();
    public bool StripMetadata { get; init; } = true;
    /// 配方摘要按设计稿写死的，优先于自动生成。
    public string? SummaryOverride { get; init; }

    /// 配方的一句摘要（配方胶囊的悬停提示、圆环外圈、设置里的配方表）。
    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (SummaryOverride is { } s) return s;
            switch (Type)
            {
                case MediaType.Image:
                    if (Image.Crop is { Aspect: { } a } c)
                    {
                        var w = Image.CropWidth ?? c.Width;
                        return $"{w}×{(int)Num.Round(w / a)}";
                    }
                    if (Image.TargetKB != null) return "按体积";
                    if (Image.Format == ImageFormatChoice.Original) return "原格式";
                    return Image.Format.Title();
                case MediaType.Video:
                    if (Video.MuteOnly) return "不重编码";
                    if (Video.OutputLive)
                        return Video.LiveKind switch { LiveKind.Apple => "照片 + 视频", LiveKind.Android => "一张 JPG", _ => "苹果 + 安卓" };
                    if (Video.OutputFrames) return Video.FrameFormat.Title();
                    if (Video.Codec == VideoCodec.Gif) return $"{Video.GifWidth} · {(int)Video.GifFps}fps";
                    if (Video.Crop is { } vc) return $"{vc.Width}×{(int)Num.Round(vc.Width / (vc.Aspect ?? 1))}";
                    if (!Video.KeepAudio) return $"静音 {(Video.ShortEdge is { } e ? $"{e}p" : "")}";
                    return Video.Codec == VideoCodec.Hevc ? "HEVC" : "H.264";
                default:
                    if (Pdf.LongImage) return Pdf.ImageFormat.Title();
                    return Pdf.Level switch
                    {
                        PdfLevelChoice.Screen => "110 dpi",
                        PdfLevelChoice.Email => "≤ 10 MB",
                        PdfLevelChoice.Print => "300 dpi",
                        _ => $"≤ {(int)Pdf.TargetMB} MB",
                    };
            }
        }
    }

    /// 内置配方（顶部配方条里排在前面的那些）。
    public static readonly IReadOnlyList<Recipe> BuiltIns = MakeBuiltIns();

    private static List<Recipe> MakeBuiltIns()
    {
        var list = new List<Recipe>();
        void Img(string id, string name, Func<ImageBody, ImageBody> edit) =>
            list.Add(new Recipe { Id = id, Name = name, Type = MediaType.Image, BuiltIn = true, Image = edit(new ImageBody()) });
        var ip = CropPreset.ImagePresets;
        Img("img.web", "网站用", b => b with { Format = ImageFormatChoice.Webp, Quality = 78, Size = 2560 });
        Img("img.chat", "发群预览", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 82, Size = 2048 });
        Img("img.xhs", "小红书 3:4", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 90, Crop = ip[0] });
        Img("img.gzh", "公众号封面", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 90, Crop = ip[2] });
        list[^1] = list[^1] with { SummaryOverride = "2.35:1" };
        Img("img.dy", "抖音 9:16", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 90, Crop = ip[1] });
        Img("img.shop", "商品方图 1:1", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 90, Crop = ip[4] });
        Img("img.ig", "Instagram 4:5", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 90, Crop = ip[5] });
        Img("img.500k", "限 500 KB", b => b with { Format = ImageFormatChoice.Original, Quality = 90, TargetKB = 500, Size = null });
        Img("img.lossless", "无损交付", b => b with { Format = ImageFormatChoice.Original, Lossless = true, Quality = 100, Size = null });

        void Vid(string id, string name, Func<VideoBody, VideoBody> edit) =>
            list.Add(new Recipe { Id = id, Name = name, Type = MediaType.Video, BuiltIn = true, Video = edit(new VideoBody()) });
        var vp = CropPreset.VideoPresets;
        Vid("vid.chat", "发群 25 MB", v => v with { Rate = VideoRateChoice.Size, TargetMB = 25, ShortEdge = 1080 });
        Vid("vid.web", "网站背景", v => v with { KeepAudio = false, ShortEdge = 720, Rate = VideoRateChoice.Medium });
        Vid("vid.dy", "抖音 9:16", v => v with { Crop = vp[0], Rate = VideoRateChoice.High, ShortEdge = 1080 });
        Vid("vid.xhs", "小红书 3:4", v => v with { Crop = vp[1], Rate = VideoRateChoice.High, ShortEdge = 1080 });
        Vid("vid.gif", "转 GIF", v => v with { Codec = VideoCodec.Gif, GifWidth = 480, GifFps = 15 });
        Vid("vid.mute", "只去声音", v => v with { MuteOnly = true, KeepAudio = false });
        Vid("vid.frames.web", "网页序列帧", v => v with { OutputFrames = true, FrameMode = FrameMode.PerSecond, FrameValue = 15, FrameFormat = ImageFormatChoice.Webp, FrameWidth = 1920 });
        Vid("vid.frames.1s", "每秒一张截图", v => v with { OutputFrames = true, FrameMode = FrameMode.PerSecond, FrameValue = 1, FrameFormat = ImageFormatChoice.Jpeg, FrameWidth = null });
        Vid("vid.live.apple", "苹果实况", v => v with { OutputLive = true, LiveApple = true, LiveAndroid = false, Rate = VideoRateChoice.High });
        Vid("vid.live.android", "安卓动态照片", v => v with { OutputLive = true, LiveApple = false, LiveAndroid = true, Rate = VideoRateChoice.High });
        Vid("vid.live.both", "两种都要", v => v with { OutputLive = true, LiveApple = true, LiveAndroid = true, Rate = VideoRateChoice.High });

        void Pdf(string id, string name, Func<PdfBody, PdfBody> edit) =>
            list.Add(new Recipe { Id = id, Name = name, Type = MediaType.Pdf, BuiltIn = true, Pdf = edit(new PdfBody()) });
        Pdf("pdf.email", "邮件附件", p => p with { Level = PdfLevelChoice.Email });
        Pdf("pdf.screen", "屏幕阅读", p => p with { Level = PdfLevelChoice.Screen });
        Pdf("pdf.print", "打印保真", p => p with { Level = PdfLevelChoice.Print });
        Pdf("pdf.long", "转长图", p => p with { LongImage = true, ImageFormat = ImageFormatChoice.Jpeg, ImageWidth = 1080 });

        return list;
    }

    public static readonly IReadOnlyList<Recipe> StarterMine =
    [
        new Recipe
        {
            Id = "mine.srcset", Name = "官网三档 srcset", Type = MediaType.Image,
            Image = new ImageBody { Format = ImageFormatChoice.Webp, Size = 2560, ExtraSizes = [1280, 640] },
            Naming = new NamingSettings { Template = "{name}-{w}", Lowercase = true, Pinyin = true, Hyphenate = true },
        },
    ];
}

/// Swift 的 `.rounded()` 是四舍五入（远离零），C# 默认是银行家舍入，统一走这里。
internal static class Num
{
    public static double Round(double v) => Math.Round(v, MidpointRounding.AwayFromZero);
}
