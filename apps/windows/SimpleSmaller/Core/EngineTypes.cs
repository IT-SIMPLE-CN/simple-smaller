using System.Text.Json.Serialization;

namespace SimpleSmaller.Core;

// 与引擎（core/src/api/types.rs）一一对应的数据类型。引擎在独立进程 smaller-core.exe 里，
// 这些类型经行分隔 JSON 传过去（字段名 snake_case，带数据的枚举用 "type" 区分），见 EngineHost。

internal record ClipboardImage(
    /// true 为 PNG，否则 JPEG。
    bool Png,
    uint Width,
    uint Height,
    ulong BeforeBytes,
    byte[] Bytes);

internal record CropBox(string Path, uint X, uint Y, uint Width, uint Height);

internal record CropSetting(
    /// 比例的宽、高份数，如 3、4。
    uint RatioWidth,
    uint RatioHeight,
    /// 输出宽度，高按比例。
    uint Width,
    string? Label);

internal record ImageSettings(
    ImageFormat Format,
    byte Quality,
    bool Lossless,
    ulong? TargetBytes,
    /// 尺寸档位；空表示不缩放，多档时一张出多个文件。
    uint[] Sizes,
    SizeBy SizeBy,
    CropSetting? Crop,
    CropBox[] CropBoxes);

internal record JobItemResult(uint Index, string Source, ulong BeforeBytes, Output[] Outputs, ItemState State, string? Message);

internal record Naming(
    string Template,
    bool Lowercase,
    bool Pinyin,
    bool Hyphenate,
    /// 由界面按本地时区给出，如 20261016。
    string Date);

internal record Output(string Path, ulong Bytes, uint Width, uint Height);

internal record PdfMeta(uint Pages, bool NeedsPassword, ulong Bytes);

internal record PdfPageRef(
    /// 第几个输入文件（从 0 起）。
    uint File,
    uint Page);

internal record PdfSettings(
    PdfOperation Operation,
    bool StripMetadata,
    /// 输出的 PDF 加打开密码（AES-256）；null 不加。
    string? Password);

internal record PlanSummary(uint Files, string[] Skipped);

internal record PlanTarget(string Source, string OutDir);

internal record PlanTargets(PlanTarget[] Items, string[] CreatedRoots);

internal record VideoCropBox(uint X, uint Y, uint Width, uint Height);

internal record VideoMeta(double Duration, uint Width, uint Height, double Fps, string Codec, ulong Bitrate, bool HasAudio, bool Hdr, ulong Bytes);

internal record VideoSettings(
    VideoCodecChoice Codec,
    VideoEncoderChoice Encoder,
    VideoRate Rate,
    uint? MaxShortEdge,
    bool KeepAudio,
    double? TrimStart,
    double? TrimEnd,
    VideoCropBox? Crop,
    bool HdrToSdr,
    double? MaxFps,
    VideoOutput Output,
    /// 同时导出封面（JPG）：取这一秒的画面；null 不导出。
    double? CoverAt);

internal enum ImageFormat { Jpeg, Webp, Avif, Png, Original }

internal enum ItemState { Done, KeptOriginal, Failed, Cancelled }

internal enum Kind { Image, Video, Pdf }

internal enum PdfLevel { Screen, Email, Print }

internal enum PdfPageMode { FitImage, A4 }

internal enum SizeBy { Width, Height, LongEdge }

internal enum VideoCodecChoice { H264, Hevc }

internal enum VideoEncoderChoice { Quality, Fast }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PerSecond), "per_second")]
[JsonDerivedType(typeof(EverySeconds), "every_seconds")]
[JsonDerivedType(typeof(Total), "total")]
[JsonDerivedType(typeof(All), "all")]
internal record FramePick
{
    public record PerSecond(double Count) : FramePick;
    public record EverySeconds(double Seconds) : FramePick;
    public record Total(uint Count) : FramePick;
    public record All : FramePick;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Compress), "compress")]
