using CommunityToolkit.Mvvm.ComponentModel;
using SimpleSmaller.Core;

namespace SimpleSmaller;

internal enum FileStateKind { Waiting, Running, Done, Kept, Failed, Cancelled }

/// 文件的处理状态；按值比较（running 带进度，failed 带原因）。
internal readonly record struct FileState(FileStateKind Kind, double Fraction = 0, string? Message = null)
{
    public static readonly FileState Waiting = new(FileStateKind.Waiting);
    public static readonly FileState Done = new(FileStateKind.Done);
    public static readonly FileState Kept = new(FileStateKind.Kept);
    public static readonly FileState Cancelled = new(FileStateKind.Cancelled);
    public static FileState Running(double fraction) => new(FileStateKind.Running, fraction);
    public static FileState Failed(string message) => new(FileStateKind.Failed, 0, message);

    public bool IsRunning => Kind == FileStateKind.Running;
    public bool IsFailed => Kind == FileStateKind.Failed;
    /// 完成或保留原文件。
    public bool IsDoneOrKept => Kind is FileStateKind.Done or FileStateKind.Kept;

    /// 这一轮里已经有结果（完成、保留原文件、失败）。
    public bool IsFinished => Kind is FileStateKind.Done or FileStateKind.Kept or FileStateKind.Failed;
}

/// 列表里的一个文件。
internal sealed partial class FileItem : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string FilePath { get; }
    /// 从哪个拖入的文件夹来的（交给引擎时传文件夹本身，输出照搬目录结构）。
    public string? Root { get; }
    public MediaType Type { get; }

    [ObservableProperty] private ulong bytes;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Dimensions))] private int width;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Dimensions))] private int height;
    [ObservableProperty] private double duration;
    [ObservableProperty] private double fps;
    [ObservableProperty] private string codec = "";
    [ObservableProperty] private bool hasAudio;
    [ObservableProperty] private bool hdr;
    [ObservableProperty] private int pages;
    [ObservableProperty] private string? metaError;
    /// 界面层的图片（WinUI ImageSource），模型层不认具体类型，由 IPlatform 生成。
    [ObservableProperty] private object? thumbnail;

    [ObservableProperty] private FileState state = FileState.Waiting;
    /// 这个文件的输出目录（开始时按引擎的展开规则算好，失败重试与视频逐段用）。
    [ObservableProperty] private string? outDir;
    /// 加密 PDF 输入密码后解出的临时副本，处理时代替原文件。
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewPath))] private string? unlockedPath;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AfterBytes), nameof(PrimaryPath), nameof(RevealPaths))]
    private Output[] outputs = [];

    public FileItem(string path, string? root, MediaType type)
    {
        FilePath = path;
        Root = root;
        Type = type;
    }

    /// 预览用的文件：解锁过的 PDF 用副本（加密的原文件渲染不出来）。
    public string PreviewPath => UnlockedPath ?? FilePath;
    public string Name => Path.GetFileName(FilePath);
    /// 「保留原图」/「保留原文件」：压完没变小、原样保留时的说法，各处统一。
    public string KeptText => Type == MediaType.Image ? "保留原图" : "保留原文件";
    public ulong AfterBytes => Outputs.Aggregate(0UL, (s, o) => s + o.Bytes);
    public string Dimensions => Width > 0 ? $"{Width} × {Height}" : "—";

    /// 处理完用第一个输出，没处理用原文件：预览、拖出都用它。
    public string PrimaryPath => Outputs.Length > 0 ? Outputs[0].Path : FilePath;
    /// 在资源管理器中显示：全部输出；还没处理就是原文件。
    public IReadOnlyList<string> RevealPaths => Outputs.Length == 0 ? [FilePath] : Outputs.Select(o => o.Path).ToList();
}
