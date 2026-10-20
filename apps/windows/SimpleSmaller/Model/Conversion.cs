using System.Globalization;
using SimpleSmaller.Core;

namespace SimpleSmaller;

/// 配方 → 引擎参数。
internal static class Conversion
{
    private static byte Clamp8(int v) => (byte)Math.Clamp(v, 0, 255);

    public static ImageSettings ImageSettings(this Recipe recipe, IReadOnlyDictionary<string, PixelRect>? cropBoxes = null)
    {
        var image = recipe.Image;
        var sizes = new List<uint>();
        if (image.Size is { } s) sizes.Add((uint)s);
        sizes.AddRange(image.ExtraSizes.Select(x => (uint)x));
        CropSetting? crop = null;
        if (image.Crop is { } c)
        {
            var w = (uint)(image.CropWidth ?? c.Width);
            if (c.Aspect != null)
                crop = new CropSetting((uint)c.RatioW, (uint)c.RatioH, w, c.RatioLabel.Replace(":", "x"));
            else
                // 自由比例：按每张框的比例，没框的按原图比例。
                crop = new CropSetting(0, 0, w, null);
        }
        var boxes = (cropBoxes ?? new Dictionary<string, PixelRect>()).Select(kv =>
            new CropBox(kv.Key, (uint)Math.Max(0, kv.Value.MinX), (uint)Math.Max(0, kv.Value.MinY),
                        (uint)Math.Max(1, kv.Value.Width), (uint)Math.Max(1, kv.Value.Height))).ToArray();
        return new ImageSettings(
            image.Format.Ffi(),
            Clamp8(image.Quality),
            image.Lossless,
            image.TargetKB is { } kb ? (ulong)kb * 1000 : null,
            image.Crop == null ? sizes.ToArray() : [],
            image.SizeAxis.Ffi(),
            crop,
            boxes);
    }

    /// `coverAt`：导出封面、做实况时封面取原视频的第几秒。
    /// `long4K`：这段是 4K、3 分钟以上的长片，自动改用系统硬件编码（软件编码要几十分钟）。
    public static VideoSettings VideoSettings(this Recipe recipe, TimeRange? trim = null, PixelRect? crop = null, double? coverAt = null, bool long4K = false)
    {
        var video = recipe.Video;
        VideoRate rate = video.Rate switch
        {
            VideoRateChoice.High => new VideoRate.High(),
            VideoRateChoice.Medium => new VideoRate.Medium(),
            VideoRateChoice.Low => new VideoRate.Low(),
            _ => new VideoRate.TargetBytes((ulong)(video.TargetMB * 1_000_000)),
        };
        VideoOutput output;
        if (video.OutputLive)
        {
            // 封面没给时取 1 秒处（引擎会把片段放在封面周围、不超过 3 秒）。
            output = new VideoOutput.Live(video.LiveApple, video.LiveAndroid, coverAt ?? 1, (uint)Math.Max(0, video.LiveLongEdge));
        }
        else if (video.MuteOnly)
        {
            output = new VideoOutput.Mute();
        }
        else if (video.OutputFrames)
        {
            FramePick pick = video.FrameMode switch
            {
                FrameMode.PerSecond => new FramePick.PerSecond(video.FrameValue),
                FrameMode.Every => new FramePick.EverySeconds(video.FrameValue),
                FrameMode.Total => new FramePick.Total((uint)Math.Max(1, video.FrameValue)),
                _ => new FramePick.All(),
            };
            output = new VideoOutput.Frames(pick, video.FrameWidth is { } fw ? (uint)fw : null, video.FrameFormat.Ffi(),
                                            Clamp8(video.FrameQuality), video.FrameZip, video.FrameContactSheet);
        }
        else if (video.Codec == VideoCodec.Gif)
        {
            output = new VideoOutput.Gif(video.GifFps, (uint)video.GifWidth, 90);
        }
        else
        {
            output = new VideoOutput.Mp4();
        }
        var box = crop is { } r ? new VideoCropBox((uint)Math.Max(0, r.MinX), (uint)Math.Max(0, r.MinY), (uint)r.Width, (uint)r.Height) : null;
        return new VideoSettings(
            video.Codec == VideoCodec.Hevc ? VideoCodecChoice.Hevc : VideoCodecChoice.H264,
            // HEVC 只走硬件编码（不内置 x265）。
            (video.Fast || video.Codec == VideoCodec.Hevc || long4K) ? VideoEncoderChoice.Fast : VideoEncoderChoice.Quality,
            rate,
            video.ShortEdge is { } e ? (uint)e : null,
            video.KeepAudio,
            trim?.Start,
            trim?.End,
            box,
            video.HdrToSdr,
            null,
            output,
            video.ExportCover && !video.OutputLive ? (coverAt ?? 0) : null);
    }

    public static PdfLevel PdfLevel(this Recipe recipe) => recipe.Pdf.Level switch
    {
        PdfLevelChoice.Screen => SimpleSmaller.Core.PdfLevel.Screen,
        PdfLevelChoice.Email or PdfLevelChoice.Size => SimpleSmaller.Core.PdfLevel.Email,
        _ => SimpleSmaller.Core.PdfLevel.Print,
    };

    public static ulong? PdfTargetBytes(this Recipe recipe) => recipe.Pdf.Level switch
    {
        PdfLevelChoice.Size => (ulong)(recipe.Pdf.TargetMB * 1_000_000),
        PdfLevelChoice.Email => 10_000_000,
        _ => null,
    };
}

internal static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// 体积：不足 1 KB 写 B，其余用十进制 KB / MB / GB（与访达一致：KB 不带小数，MB 一位，GB 两位）。
    public static string Size(ulong bytes)
    {
        if (bytes == 0) return "—";
        if (bytes < 1000) return $"{bytes} B";
        double b = bytes;
        var kb = Num.Round(b / 1e3);
        if (kb < 1000) return $"{kb.ToString("0", Inv)} KB";
        var mb = Math.Round(b / 1e6, 1, MidpointRounding.AwayFromZero);
        if (mb < 1000) return $"{mb.ToString("0.#", Inv)} MB";
        var gb = Math.Round(b / 1e9, 2, MidpointRounding.AwayFromZero);
        if (gb < 1000) return $"{gb.ToString("0.##", Inv)} GB";
        return $"{Math.Round(b / 1e12, 2, MidpointRounding.AwayFromZero).ToString("0.##", Inv)} TB";
    }

    /// 变化百分比。省超过 99% 也写「−99%」，不写成「−100%」让人以为文件没了。
    public static string Change(ulong before, ulong after)
    {
        if (before == 0 || after == 0) return "—";
        var pct = ((double)after / before - 1) * 100;
        if (pct < 0) return $"−{Math.Min(99, (int)Num.Round(-pct))}%";
        return $"+{(int)Num.Round(pct)}%";
    }

    /// 「01:23」；不到 1 秒的（动图常见）写「00:00.3」，免得显示成 00:00。
    public static string Duration(double s)
    {
        if (s > 0 && s < 1) return $"00:00.{(int)Math.Floor(s * 10)}";
        var t = (int)Math.Floor(s);
        return $"{t / 60:00}:{t % 60:00}";
    }

    public static string Timecode(double s)
    {
        var t = Math.Max(0, s);
        return $"{(int)t / 60:00}:{(t % 60).ToString("00.0", Inv)}";
    }

    public static string Today => DateTime.Now.ToString("yyyyMMdd", Inv);
}