[JsonDerivedType(typeof(Assemble), "assemble")]
[JsonDerivedType(typeof(Merge), "merge")]
[JsonDerivedType(typeof(Split), "split")]
[JsonDerivedType(typeof(ToImages), "to_images")]
[JsonDerivedType(typeof(FromImages), "from_images")]
internal record PdfOperation
{
    public record Compress(PdfLevel Level, ulong? TargetBytes, bool SubsetFonts, bool Grayscale) : PdfOperation;
    /// 从所有输入里按给定顺序挑页拼成一个文件（合并时排序、删页）。
    public record Assemble(PdfPageRef[] Pages) : PdfOperation;
    /// 所有输入按顺序合成一个文件。
    public record Merge : PdfOperation;
    /// 每 `every` 页拆一个文件。
    public record Split(uint Every) : PdfOperation;
    public record ToImages(uint Width, ImageFormat Format, byte Quality, bool LongImage) : PdfOperation;
    /// 输入是图片，合成一个 PDF。
    public record FromImages(PdfPageMode PageMode) : PdfOperation;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BesideSource), "beside_source")]
[JsonDerivedType(typeof(Folder), "folder")]
internal record Place
{
    public record BesideSource(string FolderSuffix) : Place;
    public record Folder(string Path) : Place;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Mp4), "mp4")]
[JsonDerivedType(typeof(Mute), "mute")]
[JsonDerivedType(typeof(Gif), "gif")]
[JsonDerivedType(typeof(Frames), "frames")]
[JsonDerivedType(typeof(Live), "live")]
internal record VideoOutput
{
    public record Mp4 : VideoOutput;
    /// 只去声音，视频流原样复制。
    public record Mute : VideoOutput;
    public record Gif(double Fps, uint Width, byte Quality) : VideoOutput;
    public record Frames(FramePick Pick, uint? Width, ImageFormat Format, byte Quality, bool Zip, bool ContactSheet) : VideoOutput;
    /// 实况：截取范围里取一段（不超过 3 秒，更长时以封面为中心取 3 秒）+ 封面帧。
    /// `Apple`：同名 JPEG + MOV（苹果实况）；`Android`：一张末尾接 MP4 的 JPEG（动态照片，文件名 `名字.MP.jpg`）。
    /// `CoverAt` 是封面在原视频里的秒数；`MaxLongEdge` 是视频最长边上限（0 不缩小）。
    public record Live(bool Apple, bool Android, double CoverAt, uint MaxLongEdge) : VideoOutput;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(High), "high")]
[JsonDerivedType(typeof(Medium), "medium")]
[JsonDerivedType(typeof(Low), "low")]
[JsonDerivedType(typeof(TargetBytes), "target_bytes")]
internal record VideoRate
{
    public record High : VideoRate;
    public record Medium : VideoRate;
    public record Low : VideoRate;
    public record TargetBytes(ulong Bytes) : VideoRate;
}

/// 任务回调，在引擎宿主的读线程上调用（ListenerBridge 再转到界面线程）。
internal interface JobListener
{
    /// 单个文件的进度（0–1），视频与 PDF 用；开始处理某张图片时报 0。
    void OnProgress(uint index, double fraction);
    void OnItem(JobItemResult result);
    void OnFinished();
}

/// 引擎报错。界面按种类给出提示，`message` 已是中文。
internal class EngineException : Exception
{
    private EngineException(string message) : base(message) { }

    public sealed class Io : EngineException
    {
        public readonly string message;
        public Io(string message) : base(message) { this.message = message; }
    }

    public sealed class Unsupported : EngineException
    {
        public readonly string message;
        public Unsupported(string message) : base(message) { this.message = message; }
    }

    public sealed class Failed : EngineException
    {
        public readonly string message;
        public Failed(string message) : base(message) { this.message = message; }
    }
}
